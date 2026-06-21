using CoffeeMovie.Core.Models;
using CoffeeMovie.Storage.Models;
using CoffeeMovie.Storage.Services;

var srt = """
1
00:00:01,000 --> 00:00:03,250
朝のコーヒーを淹れる。

2
00:01:10,500 --> 00:01:12,000
ここが見たいシーン。
""";

var document = SubtitleParser.Parse(srt, "sample.srt");
Assert(document.Format == SubtitleFormat.Srt, "SRT format should be detected.");
Assert(document.Cues.Count == 2, "SRT cue count should be 2.");
Assert(document.Cues[1].Start == TimeSpan.FromSeconds(70.5), "Second cue start should parse milliseconds.");

var webVtt = SubtitleParser.ToWebVtt(document.Cues);
Assert(webVtt.StartsWith("WEBVTT", StringComparison.Ordinal), "Converted subtitle should be WebVTT.");

var rewrittenSrt = SubtitleParser.ToSrt(document.Cues);
Assert(rewrittenSrt.Contains("00:01:10,500 --> 00:01:12,000", StringComparison.Ordinal), "SRT rewrite should keep comma milliseconds.");

var reparsed = SubtitleParser.Parse(webVtt, "sample.vtt");
Assert(reparsed.Format == SubtitleFormat.WebVtt, "WebVTT format should be detected.");
Assert(reparsed.Cues.Count == 2, "Converted WebVTT cue count should remain 2.");

var consensus = SubtitleConsensusService.Merge(
[
    [
        new SubtitleCue { Index = 1, Start = TimeSpan.FromSeconds(1), End = TimeSpan.FromSeconds(2), Text = "hello there" },
        new SubtitleCue { Index = 2, Start = TimeSpan.FromSeconds(4), End = TimeSpan.FromSeconds(5), Text = "missing line" }
    ],
    [
        new SubtitleCue { Index = 1, Start = TimeSpan.FromSeconds(1.2), End = TimeSpan.FromSeconds(2.2), Text = "hello there" }
    ],
    [
        new SubtitleCue { Index = 1, Start = TimeSpan.FromSeconds(3), End = TimeSpan.FromSeconds(4), Text = "hello there" },
        new SubtitleCue { Index = 2, Start = TimeSpan.FromSeconds(8), End = TimeSpan.FromSeconds(9), Text = "extra line" }
    ]
]);
Assert(consensus.Count == 3, "Consensus merge should keep missing cues from any run.");
Assert(Math.Abs((consensus[0].Start - TimeSpan.FromSeconds(1.2)).TotalMilliseconds) < 1, "Consensus merge should use median timing.");

var englishMetadata = SubtitleFileMetadataService.Infer("sample.en.srt");
Assert(englishMetadata.Language == "en", "English subtitle language should be inferred from .en suffix.");
Assert(englishMetadata.Role == SubtitleTrackRole.LearningTarget, "English subtitles should default to learning target role.");
Assert(englishMetadata.GroupKey == "sample", "Subtitle group key should remove language suffix.");

var japaneseMetadata = SubtitleFileMetadataService.Infer("sample.ja.srt");
Assert(japaneseMetadata.Language == "ja", "Japanese subtitle language should be inferred from .ja suffix.");
Assert(japaneseMetadata.Role == SubtitleTrackRole.Translation, "Japanese subtitles should default to translation role.");
Assert(japaneseMetadata.GroupKey == "sample", "Japanese subtitle group key should match English subtitle group key.");

var root = Path.Combine(Path.GetTempPath(), "coffee-movie-verification-" + Guid.NewGuid().ToString("N"));
var cache = Path.Combine(Path.GetTempPath(), "coffee-movie-cache-" + Guid.NewGuid().ToString("N"));

try
{
    var paths = new CoffeeMoviePaths(root, cache);
    paths.EnsureCreated();

    var movie = new Movie
    {
        Id = "movie-test",
        Title = "Verification Movie",
        Video = new VideoAsset
        {
            SourceKind = VideoSourceKind.GoogleDrive,
            SourceUri = "gdrive://files/video-1",
            SourceKey = "gdrive://files/video-1",
            FileName = "sample.mp4",
            ContentType = "video/mp4",
            SizeBytes = 100
        }
    };

    var libraryStore = new MovieLibraryStore(paths);
    await libraryStore.UpsertMovieAsync(movie);
    var library = await libraryStore.LoadAsync();
    Assert(library.Movies.Count == 1, "Movie library should round-trip JSON.");

    var localVideo = Path.Combine(paths.VideoCachePath, "movie-test", "sample.mp4");
    Directory.CreateDirectory(Path.GetDirectoryName(localVideo)!);
    await File.WriteAllTextAsync(localVideo, "cache");

    var cacheStore = new MovieCacheStore(paths);
    var modifiedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
    await cacheStore.UpsertAsync(new MovieCacheEntry
    {
        SourceKey = "gdrive://files/video-1",
        MovieId = "movie-test",
        FileName = "sample.mp4",
        LocalPath = localVideo,
        ContentType = "video/mp4",
        SizeBytes = 100,
        SourceModifiedAt = modifiedAt,
        CachedAt = DateTimeOffset.UtcNow
    });

    var freshEntry = await cacheStore.FindFreshEntryAsync("gdrive://files/video-1", 100, modifiedAt, null);
    Assert(freshEntry is not null, "Cache entry should be considered fresh when metadata matches.");

    var staleEntry = await cacheStore.FindFreshEntryAsync("gdrive://files/video-1", 101, modifiedAt, null);
    Assert(staleEntry is null, "Cache entry should be stale when size changes.");
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }

    if (Directory.Exists(cache))
    {
        Directory.Delete(cache, recursive: true);
    }
}

Console.WriteLine("CoffeeMovie verification passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}


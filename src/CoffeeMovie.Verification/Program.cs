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

var registrationSyncService = new CoffeeLearningRegistrationSyncService();
var registeredAt = DateTimeOffset.UtcNow.AddMinutes(-2);
var sourceLibrary = new MovieLibrary
{
    Movies =
    [
        new Movie
        {
            Id = "movie-registration-sync",
            Title = "Registration Sync",
            SubtitleTracks =
            [
                new SubtitleTrack
                {
                    Id = "track-registration-sync",
                    SourceFileName = "registration.en.srt",
                    Language = "en",
                    Role = SubtitleTrackRole.LearningTarget,
                    CueCount = 1,
                    CueLearningStates =
                    [
                        new SubtitleCueLearningState
                        {
                            CueId = "cue-registration-sync",
                            CueIndex = 1,
                            CoffeeLearningRegisteredAt = registeredAt,
                            CoffeeLearningWordId = "word-registration-sync",
                            CoffeeLearningDeckId = "deck-registration-sync",
                            UpdatedAt = registeredAt
                        }
                    ]
                }
            ]
        }
    ]
};
var targetLibrary = new MovieLibrary
{
    Movies =
    [
        new Movie
        {
            Id = "movie-registration-sync",
            Title = "Registration Sync",
            SubtitleTracks =
            [
                new SubtitleTrack
                {
                    Id = "track-registration-sync",
                    SourceFileName = "registration.en.srt",
                    Language = "en",
                    Role = SubtitleTrackRole.LearningTarget,
                    CueCount = 1,
                    CueLearningStates =
                    [
                        new SubtitleCueLearningState
                        {
                            CueId = "cue-registration-sync",
                            CueIndex = 1,
                            Note = "Keep my own note"
                        }
                    ]
                }
            ]
        }
    ]
};
var registrationDocument = registrationSyncService.CreateDocument(sourceLibrary);
var registrationMerge = registrationSyncService.Merge(targetLibrary, registrationDocument);
var mergedRegistrationState = targetLibrary.Movies[0].SubtitleTracks[0].CueLearningStates[0];
Assert(registrationMerge.CuesChanged == 1, "Registration sync should report one changed cue.");
Assert(mergedRegistrationState.CoffeeLearningRegisteredAt == registeredAt, "Registration timestamp should sync.");
Assert(mergedRegistrationState.CoffeeLearningWordId == "word-registration-sync", "Registration word id should sync.");
Assert(mergedRegistrationState.CoffeeLearningDeckId == "deck-registration-sync", "Registration deck id should sync.");
Assert(mergedRegistrationState.Note == "Keep my own note", "Registration sync should preserve the user note.");
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

    var driveExportDirectory = Path.Combine(root, "drive-export");
    var packageVideoPath = Path.Combine(root, "package-source", "metadata-test.mp4");
    Directory.CreateDirectory(Path.GetDirectoryName(packageVideoPath)!);
    await File.WriteAllBytesAsync(packageVideoPath, [1, 2, 3, 4, 5, 6]);
    var packageMovie = new Movie
    {
        Id = "movie-metadata-export",
        Title = "Metadata Export",
        UpdatedAt = DateTimeOffset.UtcNow,
        Video = new VideoAsset
        {
            SourceKind = VideoSourceKind.LocalFile,
            CachePath = packageVideoPath,
            FileName = "metadata-test.mp4",
            ContentType = "video/mp4",
            SizeBytes = new FileInfo(packageVideoPath).Length,
            ModifiedAt = modifiedAt,
            ContentFingerprint = "video-v1"
        }
    };
    var packageService = new CoffeeMoviePackageService();
    var initialPackage = await packageService.ExportReaderPackageAsync(packageMovie, driveExportDirectory);
    var initialPackageBytes = await File.ReadAllBytesAsync(initialPackage.PackagePath);

    packageMovie.Tags.Add("updated-on-pc");
    packageMovie.UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(1);
    var metadataExport = await packageService.ExportReaderMetadataAsync(packageMovie, driveExportDirectory);
    Assert(metadataExport.MetadataOnly && !metadataExport.Skipped, "Metadata changes should update only the sidecar.");
    var metadataOnlyPackageBytes = await File.ReadAllBytesAsync(initialPackage.PackagePath);
    Assert(initialPackageBytes.SequenceEqual(metadataOnlyPackageBytes), "Metadata-only export must not rewrite package bytes.");
    var latestSidecar = await packageService.ReadReaderPackageSidecarAsync(metadataExport.SidecarPath);
    Assert(latestSidecar.Movie.Tags.Contains("updated-on-pc"), "Metadata-only sidecar should contain updated movie tags.");

    var importedFromStalePackage = await packageService.ImportReaderPackageAsync(
        initialPackage.PackagePath,
        paths,
        authoritativeSidecar: latestSidecar);
    Assert(importedFromStalePackage.Tags.Contains("updated-on-pc"), "Authoritative sidecar metadata should win over the older package manifest.");
    Assert(importedFromStalePackage.SourceContentFingerprint == latestSidecar.ContentFingerprint, "Imported movie should keep the latest sidecar fingerprint.");

    var unchangedMetadata = await packageService.ExportReaderMetadataAsync(packageMovie, driveExportDirectory);
    Assert(unchangedMetadata.Skipped, "Unchanged metadata should be skipped.");

    packageMovie.Video.ContentFingerprint = "video-v2";
    var videoUpdate = await packageService.ExportReaderMetadataAsync(packageMovie, driveExportDirectory);
    Assert(!videoUpdate.MetadataOnly && !videoUpdate.Skipped, "A changed video identity should rebuild the package.");
    var salvagePaths = new CoffeeMoviePaths(Path.Combine(root, "salvage-library"), Path.Combine(cache, "salvage-cache"));
    var salvageStore = new MovieLibraryStore(salvagePaths);
    await salvageStore.SaveAsync(new MovieLibrary
    {
        Movies =
        [
            new Movie { Id = "recovery-movie-1", Title = "Complete movie" },
            new Movie { Id = "recovery-movie-2", Title = "Truncated movie" }
        ]
    });
    var salvageJson = await File.ReadAllTextAsync(salvagePaths.LibraryPath);
    var truncatedMovieOffset = salvageJson.IndexOf("\"id\": \"recovery-movie-2\"", StringComparison.Ordinal);
    var truncateOffset = salvageJson.IndexOf("\"video\"", truncatedMovieOffset, StringComparison.Ordinal);
    Assert(truncatedMovieOffset > 0 && truncateOffset > truncatedMovieOffset, "Recovery fixture should find the second movie.");
    await File.WriteAllTextAsync(salvagePaths.LibraryPath, salvageJson[..(truncateOffset + 12)]);

    var recoveredStore = new MovieLibraryStore(salvagePaths);
    var salvagedLibrary = await recoveredStore.LoadAsync();
    Assert(salvagedLibrary.Movies.Count == 1, "Truncated library recovery should keep every fully completed movie.");
    Assert(salvagedLibrary.Movies[0].Id == "recovery-movie-1", "Truncated library recovery should exclude the incomplete movie.");
    Assert(!string.IsNullOrWhiteSpace(recoveredStore.ConsumeRecoveryMessage()), "Truncated library recovery should report what was recovered.");
    Assert(Directory.EnumerateFiles(salvagePaths.RootPath, "library.json.corrupt-*").Any(), "Truncated library recovery should preserve the corrupt source file.");

    var backupPaths = new CoffeeMoviePaths(Path.Combine(root, "backup-library"), Path.Combine(cache, "backup-cache"));
    var backupStore = new MovieLibraryStore(backupPaths);
    await backupStore.SaveAsync(new MovieLibrary
    {
        Movies = [new Movie { Id = "backup-v1", Title = "Backup version" }]
    });
    await backupStore.SaveAsync(new MovieLibrary
    {
        Movies = [new Movie { Id = "backup-v2", Title = "Current version" }]
    });
    await File.WriteAllTextAsync(backupPaths.LibraryPath, "{ truncated");

    var restoredBackupStore = new MovieLibraryStore(backupPaths);
    var restoredBackupLibrary = await restoredBackupStore.LoadAsync();
    Assert(restoredBackupLibrary.Movies.Count == 1 && restoredBackupLibrary.Movies[0].Id == "backup-v1", "A valid automatic backup should take precedence over partial salvage.");
    Assert(!File.Exists(backupPaths.LibraryPath + ".tmp"), "Atomic save should not leave a temporary file behind.");
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


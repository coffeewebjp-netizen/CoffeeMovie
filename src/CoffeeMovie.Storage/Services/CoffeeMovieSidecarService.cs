using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Storage.Models;

namespace CoffeeMovie.Storage.Services;

public static class CoffeeMovieSidecarService
{
    public static CoffeeMovieSidecar Create(
        Movie movie,
        string? packageFileName = null,
        long? packageSizeBytes = null,
        string? contentFingerprint = null)
    {
        var fingerprint = contentFingerprint ?? ComputeContentFingerprint(movie);
        var thumbnail = CreateThumbnailPayload(movie.Video.ThumbnailPath);
        return new CoffeeMovieSidecar
        {
            SourceMovieId = movie.Id,
            ContentFingerprint = fingerprint,
            PackageFileName = packageFileName,
            PackageSizeBytes = packageSizeBytes,
            Movie = new CoffeeMovieSidecarMovie
            {
                Id = movie.Id,
                Title = movie.Title,
                SeriesTitle = movie.SeriesTitle,
                SeasonNumber = movie.SeasonNumber,
                EpisodeNumber = movie.EpisodeNumber,
                Description = movie.Description,
                DurationSeconds = movie.Playback.DurationSeconds,
                Tags = (movie.Tags ?? []).ToList(),
                CreatedAt = movie.CreatedAt,
                UpdatedAt = movie.UpdatedAt
            },
            Video = new CoffeeMovieSidecarVideo
            {
                FileName = movie.Video.FileName,
                SourceKey = movie.Video.SourceKey,
                ContentType = movie.Video.ContentType,
                SizeBytes = movie.Video.SizeBytes,
                ModifiedAt = movie.Video.ModifiedAt,
                ContentFingerprint = movie.Video.ContentFingerprint,
                ThumbnailFileName = thumbnail.FileName,
                ThumbnailContentType = thumbnail.ContentType,
                ThumbnailDataBase64 = thumbnail.DataBase64,
                ThumbnailTimestampSeconds = movie.Video.ThumbnailTimestampSeconds
            },
            Subtitles = movie.SubtitleTracks
                .Select(track => new CoffeeMovieSidecarSubtitle
                {
                    Id = track.Id,
                    FileName = track.SourceFileName,
                    SourceFileName = track.SourceFileName,
                    Label = track.Label,
                    Language = track.Language,
                    Role = track.Role.ToString(),
                    GroupKey = track.GroupKey,
                    Format = track.Format.ToString(),
                    CueCount = track.CueCount,
                    ContentFingerprint = ComputeSubtitleFingerprint(track),
                    Cues = (track.Cues ?? [])
                        .OrderBy(cue => cue.Index)
                        .Select(cue => new CoffeeMovieSidecarCue
                        {
                            Id = cue.Id,
                            Index = cue.Index,
                            StartSeconds = cue.Start.TotalSeconds,
                            EndSeconds = cue.End.TotalSeconds,
                            Text = cue.Text
                        })
                        .ToList(),
                    LearningStates = (track.CueLearningStates ?? [])
                        .OrderBy(state => state.CueIndex)
                        .ToList()
                })
                .ToList()
        };
    }

    public static async Task WriteAsync(
        Movie movie,
        string path,
        CancellationToken cancellationToken = default)
    {
        var sidecar = Create(movie);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, sidecar, JsonStoreOptions.Default, cancellationToken);
    }

    public static string ComputeContentFingerprint(Movie movie)
    {
        var builder = new StringBuilder();
        builder.AppendLine(movie.Id);
        builder.AppendLine(movie.Title);
        builder.AppendLine(movie.SeriesTitle ?? string.Empty);
        builder.AppendLine(movie.SeasonNumber?.ToString() ?? string.Empty);
        builder.AppendLine(movie.EpisodeNumber?.ToString() ?? string.Empty);
        builder.AppendLine(movie.Description ?? string.Empty);
        builder.AppendLine(movie.Playback.DurationSeconds.ToString("0.###"));
        builder.AppendLine(movie.Video.FileName);
        builder.AppendLine(movie.Video.SizeBytes.ToString());
        builder.AppendLine(movie.Video.ModifiedAt?.ToUnixTimeMilliseconds().ToString() ?? string.Empty);
        builder.AppendLine(movie.Video.ContentFingerprint ?? string.Empty);
        builder.AppendLine(movie.Video.ThumbnailTimestampSeconds?.ToString("0.###") ?? string.Empty);
        var thumbnailFingerprint = ComputeFileFingerprint(movie.Video.ThumbnailPath);
        if (!string.IsNullOrWhiteSpace(thumbnailFingerprint))
        {
            builder.Append("thumbnail-file:");
            builder.AppendLine(thumbnailFingerprint);
        }

        foreach (var tag in (movie.Tags ?? []).OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("movie-tag:");
            builder.AppendLine(tag);
        }

        foreach (var track in movie.SubtitleTracks.OrderBy(track => track.Language).ThenBy(track => track.SourceFileName))
        {
            builder.AppendLine(ComputeSubtitleFingerprint(track));
        }

        return Sha256(builder.ToString());
    }

    private static ThumbnailPayload CreateThumbnailPayload(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new ThumbnailPayload(null, null, null);
        }

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0)
        {
            return new ThumbnailPayload(null, null, null);
        }

        return new ThumbnailPayload(
            Path.GetFileName(path),
            GuessImageContentType(path),
            Convert.ToBase64String(bytes));
    }

    private static string? ComputeFileFingerprint(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        var bytes = SHA256.HashData(stream);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GuessImageContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
    }

    public static string ComputeSubtitleFingerprint(SubtitleTrack track)
    {
        var builder = new StringBuilder();
        builder.AppendLine(track.Id);
        builder.AppendLine(track.Label);
        builder.AppendLine(track.Language ?? string.Empty);
        builder.AppendLine(track.Role.ToString());
        builder.AppendLine(track.Format.ToString());
        builder.AppendLine(track.SourceFileName);

        foreach (var cue in (track.Cues ?? []).OrderBy(cue => cue.Index))
        {
            builder.Append(cue.Index);
            builder.Append('|');
            builder.Append(cue.Start.TotalMilliseconds.ToString("0"));
            builder.Append('|');
            builder.Append(cue.End.TotalMilliseconds.ToString("0"));
            builder.Append('|');
            builder.AppendLine(cue.Text);
        }

        foreach (var state in (track.CueLearningStates ?? []).OrderBy(state => state.CueIndex))
        {
            builder.Append(state.CueIndex);
            builder.Append('|');
            builder.Append(state.IsFlagged);
            builder.Append('|');
            builder.Append(string.Join(',', (state.Tags ?? []).OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)));
            builder.Append('|');
            builder.Append(state.Note);
            builder.Append('|');
            builder.Append(state.AiNote);
            builder.Append('|');
            builder.Append(state.CoffeeLearningRegisteredAt?.ToUnixTimeMilliseconds().ToString() ?? string.Empty);
            builder.Append('|');
            builder.Append(state.CoffeeLearningWordId ?? string.Empty);
            builder.Append('|');
            builder.Append(state.CoffeeLearningDeckId ?? string.Empty);
            builder.Append('|');
            AppendMetric(builder, state.Listening);
            builder.Append('|');
            AppendMetric(builder, state.Shadowing);
            builder.AppendLine();
        }

        return Sha256(builder.ToString());
    }

    private static void AppendMetric(StringBuilder builder, CuePracticeMetric? metric)
    {
        metric ??= new CuePracticeMetric();
        builder.Append(metric.AttemptCount);
        builder.Append('/');
        builder.Append(metric.OkCount);
        builder.Append('/');
        builder.Append(metric.NgCount);
        builder.Append('/');
        builder.Append(metric.LastAccuracy?.ToString("0.###") ?? string.Empty);
        builder.Append('/');
        builder.Append(metric.BestAccuracy?.ToString("0.###") ?? string.Empty);
        builder.Append('/');
        builder.Append(metric.LastTranscript ?? string.Empty);
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record ThumbnailPayload(string? FileName, string? ContentType, string? DataBase64);
}


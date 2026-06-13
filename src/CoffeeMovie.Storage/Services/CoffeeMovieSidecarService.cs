using System.Text.Json;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Storage.Models;

namespace CoffeeMovie.Storage.Services;

public static class CoffeeMovieSidecarService
{
    public static CoffeeMovieSidecar Create(Movie movie)
    {
        return new CoffeeMovieSidecar
        {
            Movie = new CoffeeMovieSidecarMovie
            {
                Id = movie.Id,
                Title = movie.Title,
                Description = movie.Description,
                DurationSeconds = movie.Playback.DurationSeconds,
                Tags = movie.Tags.ToList()
            },
            Video = new CoffeeMovieSidecarVideo
            {
                FileName = movie.Video.FileName,
                SourceKey = movie.Video.SourceKey,
                ContentType = movie.Video.ContentType,
                SizeBytes = movie.Video.SizeBytes,
                ModifiedAt = movie.Video.ModifiedAt,
                ContentFingerprint = movie.Video.ContentFingerprint
            },
            Subtitles = movie.SubtitleTracks
                .Select(track => new CoffeeMovieSidecarSubtitle
                {
                    FileName = track.SourceFileName,
                    Label = track.Label,
                    Language = track.Language,
                    Format = track.Format.ToString(),
                    CueCount = track.CueCount
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
}


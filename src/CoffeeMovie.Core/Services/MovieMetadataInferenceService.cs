using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CoffeeMovie.Core.Models;

namespace CoffeeMovie.Core.Services;

public static class MovieMetadataInferenceService
{
    public static InferredMovieMetadata InferFromFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var seasonEpisodeMatch = Regex.Match(name, @"\bS(?<season>\d{1,2})\s*E(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase);
        if (seasonEpisodeMatch.Success)
        {
            return new InferredMovieMetadata(
                CleanSeriesTitle(name[..seasonEpisodeMatch.Index]),
                ParsePositiveInt(seasonEpisodeMatch.Groups["season"].Value),
                ParsePositiveInt(seasonEpisodeMatch.Groups["episode"].Value));
        }

        var episodeMatch = Regex.Match(name, @"\bE(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase);
        if (episodeMatch.Success)
        {
            return new InferredMovieMetadata(
                CleanSeriesTitle(name[..episodeMatch.Index]),
                null,
                ParsePositiveInt(episodeMatch.Groups["episode"].Value));
        }

        return new InferredMovieMetadata(null, null, null);
    }

    public static string FormatSeasonEpisode(Movie movie)
    {
        var season = movie.SeasonNumber is null ? string.Empty : $"S{movie.SeasonNumber.Value:00}";
        var episode = movie.EpisodeNumber is null ? string.Empty : $"E{movie.EpisodeNumber.Value:00}";
        return string.Join(' ', new[] { season, episode }.Where(part => part.Length > 0));
    }

    private static string? CleanSeriesTitle(string value)
    {
        var cleaned = Regex.Replace(value, @"[-_\u30FB\s]+$", string.Empty).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static int? ParsePositiveInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;
    }
}

public sealed record InferredMovieMetadata(string? SeriesTitle, int? SeasonNumber, int? EpisodeNumber);

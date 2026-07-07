using CoffeeMovie.Core.Models;
using CoffeeMovie.Core.Services;

namespace CoffeeMovie.Studio.Services;

public static class TagFilterService
{
    public static bool MatchesMovie(
        Movie movie,
        string? searchText,
        string? movieTagFilterText,
        string? subtitleTagFilterText,
        string flagTagName)
    {
        var search = NormalizeOptionalText(searchText);
        if (!string.IsNullOrWhiteSpace(search)
            && !ContainsText(movie.Title, search)
            && !ContainsText(movie.SeriesTitle, search)
            && !ContainsText(MovieMetadataInferenceService.FormatSeasonEpisode(movie), search))
        {
            return false;
        }

        var movieTagFilters = ParseTags(movieTagFilterText);
        if (movieTagFilters.Count > 0
            && !movieTagFilters.All(filter => movie.Tags.Any(tag => ContainsText(tag, filter))))
        {
            return false;
        }

        var subtitleTagFilters = ParseTags(subtitleTagFilterText);
        return subtitleTagFilters.Count == 0 || MovieHasSubtitleTags(movie, subtitleTagFilters, flagTagName);
    }

    public static List<string> ParseTags(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Split([',', '\u3001', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsFlagTag(string tag, string flagTagName)
    {
        return string.Equals(tag, flagTagName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool ContainsText(string? value, string search)
    {
        return value?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool MovieHasSubtitleTags(Movie movie, IReadOnlyCollection<string> filters, string flagTagName)
    {
        return movie.SubtitleTracks
            .SelectMany(track => track.CueLearningStates)
            .Any(state =>
            {
                var tags = state.IsFlagged
                    ? state.Tags.Concat([flagTagName])
                    : state.Tags;
                return filters.All(filter => tags.Any(tag => ContainsText(tag, filter)));
            });
    }

    private static string? NormalizeOptionalText(string? text)
    {
        var normalized = text?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}

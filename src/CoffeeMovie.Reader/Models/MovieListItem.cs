namespace CoffeeMovie.Reader.Models;

public sealed class MovieListItem
{
    public string MovieId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public string SeriesKey { get; set; } = string.Empty;

    public string SeriesTitle { get; set; } = string.Empty;

    public string SeasonKey { get; set; } = string.Empty;

    public string SeasonTitle { get; set; } = string.Empty;

    public string SeriesDetail { get; set; } = string.Empty;

    public bool HasSeriesDetail { get; set; }

    public string TagsDetail { get; set; } = string.Empty;

    public bool HasTagsDetail { get; set; }

    public string CacheState { get; set; } = string.Empty;

    public string? ThumbnailPath { get; set; }

    public bool HasThumbnail { get; set; }

    public bool HasNoThumbnail => !HasThumbnail;

    public string ActionText { get; set; } = string.Empty;

    public bool HasAction { get; set; }
}


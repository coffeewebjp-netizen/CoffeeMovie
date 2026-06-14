namespace CoffeeMovie.Reader.Models;

public sealed class MovieListItem
{
    public string MovieId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public string CacheState { get; set; } = string.Empty;

    public string ActionText { get; set; } = string.Empty;

    public bool HasAction { get; set; }
}


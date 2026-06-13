namespace CoffeeMovie.Core.Models;

public sealed class SceneMarker
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Label { get; set; } = string.Empty;

    public TimeSpan Start { get; set; }

    public TimeSpan? End { get; set; }

    public string? SourceSubtitleTrackId { get; set; }

    public int? SourceCueIndex { get; set; }
}


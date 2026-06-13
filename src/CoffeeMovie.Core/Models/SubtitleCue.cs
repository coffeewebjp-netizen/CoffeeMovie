namespace CoffeeMovie.Core.Models;

public sealed class SubtitleCue
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public int Index { get; set; }

    public TimeSpan Start { get; set; }

    public TimeSpan End { get; set; }

    public string Text { get; set; } = string.Empty;
}


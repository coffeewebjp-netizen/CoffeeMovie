namespace CoffeeMovie.Core.Models;

public sealed class SubtitleCue
{
    public int Index { get; set; }

    public TimeSpan Start { get; set; }

    public TimeSpan End { get; set; }

    public string Text { get; set; } = string.Empty;
}


namespace CoffeeMovie.Core.Models;

public sealed class SubtitleDocument
{
    public SubtitleFormat Format { get; set; } = SubtitleFormat.Unknown;

    public string? SourceFileName { get; set; }

    public List<SubtitleCue> Cues { get; set; } = [];
}


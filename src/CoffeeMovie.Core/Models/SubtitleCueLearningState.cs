namespace CoffeeMovie.Core.Models;

public sealed class SubtitleCueLearningState
{
    public string CueId { get; set; } = string.Empty;

    public int CueIndex { get; set; }

    public bool IsFlagged { get; set; }

    public List<string> Tags { get; set; } = [];

    public string? Note { get; set; }

    public string? AiNote { get; set; }

    public CuePracticeMetric Listening { get; set; } = new();

    public CuePracticeMetric Shadowing { get; set; } = new();

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

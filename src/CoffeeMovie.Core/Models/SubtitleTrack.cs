namespace CoffeeMovie.Core.Models;

public sealed class SubtitleTrack
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Label { get; set; } = string.Empty;

    public string? Language { get; set; }

    public SubtitleTrackRole Role { get; set; } = SubtitleTrackRole.Unknown;

    public string? GroupKey { get; set; }

    public SubtitleFormat Format { get; set; } = SubtitleFormat.Unknown;

    public string? SourceUri { get; set; }

    public string SourceFileName { get; set; } = string.Empty;

    public string LocalPath { get; set; } = string.Empty;

    public string? VttCachePath { get; set; }

    public int CueCount { get; set; }

    public List<SubtitleCue> Cues { get; set; } = [];

    public List<SubtitleCueLearningState> CueLearningStates { get; set; } = [];

    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
}


namespace CoffeeMovie.Storage.Models;

public sealed class CoffeeLearningRegistrationSyncDocument
{
    public int SchemaVersion { get; set; } = 1;

    public string PackageType { get; set; } = "coffeelearning-registration-state";

    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<CoffeeLearningRegistrationSyncMovie> Movies { get; set; } = [];
}

public sealed class CoffeeLearningRegistrationSyncMovie
{
    public string MovieId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? SourceContentFingerprint { get; set; }

    public string? SourcePackageName { get; set; }

    public List<CoffeeLearningRegistrationSyncTrack> SubtitleTracks { get; set; } = [];
}

public sealed class CoffeeLearningRegistrationSyncTrack
{
    public string TrackId { get; set; } = string.Empty;

    public string SourceFileName { get; set; } = string.Empty;

    public string? Language { get; set; }

    public string Role { get; set; } = string.Empty;

    public string? GroupKey { get; set; }

    public int CueCount { get; set; }

    public List<CoffeeLearningRegistrationSyncCue> Cues { get; set; } = [];
}

public sealed class CoffeeLearningRegistrationSyncCue
{
    public string CueId { get; set; } = string.Empty;

    public int CueIndex { get; set; }

    public DateTimeOffset? RegisteredAt { get; set; }

    public string? WordId { get; set; }

    public string? DeckId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record CoffeeLearningRegistrationSyncMergeResult(
    int MoviesChanged,
    int TracksChanged,
    int CuesChanged,
    int MoviesSkipped);

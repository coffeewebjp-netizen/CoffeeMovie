using CoffeeMovie.Core.Models;

namespace CoffeeMovie.Reader.Models;

public sealed class LearningStateBackup
{
    public int SchemaVersion { get; set; } = 1;

    public string PackageType { get; set; } = "reader-learning-state";

    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<LearningStateBackupMovie> Movies { get; set; } = [];
}

public sealed class LearningStateBackupMovie
{
    public string MovieId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? SeriesTitle { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public string? SourcePackageUri { get; set; }

    public string? SourceContentFingerprint { get; set; }

    public List<string> Tags { get; set; } = [];

    public PlaybackState Playback { get; set; } = new();

    public DateTimeOffset UpdatedAt { get; set; }

    public List<LearningStateBackupTrack> SubtitleTracks { get; set; } = [];
}

public sealed class LearningStateBackupTrack
{
    public string TrackId { get; set; } = string.Empty;

    public string SourceFileName { get; set; } = string.Empty;

    public string? Language { get; set; }

    public string Role { get; set; } = string.Empty;

    public string? GroupKey { get; set; }

    public int CueCount { get; set; }

    public List<SubtitleCueLearningState> LearningStates { get; set; } = [];
}

public sealed record LearningStateBackupExportResult(
    string FilePath,
    int MovieCount,
    int LearningStateCount);

public sealed record LearningStateBackupImportResult(
    int MoviesChanged,
    int TracksChanged,
    int LearningStateCount,
    int MoviesSkipped);

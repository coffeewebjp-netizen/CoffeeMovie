using CoffeeMovie.Core.Models;

namespace CoffeeMovie.Storage.Models;

public sealed class CoffeeMovieSidecar
{
    public int SchemaVersion { get; set; } = 1;

    public string PackageType { get; set; } = "reader-sidecar";

    public string SourceMovieId { get; set; } = string.Empty;

    public string? ContentFingerprint { get; set; }

    public string? PackageFileName { get; set; }

    public long? PackageSizeBytes { get; set; }

    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;

    public CoffeeMovieSidecarMovie Movie { get; set; } = new();

    public CoffeeMovieSidecarVideo Video { get; set; } = new();

    public List<CoffeeMovieSidecarSubtitle> Subtitles { get; set; } = [];
}

public sealed class CoffeeMovieSidecarMovie
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? SeriesTitle { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public string? Description { get; set; }

    public double DurationSeconds { get; set; }

    public List<string> Tags { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CoffeeMovieSidecarVideo
{
    public string FileName { get; set; } = string.Empty;

    public string? SourceKey { get; set; }

    public string? ContentType { get; set; }

    public long SizeBytes { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public string? ContentFingerprint { get; set; }

    public string? PackagePath { get; set; }

    public string? ThumbnailFileName { get; set; }

    public string? ThumbnailPackagePath { get; set; }

    public string? ThumbnailContentType { get; set; }

    public string? ThumbnailDataBase64 { get; set; }

    public double? ThumbnailTimestampSeconds { get; set; }
}

public sealed class CoffeeMovieSidecarSubtitle
{
    public string Id { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string SourceFileName { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string? Language { get; set; }

    public string Role { get; set; } = string.Empty;

    public string? GroupKey { get; set; }

    public string Format { get; set; } = string.Empty;

    public int CueCount { get; set; }

    public string? PackagePath { get; set; }

    public string? VttPackagePath { get; set; }

    public string? ContentFingerprint { get; set; }

    public List<CoffeeMovieSidecarCue> Cues { get; set; } = [];

    public List<SubtitleCueLearningState> LearningStates { get; set; } = [];
}

public sealed class CoffeeMovieSidecarCue
{
    public string Id { get; set; } = string.Empty;

    public int Index { get; set; }

    public double StartSeconds { get; set; }

    public double EndSeconds { get; set; }

    public string Text { get; set; } = string.Empty;
}


namespace CoffeeMovie.Storage.Models;

public sealed class CoffeeMovieSidecar
{
    public int SchemaVersion { get; set; } = 1;

    public CoffeeMovieSidecarMovie Movie { get; set; } = new();

    public CoffeeMovieSidecarVideo Video { get; set; } = new();

    public List<CoffeeMovieSidecarSubtitle> Subtitles { get; set; } = [];
}

public sealed class CoffeeMovieSidecarMovie
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public double DurationSeconds { get; set; }

    public List<string> Tags { get; set; } = [];
}

public sealed class CoffeeMovieSidecarVideo
{
    public string FileName { get; set; } = string.Empty;

    public string? SourceKey { get; set; }

    public string? ContentType { get; set; }

    public long SizeBytes { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public string? ContentFingerprint { get; set; }
}

public sealed class CoffeeMovieSidecarSubtitle
{
    public string FileName { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string? Language { get; set; }

    public string Format { get; set; } = string.Empty;

    public int CueCount { get; set; }
}


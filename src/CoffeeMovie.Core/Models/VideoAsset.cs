namespace CoffeeMovie.Core.Models;

public sealed class VideoAsset
{
    public VideoSourceKind SourceKind { get; set; } = VideoSourceKind.LocalFile;

    public string SourceUri { get; set; } = string.Empty;

    public string? SourceKey { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string? ContentType { get; set; }

    public long SizeBytes { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public string? ContentFingerprint { get; set; }

    public string? CachePath { get; set; }

    public bool HasLocalCache => !string.IsNullOrWhiteSpace(CachePath);
}


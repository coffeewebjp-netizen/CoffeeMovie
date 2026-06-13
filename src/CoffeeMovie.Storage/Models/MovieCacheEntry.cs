namespace CoffeeMovie.Storage.Models;

public sealed class MovieCacheEntry
{
    public string SourceKey { get; set; } = string.Empty;

    public string MovieId { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string LocalPath { get; set; } = string.Empty;

    public string? ContentType { get; set; }

    public long SizeBytes { get; set; }

    public DateTimeOffset? SourceModifiedAt { get; set; }

    public string? SourceFingerprint { get; set; }

    public DateTimeOffset CachedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool Matches(long sizeBytes, DateTimeOffset? sourceModifiedAt, string? sourceFingerprint)
    {
        if (SizeBytes != sizeBytes)
        {
            return false;
        }

        if (!Nullable.Equals(SourceModifiedAt, sourceModifiedAt))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(sourceFingerprint)
            && !string.Equals(SourceFingerprint, sourceFingerprint, StringComparison.Ordinal))
        {
            return false;
        }

        return File.Exists(LocalPath);
    }
}


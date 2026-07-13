namespace CoffeeMovie.Storage.Models;

public sealed record CoffeeMoviePackageExportResult(
    string PackagePath,
    long PackageSizeBytes,
    string SidecarPath,
    string MovieId,
    string ShortMovieId,
    string Title)
{
    public bool Skipped { get; init; }

    public bool MetadataOnly { get; init; }

    public string? ContentFingerprint { get; init; }

    public DateTimeOffset? ExportedAt { get; init; }
}

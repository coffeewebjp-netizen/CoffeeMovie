namespace CoffeeMovie.Reader.Models;

public sealed class SyncMovieCandidate
{
    public string ContentUri { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public long? LastModified { get; set; }

    public long? Size { get; set; }

    public string? SidecarContentUri { get; set; }

    public string? SidecarFileName { get; set; }

    public long? SidecarLastModified { get; set; }

    public long? SidecarSize { get; set; }

    public bool HasSidecar => !string.IsNullOrWhiteSpace(SidecarContentUri);

    public string DisplayName => string.IsNullOrWhiteSpace(FileName)
        ? "CoffeeMovie パッケージ"
        : FileName;
}

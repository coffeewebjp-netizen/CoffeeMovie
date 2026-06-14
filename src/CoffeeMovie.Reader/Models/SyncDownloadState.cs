namespace CoffeeMovie.Reader.Models;

public sealed record SyncDownloadState(
    string FileName,
    long PartialBytes,
    long? TotalBytes,
    bool CompletedAvailable)
{
    public bool CanResume => PartialBytes > 0 && !CompletedAvailable;

    public double? Percent => TotalBytes is > 0
        ? Math.Clamp(PartialBytes * 100d / TotalBytes.Value, 0, 100)
        : null;
}

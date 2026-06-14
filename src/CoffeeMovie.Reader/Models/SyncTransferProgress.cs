namespace CoffeeMovie.Reader.Models;

public sealed record SyncTransferProgress(string FileName, long BytesTransferred, long? TotalBytes)
{
    public double? Percent => TotalBytes is > 0
        ? Math.Clamp(BytesTransferred * 100d / TotalBytes.Value, 0, 100)
        : null;
}

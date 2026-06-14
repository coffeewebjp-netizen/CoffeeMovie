namespace CoffeeMovie.Storage.Models;

public sealed record CoffeeMoviePackageExportProgress(
    string Stage,
    long BytesWritten,
    long TotalBytes)
{
    public double Percent => TotalBytes <= 0
        ? 0
        : Math.Clamp(BytesWritten * 100.0 / TotalBytes, 0.0, 100.0);
}

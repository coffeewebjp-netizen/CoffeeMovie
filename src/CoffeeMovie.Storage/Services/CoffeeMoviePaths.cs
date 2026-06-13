namespace CoffeeMovie.Storage.Services;

public sealed class CoffeeMoviePaths
{
    public CoffeeMoviePaths(string? rootPath = null, string? cacheRootPath = null)
    {
        RootPath = rootPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "coffee-movie");

        CacheRootPath = cacheRootPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "coffee-movie-cache");

        LibraryPath = Path.Combine(RootPath, "library.json");
        CacheIndexPath = Path.Combine(RootPath, "cache-index.json");
        VideoCachePath = Path.Combine(RootPath, "videos");
        SubtitlePath = Path.Combine(RootPath, "subtitles");
        ThumbnailCachePath = Path.Combine(RootPath, "thumbnail-cache");
        DriveImportCachePath = Path.Combine(CacheRootPath, "drive-imports");
    }

    public string RootPath { get; }

    public string CacheRootPath { get; }

    public string LibraryPath { get; }

    public string CacheIndexPath { get; }

    public string VideoCachePath { get; }

    public string SubtitlePath { get; }

    public string ThumbnailCachePath { get; }

    public string DriveImportCachePath { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(CacheRootPath);
        Directory.CreateDirectory(VideoCachePath);
        Directory.CreateDirectory(SubtitlePath);
        Directory.CreateDirectory(ThumbnailCachePath);
        Directory.CreateDirectory(DriveImportCachePath);
    }

    public string GetMovieVideoDirectory(string movieId)
    {
        return Path.Combine(VideoCachePath, movieId);
    }

    public string GetMovieSubtitleDirectory(string movieId)
    {
        return Path.Combine(SubtitlePath, movieId);
    }
}


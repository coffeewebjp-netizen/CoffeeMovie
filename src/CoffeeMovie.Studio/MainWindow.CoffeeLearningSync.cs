using System.IO;
using CoffeeMovie.Storage.Models;
using CoffeeMovie.Storage.Services;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
    private readonly CoffeeLearningRegistrationSyncService _coffeeLearningRegistrationSyncService = new();

    private async Task PublishCoffeeLearningRegistrationStateAsync(
        MovieLibrary? library = null,
        string? driveRootPath = null)
    {
        library ??= await _libraryStore.LoadAsync();
        driveRootPath ??= library.Studio.GoogleDriveRootPath;
        if (string.IsNullOrWhiteSpace(driveRootPath) || !Directory.Exists(driveRootPath))
        {
            return;
        }

        var deviceId = "studio-" + Environment.MachineName;
        var fileName = CoffeeLearningRegistrationSyncService.CreateFileName(deviceId);
        await _coffeeLearningRegistrationSyncService.WriteAsync(
            library,
            Path.Combine(driveRootPath, fileName));
    }

    private async Task<CoffeeLearningRegistrationSyncMergeResult> ImportCoffeeLearningRegistrationStateAsync(
        MovieLibrary library,
        string driveRootPath)
    {
        var moviesChanged = 0;
        var tracksChanged = 0;
        var cuesChanged = 0;
        var moviesSkipped = 0;
        foreach (var path in Directory.EnumerateFiles(driveRootPath, "*.json", SearchOption.TopDirectoryOnly)
                     .Where(path => CoffeeLearningRegistrationSyncService.IsSyncFileName(Path.GetFileName(path))))
        {
            try
            {
                var document = await _coffeeLearningRegistrationSyncService.ReadAsync(path);
                var result = _coffeeLearningRegistrationSyncService.Merge(library, document);
                moviesChanged += result.MoviesChanged;
                tracksChanged += result.TracksChanged;
                cuesChanged += result.CuesChanged;
                moviesSkipped += result.MoviesSkipped;
            }
            catch
            {
                // One malformed device snapshot must not block package import.
            }
        }

        return new CoffeeLearningRegistrationSyncMergeResult(
            moviesChanged,
            tracksChanged,
            cuesChanged,
            moviesSkipped);
    }
}

namespace CoffeeMovie.Reader.Pages;

public sealed partial class MovieShelfPage
{
    private async Task<(int FilesMerged, int CuesMerged)> SyncCoffeeLearningRegistrationStateAsync()
    {
        var files = await _googleDriveSyncService.FindCoffeeLearningRegistrationFilesAsync();
        var filesMerged = 0;
        var cuesMerged = 0;
        foreach (var file in files)
        {
            await using var input = await _googleDriveSyncService.DownloadCoffeeLearningRegistrationFileAsync(file);
            var result = await _libraryService.ImportCoffeeLearningRegistrationSyncAsync(input);
            if (result.CuesChanged <= 0)
            {
                continue;
            }

            filesMerged++;
            cuesMerged += result.CuesChanged;
        }

        var snapshot = await _libraryService.ExportCoffeeLearningRegistrationSyncAsync();
        await _googleDriveSyncService.UploadCoffeeLearningRegistrationStateAsync(snapshot);
        return (filesMerged, cuesMerged);
    }
}

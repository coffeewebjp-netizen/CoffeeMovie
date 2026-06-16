using CoffeeMovie.Reader.Models;

namespace CoffeeMovie.Reader.Pages;

public sealed partial class MovieShelfPage
{
    private async Task ManageLearningBackupAsync()
    {
        if (_isSyncing)
        {
            return;
        }

        var choice = await DisplayActionSheetAsync(
            "Learning backup",
            "Cancel",
            null,
            "Export",
            "Import");
        switch (choice)
        {
            case "Export":
                await ExportLearningBackupAsync();
                break;
            case "Import":
                await ImportLearningBackupAsync();
                break;
        }
    }

    private async Task ExportLearningBackupAsync()
    {
        SetBackupBusy(true, "Exporting learning backup...");
        try
        {
            var result = await _libraryService.ExportLearningStateBackupAsync();
            _summaryLabel.Text =
                $"Learning backup exported: {result.MovieCount} movies / {result.LearningStateCount} cue states";

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "CoffeeMovie learning backup",
                File = new ShareFile(result.FilePath)
            });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Learning backup export failed", ex.Message, "Close");
        }
        finally
        {
            SetBackupBusy(false);
        }
    }

    private async Task ImportLearningBackupAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select CoffeeMovie learning backup",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.Android] = ["application/json", "text/json", "text/plain"],
                [DevicePlatform.WinUI] = [".json"]
            })
        });
        if (file is null)
        {
            return;
        }

        SetBackupBusy(true, "Importing learning backup...");
        try
        {
            LearningStateBackupImportResult result = await _libraryService.ImportLearningStateBackupAsync(file);
            await ReloadAsync();
            _summaryLabel.Text =
                $"Learning backup imported: {result.MoviesChanged} movies / {result.TracksChanged} tracks / {result.LearningStateCount} cue states / skipped {result.MoviesSkipped}";
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Learning backup import failed", ex.Message, "Close");
        }
        finally
        {
            SetBackupBusy(false);
        }
    }

    private void SetBackupBusy(bool busy, string? message = null)
    {
        _backupButton.IsEnabled = !busy;
        _syncButton.IsEnabled = !busy;
        _driveSettingsButton.IsEnabled = !busy;
        _importButton.IsEnabled = !busy;
        _moviesView.IsEnabled = !busy;
        _backupButton.Opacity = busy ? 0.55 : 1;
        _syncButton.Opacity = busy ? 0.55 : 1;
        _driveSettingsButton.Opacity = busy ? 0.55 : 1;
        _importButton.Opacity = busy ? 0.55 : 1;
        _moviesView.Opacity = busy ? 0.65 : 1;
        if (!string.IsNullOrWhiteSpace(message))
        {
            _summaryLabel.Text = message;
        }
    }
}

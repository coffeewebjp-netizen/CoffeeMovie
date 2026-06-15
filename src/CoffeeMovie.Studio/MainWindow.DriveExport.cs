using System.IO;
using System.Windows;
using CoffeeMovie.Storage.Models;
using CoffeeMovie.Storage.Services;
using Microsoft.Win32;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
    private async void OnWriteSidecarClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedMovie is null)
        {
            return;
        }

        var defaultFileName = Path.GetFileNameWithoutExtension(_selectedMovie.Video.FileName) + ".coffeemovie.json";
        var dialog = new SaveFileDialog
        {
            Title = "サイドカーを書き出し",
            FileName = defaultFileName,
            Filter = "CoffeeMovie sidecar|*.coffeemovie.json|JSON|*.json|All files|*.*"
        };

        if (!string.IsNullOrWhiteSpace(_selectedMovie.Video.CachePath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_selectedMovie.Video.CachePath);
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await CoffeeMovieSidecarService.WriteAsync(_selectedMovie, dialog.FileName);
            SetStatus($"サイドカーを書き出しました: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            ShowError("サイドカーの書き出しに失敗しました", ex);
        }
    }

    private async void OnExportDrivePackageClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedMovie is null)
        {
            return;
        }

        try
        {
            var driveRootPath = await GetOrChooseDriveRootPathAsync();
            if (string.IsNullOrWhiteSpace(driveRootPath))
            {
                return;
            }

            SetStatus("スマホ用パッケージを書き出しています...");
            ExportDrivePackageButton.IsEnabled = false;
            var progress = new Progress<CoffeeMoviePackageExportProgress>(SetPackageExportProgress);
            var result = await _packageService.ExportReaderPackageAsync(_selectedMovie, driveRootPath, progress);
            if (result.Skipped)
            {
                SetStatus(
                    $"差分なしのため書き出しをスキップしました: {Path.GetFileName(result.PackagePath)}",
                    hideProgress: false);
                return;
            }

            SetStatus(
                $"スマホ用パッケージを書き出しました: {Path.GetFileName(result.PackagePath)} / {Path.GetFileName(result.SidecarPath)}",
                hideProgress: false);
        }
        catch (Exception ex)
        {
            ShowError("スマホ用パッケージの書き出しに失敗しました", ex);
        }
        finally
        {
            ExportDrivePackageButton.IsEnabled = _selectedMovie is not null;
        }
    }

    private async void OnConfigureDriveSyncFolderClicked(object sender, RoutedEventArgs e)
    {
        var path = await GetOrChooseDriveRootPathAsync(forceChoose: true);
        if (!string.IsNullOrWhiteSpace(path))
        {
            SetStatus($"Drive同期フォルダを設定しました: {path}");
        }
    }

    private async Task<string?> GetOrChooseDriveRootPathAsync(bool forceChoose = false)
    {
        var library = await _libraryStore.LoadAsync();
        var driveRootPath = library.Studio.GoogleDriveRootPath;
        if (!forceChoose && !string.IsNullOrWhiteSpace(driveRootPath) && Directory.Exists(driveRootPath))
        {
            return driveRootPath;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Google Drive 同期先フォルダを選択",
            Multiselect = false
        };
        if (!string.IsNullOrWhiteSpace(driveRootPath) && Directory.Exists(driveRootPath))
        {
            dialog.InitialDirectory = driveRootPath;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return null;
        }

        driveRootPath = dialog.FolderName;
        Directory.CreateDirectory(driveRootPath);
        library.Studio.GoogleDriveRootPath = driveRootPath;
        await _libraryStore.SaveAsync(library);
        return driveRootPath;
    }

    private void SetPackageExportProgress(CoffeeMoviePackageExportProgress progress)
    {
        StatusProgressBar.Visibility = Visibility.Visible;
        StatusProgressTextBlock.Visibility = Visibility.Visible;
        StatusProgressBar.Value = progress.Percent;
        StatusProgressTextBlock.Text = $"{progress.Percent:0}%";
        StatusTextBlock.Text = $"{progress.Stage}: {FormatBytes(progress.BytesWritten)} / {FormatBytes(progress.TotalBytes)}";
    }
}

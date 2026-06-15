using System.IO;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Reader.Models;
using CoffeeMovie.Reader.Services;

namespace CoffeeMovie.Reader.Pages;

public sealed partial class MovieShelfPage
{
    private async Task ConfigureGoogleDriveAsync()
    {
        try
        {
            var settings = await _syncSettingsService.LoadSettingsAsync();
            var clientId = await DisplayPromptAsync(
                "Google Drive設定",
                "OAuth Client ID",
                "次へ",
                "キャンセル",
                initialValue: string.IsNullOrWhiteSpace(settings.GoogleDriveClientId)
                    ? DefaultGoogleDriveClientId
                    : settings.GoogleDriveClientId);
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return;
            }

            var folder = await DisplayPromptAsync(
                "Google Drive設定",
                "同期フォルダURLまたはフォルダID",
                "保存",
                "キャンセル",
                initialValue: string.IsNullOrWhiteSpace(settings.GoogleDriveFolderId)
                    ? string.Empty
                    : settings.GoogleDriveFolderId);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            settings = await _googleDriveSyncService.SaveConfigurationAsync(clientId, null, folder);
            var shouldLogin = await DisplayAlertAsync(
                "Google Drive設定",
                "設定を保存しました。Googleログインを開きますか？",
                "ログイン",
                "後で");
            if (!shouldLogin)
            {
                _summaryLabel.Text = "Google Drive設定を保存しました";
                return;
            }

            await AuthorizeGoogleDriveAsync(settings);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Google Drive設定に失敗しました", ex.Message, "閉じる");
        }
    }

    private async Task SyncGoogleDriveAsync()
    {
        if (_isSyncing)
        {
            return;
        }

        SetSyncBusy(true, "Google Drive同期を開始しています...");
        try
        {
            if (!await EnsureGoogleDriveReadyAsync())
            {
                return;
            }

            SetSyncBusy(true, "Google Driveを確認しています...");
            var packages = await _googleDriveSyncService.FindPackagesAsync();
            if (packages.Count == 0)
            {
                _summaryLabel.Text = "同期対象の .coffeemovie がありません";
                return;
            }

            var imported = 0;
            var unchanged = 0;
            var skipped = 0;
            var failed = 0;
            for (var index = 0; index < packages.Count; index++)
            {
                var package = packages[index];
                if (!package.HasSidecar)
                {
                    skipped++;
                    continue;
                }

                string? tempPath = null;
                try
                {
                    _summaryLabel.Text = $"同期中 ({index + 1}/{packages.Count}): {package.DisplayName}";
                    tempPath = await _googleDriveSyncService.DownloadSidecarToCacheAsync(
                        package,
                        CreateTransferProgress("メタ取得中"));
                    if (await _libraryService.ImportDriveSidecarAsync(package, tempPath))
                    {
                        imported++;
                    }
                    else
                    {
                        unchanged++;
                    }
                }
                catch
                {
                    failed++;
                }
                finally
                {
                    DeleteFileQuietly(tempPath);
                }
            }

            await ReloadAsync();
            _summaryLabel.Text = $"Drive同期完了: 追加/更新 {imported} / 変更なし {unchanged} / sidecarなし {skipped} / 失敗 {failed}";
        }
        catch (GoogleDriveReconnectRequiredException ex)
        {
            await DisplayAlertAsync("Google Driveの再接続が必要です", ex.Message, "閉じる");
            await ConfigureGoogleDriveAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Google Drive同期に失敗しました", ex.Message, "閉じる");
        }
        finally
        {
            SetSyncBusy(false);
        }
    }

    private async Task<bool> EnsureGoogleDriveReadyAsync()
    {
        var settings = await _syncSettingsService.LoadSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.GoogleDriveClientId)
            || string.IsNullOrWhiteSpace(settings.GoogleDriveFolderId))
        {
            await ConfigureGoogleDriveAsync();
            return await _googleDriveSyncService.IsConfiguredAsync();
        }

        if (await _googleDriveSyncService.IsConfiguredAsync())
        {
            return true;
        }

        var shouldLogin = await DisplayAlertAsync(
            "Google Drive",
            "Google Driveに接続します。",
            "ログイン",
            "キャンセル");
        if (!shouldLogin)
        {
            return false;
        }

        await AuthorizeGoogleDriveAsync(settings);
        return await _googleDriveSyncService.IsConfiguredAsync();
    }

    private async Task<bool?> ChooseDownloadModeAsync(Movie movie, SyncMovieCandidate package)
    {
        var state = _googleDriveSyncService.GetPackageDownloadState(package);
        if (state.CompletedAvailable)
        {
            return false;
        }

        if (state.CanResume)
        {
            var resumeLabel = state.Percent is null
                ? $"続きから取得 ({FormatBytes(state.PartialBytes)} 取得済み)"
                : $"続きから取得 ({state.Percent.Value:0}% / {FormatBytes(state.PartialBytes)} 取得済み)";
            var choice = await DisplayActionSheetAsync(
                $"「{movie.Title}」をGoogle Driveから取得します",
                "キャンセル",
                "最初から取得",
                resumeLabel);
            if (choice == resumeLabel)
            {
                return false;
            }

            return choice == "最初から取得" ? true : null;
        }

        var shouldDownload = await DisplayAlertAsync(
            "動画キャッシュを取得",
            $"「{movie.Title}」をGoogle Driveから取得して開きますか？{FormatDownloadEstimate(movie.SourcePackageSize)}",
            "取得",
            "キャンセル");
        return shouldDownload ? false : null;
    }

    private async Task AuthorizeGoogleDriveAsync(ReaderSyncSettings settings)
    {
        await _googleDriveSyncService.AuthorizeWithBrowserAsync(
            settings,
            new Progress<string>(message => _summaryLabel.Text = message));
        _summaryLabel.Text = "Google Driveに接続しました";
    }

    private async Task<Movie> DownloadMovieCacheAsync(Movie movie, bool restartDownload)
    {
        var package = CreatePackageCandidate(movie);

        string? tempPath = null;
        try
        {
            SetSyncBusy(true, restartDownload
                ? $"動画を最初から取得中: {movie.Title}"
                : $"動画を取得中: {movie.Title}");
            tempPath = await _googleDriveSyncService.DownloadPackageToCacheAsync(
                package,
                CreateTransferProgress(restartDownload ? "動画再取得中" : "動画取得中"),
                restartDownload);
            return await _libraryService.ImportCoffeeMoviePackageFileAsync(tempPath, package);
        }
        finally
        {
            DeleteFileQuietly(tempPath);
            SetSyncBusy(false);
        }
    }

    private static SyncMovieCandidate CreatePackageCandidate(Movie movie)
    {
        return new SyncMovieCandidate
        {
            ContentUri = movie.SourcePackageUri ?? string.Empty,
            FileName = string.IsNullOrWhiteSpace(movie.SourcePackageName)
                ? $"{movie.Title}.coffeemovie"
                : movie.SourcePackageName,
            LastModified = movie.SourcePackageLastModified,
            Size = movie.SourcePackageSize
        };
    }

    private IProgress<SyncTransferProgress> CreateTransferProgress(string label)
    {
        return new Progress<SyncTransferProgress>(progress =>
        {
            var percent = progress.Percent is null ? string.Empty : $" {progress.Percent.Value:0}%";
            var total = progress.TotalBytes is > 0 ? $" / {FormatBytes(progress.TotalBytes.Value)}" : string.Empty;
            _summaryLabel.Text = $"{label}: {progress.FileName}{percent} ({FormatBytes(progress.BytesTransferred)}{total})";
        });
    }

    private void SetSyncBusy(bool busy, string? message = null)
    {
        _isSyncing = busy;
        _syncButton.IsEnabled = !busy;
        _driveSettingsButton.IsEnabled = !busy;
        _importButton.IsEnabled = !busy;
        _moviesView.IsEnabled = !busy;
        _syncButton.Opacity = busy ? 0.55 : 1;
        _driveSettingsButton.Opacity = busy ? 0.55 : 1;
        _importButton.Opacity = busy ? 0.55 : 1;
        _moviesView.Opacity = busy ? 0.65 : 1;
        if (!string.IsNullOrWhiteSpace(message))
        {
            _summaryLabel.Text = message;
        }
    }

    private static void DeleteFileQuietly(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary Drive downloads are best-effort cleanup.
        }
    }
}

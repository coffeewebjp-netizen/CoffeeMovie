using System.IO;
using System.Windows;
using CoffeeMovie.Core.Models;
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

    private async void OnImportDrivePackagesClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var driveRootPath = await GetOrChooseDriveRootPathAsync();
            if (string.IsNullOrWhiteSpace(driveRootPath))
            {
                return;
            }

            var packagePaths = Directory
                .EnumerateFiles(driveRootPath, "*.coffeemovie", SearchOption.TopDirectoryOnly)
                .Where(path => CoffeeMoviePackageService.IsReaderPackageFileName(Path.GetFileName(path)))
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (packagePaths.Count == 0)
            {
                SetStatus("Drive同期フォルダに CoffeeMovie パッケージがありません。");
                return;
            }

            ImportDrivePackagesButton.IsEnabled = false;
            var library = await _libraryStore.LoadAsync();
            var imported = 0;
            var updated = 0;
            var unchanged = 0;
            var failed = 0;
            string? selectedMovieId = null;

            for (var index = 0; index < packagePaths.Count; index++)
            {
                var packagePath = packagePaths[index];
                var packageName = Path.GetFileName(packagePath);
                var packageInfo = new FileInfo(packagePath);
                var sidecarInfo = TryGetDriveSidecarInfo(packagePath);
                SetStatus($"Driveから取り込み中: {index + 1} / {packagePaths.Count} ({packageName})");

                try
                {
                    var metadataMatchedMovie = FindExistingDriveFileMovie(library, packageInfo, sidecarInfo);
                    if (metadataMatchedMovie is not null
                        && HasCurrentDriveFileMetadata(metadataMatchedMovie, packageInfo, sidecarInfo))
                    {
                        unchanged++;
                        continue;
                    }

                    var manifest = await ReadDrivePackageMetadataAsync(packagePath);
                    var existing = FindExistingPackageMovie(library, manifest) ?? metadataMatchedMovie;
                    if (!ShouldImportDrivePackage(manifest, existing))
                    {
                        unchanged++;
                        continue;
                    }

                    var movie = await _packageService.ImportReaderPackageAsync(
                        packagePath,
                        _paths,
                        existing,
                        packagePath,
                        packageName,
                        new DateTimeOffset(packageInfo.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
                        packageInfo.Length,
                        maxSceneMarkers: 1000);
                    ApplyDriveFileSourceMetadata(movie, packageInfo, sidecarInfo);

                    AddOrUpdateImportedMovie(library, movie);
                    if (existing is null)
                    {
                        imported++;
                    }
                    else
                    {
                        updated++;
                    }

                    selectedMovieId = movie.Id;
                }
                catch
                {
                    failed++;
                }
            }

            MergeTagDefinitionsFromLibrary(library);
            await _libraryStore.SaveAsync(library);
            await RefreshMoviesAsync(selectedMovieId);
            SetStatus($"Drive取り込み完了: 追加 {imported} / 更新 {updated} / 変更なし {unchanged} / 失敗 {failed}");
        }
        catch (Exception ex)
        {
            ShowError("Driveからの取り込みに失敗しました", ex);
        }
        finally
        {
            ImportDrivePackagesButton.IsEnabled = true;
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

    private async Task<CoffeeMovieSidecar> ReadDrivePackageMetadataAsync(string packagePath)
    {
        var sidecarPath = CoffeeMoviePackageService.GetReaderPackageSidecarPath(packagePath);
        return File.Exists(sidecarPath)
            ? await _packageService.ReadReaderPackageSidecarAsync(sidecarPath)
            : await _packageService.ReadReaderPackageManifestAsync(packagePath);
    }

    private static FileInfo? TryGetDriveSidecarInfo(string packagePath)
    {
        var sidecarPath = CoffeeMoviePackageService.GetReaderPackageSidecarPath(packagePath);
        return File.Exists(sidecarPath) ? new FileInfo(sidecarPath) : null;
    }

    private static Movie? FindExistingPackageMovie(MovieLibrary library, CoffeeMovieSidecar manifest)
    {
        var movieId = string.IsNullOrWhiteSpace(manifest.SourceMovieId)
            ? manifest.Movie.Id
            : manifest.SourceMovieId;
        if (string.IsNullOrWhiteSpace(movieId))
        {
            return null;
        }

        return library.Movies.FirstOrDefault(movie =>
            string.Equals(movie.Id, movieId, StringComparison.Ordinal));
    }

    private static Movie? FindExistingDriveFileMovie(
        MovieLibrary library,
        FileInfo packageInfo,
        FileInfo? sidecarInfo)
    {
        return library.Movies.FirstOrDefault(movie =>
                string.Equals(movie.SourcePackageUri, packageInfo.FullName, StringComparison.OrdinalIgnoreCase))
            ?? library.Movies.FirstOrDefault(movie =>
                sidecarInfo is not null
                && string.Equals(movie.SourceSidecarUri, sidecarInfo.FullName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasCurrentDriveFileMetadata(
        Movie movie,
        FileInfo packageInfo,
        FileInfo? sidecarInfo)
    {
        if (string.IsNullOrWhiteSpace(movie.SourceContentFingerprint))
        {
            return false;
        }

        if (sidecarInfo is not null)
        {
            var sidecarLastModified = ToUnixMilliseconds(sidecarInfo.LastWriteTimeUtc);
            if (movie.SourceSidecarLastModified == sidecarLastModified
                && movie.SourceSidecarSize == sidecarInfo.Length)
            {
                return true;
            }
        }

        var packageLastModified = ToUnixMilliseconds(packageInfo.LastWriteTimeUtc);
        return movie.SourcePackageLastModified == packageLastModified
            && movie.SourcePackageSize == packageInfo.Length;
    }

    private static void ApplyDriveFileSourceMetadata(
        Movie movie,
        FileInfo packageInfo,
        FileInfo? sidecarInfo)
    {
        movie.SourcePackageUri = packageInfo.FullName;
        movie.SourcePackageName = packageInfo.Name;
        movie.SourcePackageLastModified = ToUnixMilliseconds(packageInfo.LastWriteTimeUtc);
        movie.SourcePackageSize = packageInfo.Length;
        movie.SourceSidecarUri = sidecarInfo?.FullName;
        movie.SourceSidecarName = sidecarInfo?.Name;
        movie.SourceSidecarLastModified = sidecarInfo is null ? null : ToUnixMilliseconds(sidecarInfo.LastWriteTimeUtc);
        movie.SourceSidecarSize = sidecarInfo?.Length;
    }

    private static bool ShouldImportDrivePackage(CoffeeMovieSidecar manifest, Movie? existing)
    {
        if (existing is null)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(manifest.ContentFingerprint))
        {
            return !string.Equals(
                existing.SourceContentFingerprint,
                manifest.ContentFingerprint,
                StringComparison.Ordinal);
        }

        var existingSourceUpdatedAt = existing.SourceMovieUpdatedAt ?? existing.UpdatedAt;
        if (manifest.Movie.UpdatedAt != default && manifest.Movie.UpdatedAt > existingSourceUpdatedAt)
        {
            return true;
        }

        if (!string.Equals(existing.Title, manifest.Movie.Title, StringComparison.Ordinal)
            || !string.Equals(existing.SeriesTitle, manifest.Movie.SeriesTitle, StringComparison.Ordinal)
            || existing.SeasonNumber != manifest.Movie.SeasonNumber
            || existing.EpisodeNumber != manifest.Movie.EpisodeNumber
            || existing.Video.SizeBytes != manifest.Video.SizeBytes
            || existing.SubtitleTracks.Count != manifest.Subtitles.Count)
        {
            return true;
        }

        return !existing.Tags
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(
                (manifest.Movie.Tags ?? []).OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
    }

    private static void AddOrUpdateImportedMovie(MovieLibrary library, Movie movie)
    {
        library.Movies.RemoveAll(existing =>
            string.Equals(existing.Id, movie.Id, StringComparison.Ordinal));
        library.Movies.Add(movie);
        library.Movies.Sort((left, right) => right.UpdatedAt.CompareTo(left.UpdatedAt));
    }

    private static long ToUnixMilliseconds(DateTime value)
    {
        return new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeMilliseconds();
    }
}

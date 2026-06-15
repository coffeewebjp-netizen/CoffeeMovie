using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using CoffeeMovie.Reader.Models;
using Microsoft.Maui.Storage;

namespace CoffeeMovie.Reader.Services;

public sealed class DrivePackageDownloadService
{
    private const string DriveFilesUrl = "https://www.googleapis.com/drive/v3/files";
    private const int DriveDownloadMaxAttempts = 3;

    private readonly ReaderSyncSettingsService _settingsService;
    private readonly HttpClient _httpClient;
    private readonly Func<ReaderSyncSettings, CancellationToken, Task<string>> _getValidAccessTokenAsync;
    private readonly Action _clearCachedAccessToken;
    private readonly Func<string, string> _getErrorMessage;

    public DrivePackageDownloadService(
        ReaderSyncSettingsService settingsService,
        HttpClient httpClient,
        Func<ReaderSyncSettings, CancellationToken, Task<string>> getValidAccessTokenAsync,
        Action clearCachedAccessToken,
        Func<string, string> getErrorMessage)
    {
        _settingsService = settingsService;
        _httpClient = httpClient;
        _getValidAccessTokenAsync = getValidAccessTokenAsync;
        _clearCachedAccessToken = clearCachedAccessToken;
        _getErrorMessage = getErrorMessage;
    }

    public async Task<string> DownloadSidecarToCacheAsync(
        SyncMovieCandidate package,
        IProgress<SyncTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(package.SidecarContentUri))
        {
            throw new InvalidOperationException("CoffeeMovie サイドカーが見つかりません。");
        }

        var sidecar = new SyncMovieCandidate
        {
            ContentUri = package.SidecarContentUri,
            FileName = string.IsNullOrWhiteSpace(package.SidecarFileName)
                ? package.FileName + ".json"
                : package.SidecarFileName,
            Size = package.SidecarSize
        };

        return await DownloadDriveFileToCacheAsync(sidecar, ".coffeemovie.json", progress, restartDownload: false, cancellationToken);
    }

    public async Task<string> DownloadPackageToCacheAsync(
        SyncMovieCandidate package,
        IProgress<SyncTransferProgress>? progress = null,
        bool restartDownload = false,
        CancellationToken cancellationToken = default)
    {
        return await DownloadDriveFileToCacheAsync(package, ".coffeemovie", progress, restartDownload, cancellationToken);
    }

    public SyncDownloadState GetPackageDownloadState(SyncMovieCandidate package)
    {
        var paths = GetDownloadPaths(package, ".coffeemovie");
        MigrateLegacyDownloadFiles(paths);
        PrepareResumableDownloadPaths(package, paths.PartialPath, paths.CompletedPath);
        var completedAvailable = IsCompletedDownloadReusable(package, paths.CompletedPath);
        var partialBytes = completedAvailable
            ? 0
            : GetPartialDownloadLength(package, paths.PartialPath);
        return new SyncDownloadState(package.FileName, partialBytes, package.Size, completedAvailable);
    }

    private async Task<string> DownloadDriveFileToCacheAsync(
        SyncMovieCandidate package,
        string fallbackExtension,
        IProgress<SyncTransferProgress>? progress,
        bool restartDownload,
        CancellationToken cancellationToken)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);
        var fileId = RequireFileId(package);
        var paths = GetDownloadPaths(package, fallbackExtension);
        MigrateLegacyDownloadFiles(paths);
        if (restartDownload)
        {
            DeleteFileQuietly(paths.PartialPath);
            DeleteFileQuietly(paths.CompletedPath);
        }

        var partialPath = paths.PartialPath;
        var completedPath = paths.CompletedPath;
        PrepareResumableDownloadPaths(package, partialPath, completedPath);
        if (IsCompletedDownloadReusable(package, completedPath))
        {
            progress?.Report(new SyncTransferProgress(package.FileName, new FileInfo(completedPath).Length, package.Size));
            return completedPath;
        }

        Exception? lastException = null;
        for (var attempt = 1; attempt <= DriveDownloadMaxAttempts; attempt++)
        {
            try
            {
                var accessToken = await _getValidAccessTokenAsync(settings, cancellationToken);
                var existingBytes = GetPartialDownloadLength(package, partialPath);
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{DriveFilesUrl}/{Uri.EscapeDataString(fileId)}?alt=media&supportsAllDrives=true");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                if (existingBytes > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(existingBytes, null);
                }

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    if (IsPartialDownloadComplete(package, partialPath))
                    {
                        File.Move(partialPath, completedPath, overwrite: true);
                        progress?.Report(new SyncTransferProgress(package.FileName, package.Size ?? new FileInfo(completedPath).Length, package.Size));
                        return completedPath;
                    }

                    DeleteFileQuietly(partialPath);
                    lastException = new IOException("Google Driveの再開位置が一致しませんでした。最初から取り直します。");
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    var message = _getErrorMessage(body);
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        _clearCachedAccessToken();
                    }

                    lastException = new InvalidOperationException(
                        $"Google Driveからのダウンロードに失敗しました: HTTP {(int)response.StatusCode} / {message}");
                    if (!IsRetryableStatusCode(response.StatusCode) || attempt == DriveDownloadMaxAttempts)
                    {
                        throw lastException;
                    }
                }
                else
                {
                    var append = existingBytes > 0
                        && response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                    if (existingBytes > 0 && !append)
                    {
                        // Some servers ignore Range. Restart cleanly rather than appending duplicate bytes.
                        DeleteFileQuietly(partialPath);
                        existingBytes = 0;
                    }

                    await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using var output = new FileStream(
                        partialPath,
                        append ? FileMode.Append : FileMode.Create,
                        FileAccess.Write,
                        FileShare.None);
                    await CopyWithProgressAsync(input, output, package, progress, existingBytes, cancellationToken);
                    if (!IsPartialDownloadComplete(package, partialPath))
                    {
                        lastException = new IOException("Google Driveからのダウンロードが途中で終了しました。次回は途中から再開します。");
                        if (attempt == DriveDownloadMaxAttempts)
                        {
                            throw lastException;
                        }

                        continue;
                    }

                    File.Move(partialPath, completedPath, overwrite: true);
                    return completedPath;
                }
            }
            catch (Exception ex) when (IsRetryableDownloadException(ex, cancellationToken) && attempt < DriveDownloadMaxAttempts)
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
                continue;
            }

            await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
        }

        throw new InvalidOperationException(
            $"Google Driveからのダウンロードに失敗しました: {DriveDownloadMaxAttempts}回試行しました。{lastException?.Message}",
            lastException);
    }

    private static bool IsRetryableStatusCode(System.Net.HttpStatusCode statusCode)
    {
        return statusCode is System.Net.HttpStatusCode.Unauthorized
            or System.Net.HttpStatusCode.RequestTimeout
            or System.Net.HttpStatusCode.TooManyRequests
            or System.Net.HttpStatusCode.InternalServerError
            or System.Net.HttpStatusCode.BadGateway
            or System.Net.HttpStatusCode.ServiceUnavailable
            or System.Net.HttpStatusCode.GatewayTimeout;
    }

    private static bool IsRetryableDownloadException(Exception ex, CancellationToken cancellationToken)
    {
        return !cancellationToken.IsCancellationRequested
            && ex is HttpRequestException or IOException or TaskCanceledException;
    }

    private static string GetResumableDownloadBasePath(
        string importsPath,
        SyncMovieCandidate package,
        string fileId,
        string extension)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fileId)))[..16].ToLowerInvariant();
        var safeName = CreateSafeFileStem(string.IsNullOrWhiteSpace(package.FileName)
            ? "drive-file"
            : Path.GetFileNameWithoutExtension(package.FileName));
        return Path.Combine(importsPath, $"{safeName}-{hash}{extension.ToLowerInvariant()}");
    }

    private static DownloadPaths GetDownloadPaths(SyncMovieCandidate package, string fallbackExtension)
    {
        var fileId = RequireFileId(package);
        var extension = Path.GetExtension(package.FileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = fallbackExtension;
        }

        var importsPath = Path.Combine(FileSystem.AppDataDirectory, "drive-imports");
        Directory.CreateDirectory(importsPath);
        var completedPath = GetResumableDownloadBasePath(importsPath, package, fileId, extension);

        var legacyImportsPath = Path.Combine(FileSystem.CacheDirectory, "drive-imports");
        var legacyCompletedPath = GetResumableDownloadBasePath(legacyImportsPath, package, fileId, extension);
        return new DownloadPaths(
            completedPath,
            completedPath + ".partial",
            legacyCompletedPath,
            legacyCompletedPath + ".partial");
    }

    private static string RequireFileId(SyncMovieCandidate package)
    {
        var fileId = ExtractFileId(package.ContentUri);
        if (string.IsNullOrWhiteSpace(fileId))
        {
            throw new InvalidOperationException("Google DriveのファイルIDを取得できませんでした。");
        }

        return fileId;
    }

    private static void MigrateLegacyDownloadFiles(DownloadPaths paths)
    {
        MoveLegacyFileIfMissing(paths.LegacyPartialPath, paths.PartialPath);
        MoveLegacyFileIfMissing(paths.LegacyCompletedPath, paths.CompletedPath);
    }

    private static void MoveLegacyFileIfMissing(string legacyPath, string targetPath)
    {
        if (!File.Exists(legacyPath) || File.Exists(targetPath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? FileSystem.AppDataDirectory);
            File.Move(legacyPath, targetPath);
        }
        catch
        {
            // Legacy cache migration is best-effort; the download can still restart cleanly.
        }
    }

    private static string CreateSafeFileStem(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
        }

        var safe = builder.ToString().Trim();
        if (safe.Length > 80)
        {
            safe = safe[..80].Trim();
        }

        return string.IsNullOrWhiteSpace(safe) ? "drive-file" : safe;
    }

    private static void PrepareResumableDownloadPaths(
        SyncMovieCandidate package,
        string partialPath,
        string completedPath)
    {
        if (File.Exists(completedPath))
        {
            if (package.Size is > 0 && new FileInfo(completedPath).Length != package.Size.Value)
            {
                DeleteFileQuietly(completedPath);
            }
        }

        if (File.Exists(partialPath)
            && package.Size is > 0
            && new FileInfo(partialPath).Length > package.Size.Value)
        {
            DeleteFileQuietly(partialPath);
        }
    }

    private static long GetPartialDownloadLength(SyncMovieCandidate package, string partialPath)
    {
        if (!File.Exists(partialPath))
        {
            return 0;
        }

        var length = new FileInfo(partialPath).Length;
        if (package.Size is > 0 && length > package.Size.Value)
        {
            DeleteFileQuietly(partialPath);
            return 0;
        }

        return length;
    }

    private static bool IsPartialDownloadComplete(SyncMovieCandidate package, string partialPath)
    {
        if (!File.Exists(partialPath))
        {
            return false;
        }

        var length = new FileInfo(partialPath).Length;
        return package.Size is > 0
            ? length >= package.Size.Value
            : length > 0;
    }

    private static bool IsCompletedDownloadReusable(SyncMovieCandidate package, string completedPath)
    {
        if (!File.Exists(completedPath))
        {
            return false;
        }

        return package.Size is not > 0 || new FileInfo(completedPath).Length == package.Size.Value;
    }

    private static void DeleteFileQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary download files are best-effort cleanup.
        }
    }

    private static async Task CopyWithProgressAsync(
        Stream input,
        Stream output,
        SyncMovieCandidate package,
        IProgress<SyncTransferProgress>? progress,
        long initialBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 128];
        long totalRead = initialBytes;
        long lastReported = initialBytes;
        progress?.Report(new SyncTransferProgress(package.FileName, totalRead, package.Size));

        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            totalRead += read;
            if (totalRead - lastReported >= 1024 * 1024)
            {
                lastReported = totalRead;
                progress?.Report(new SyncTransferProgress(package.FileName, totalRead, package.Size));
            }
        }

        progress?.Report(new SyncTransferProgress(package.FileName, totalRead, package.Size));
    }

    private static string ExtractFileId(string contentUri)
    {
        const string prefix = "gdrive://files/";
        return contentUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? contentUri[prefix.Length..]
            : string.Empty;
    }

    private sealed record DownloadPaths(
        string CompletedPath,
        string PartialPath,
        string LegacyCompletedPath,
        string LegacyPartialPath);
}

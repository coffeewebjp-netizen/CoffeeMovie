using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoffeeMovie.Reader.Models;
using CoffeeMovie.Storage.Services;

namespace CoffeeMovie.Reader.Services;

public sealed class GoogleDriveSyncService
{
    private const string DriveReadonlyScope = "https://www.googleapis.com/auth/drive.readonly";
    private const string BrowserRedirectUri = "net.coffeewebjp.coffeemovie.reader:/oauth2redirect";
    private const string AuthorizationUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";
    private const string DriveFilesUrl = "https://www.googleapis.com/drive/v3/files";
    private const string RefreshTokenKey = "coffee-movie-google-drive-refresh-token";
    private const int DriveDownloadMaxAttempts = 3;

    private readonly ReaderSyncSettingsService _settingsService;
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(30)
    };
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public GoogleDriveSyncService(ReaderSyncSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);
        return !string.IsNullOrWhiteSpace(settings.GoogleDriveClientId)
            && !string.IsNullOrWhiteSpace(settings.GoogleDriveFolderId)
            && !string.IsNullOrWhiteSpace(await GetRefreshTokenAsync());
    }

    public async Task<ReaderSyncSettings> SaveConfigurationAsync(
        string clientId,
        string? clientSecret,
        string folderInput,
        CancellationToken cancellationToken = default)
    {
        var folderId = ExtractFolderId(folderInput);
        var normalizedClientId = clientId.Trim();
        var normalizedClientSecret = string.IsNullOrWhiteSpace(clientSecret) ? null : clientSecret.Trim();

        if (string.IsNullOrWhiteSpace(normalizedClientId))
        {
            throw new InvalidOperationException("Google OAuth Client ID を入力してください。");
        }

        if (normalizedClientId.StartsWith("GOCSPX-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Client ID欄にClient Secretが入力されています。Client IDは通常「.apps.googleusercontent.com」で終わる値です。");
        }

        if (!normalizedClientId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Client IDの形式が違うようです。Google Cloudの「クライアントID」を入力してください。");
        }

        if (!string.IsNullOrWhiteSpace(normalizedClientSecret)
            && normalizedClientSecret.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Client Secret欄にClient IDが入力されています。Client IDとClient Secretを入れ替えてください。");
        }

        if (string.IsNullOrWhiteSpace(folderId))
        {
            throw new InvalidOperationException("Google Drive フォルダURLまたはフォルダIDを入力してください。");
        }

        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);
        settings.GoogleDriveClientId = normalizedClientId;
        settings.GoogleDriveClientSecret = normalizedClientSecret;
        settings.GoogleDriveFolderId = folderId;
        settings.GoogleDriveFolderName = "Google Drive";
        await _settingsService.SaveSettingsAsync(settings, cancellationToken);
        return settings;
    }

    public async Task AuthorizeWithBrowserAsync(
        ReaderSyncSettings settings,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.GoogleDriveClientId))
        {
            throw new InvalidOperationException("Google OAuth Client ID が未設定です。");
        }

        var codeVerifier = CreateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var authUri = CreateAuthorizationUri(settings.GoogleDriveClientId, codeChallenge);

        progress?.Report("Googleログインを開いています...");
        var result = await WebAuthenticator.Default.AuthenticateAsync(authUri, new Uri(BrowserRedirectUri));
        if (!result.Properties.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            var error = result.Properties.TryGetValue("error", out var errorValue) ? errorValue : "authorization_failed";
            throw new InvalidOperationException($"Google認証に失敗しました: {error}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("Google認証トークンを取得しています...");

        var form = new Dictionary<string, string>
        {
            ["client_id"] = settings.GoogleDriveClientId,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = BrowserRedirectUri
        };
        if (!string.IsNullOrWhiteSpace(settings.GoogleDriveClientSecret))
        {
            form["client_secret"] = settings.GoogleDriveClientSecret;
        }

        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(TokenUrl, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Google認証に失敗しました: {GetErrorMessage(body)}");
        }

        await SaveTokenResponseAsync(settings, body, cancellationToken);
    }

    public async Task<IReadOnlyList<SyncMovieCandidate>> FindPackagesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.GoogleDriveFolderId))
        {
            return [];
        }

        var accessToken = await GetValidAccessTokenAsync(settings, cancellationToken);
        var driveFiles = new List<DriveFileInfo>();
        string? pageToken = null;

        do
        {
            var query = $"'{settings.GoogleDriveFolderId}' in parents and trashed = false and mimeType != 'application/vnd.google-apps.folder'";
            var url = $"{DriveFilesUrl}?pageSize=1000&supportsAllDrives=true&includeItemsFromAllDrives=true&orderBy=name_natural&fields=nextPageToken,files(id,name,size,modifiedTime,mimeType)&q={Uri.EscapeDataString(query)}";
            if (!string.IsNullOrWhiteSpace(pageToken))
            {
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Google Drive一覧の取得に失敗しました: {GetErrorMessage(body)}");
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            pageToken = root.TryGetProperty("nextPageToken", out var tokenElement)
                ? tokenElement.GetString()
                : null;

            if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var file in files.EnumerateArray())
            {
                var name = GetString(file, "name");
                if (!CoffeeMoviePackageService.IsReaderPackageFileName(name)
                    && !CoffeeMoviePackageService.IsReaderPackageSidecarFileName(name))
                {
                    continue;
                }

                var id = GetString(file, "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                driveFiles.Add(new DriveFileInfo(
                    $"gdrive://files/{id}",
                    name,
                    GetModifiedTimeMilliseconds(file),
                    GetInt64(file, "size")));
            }
        }
        while (!string.IsNullOrWhiteSpace(pageToken));

        var sidecars = driveFiles
            .Where(file => CoffeeMoviePackageService.IsReaderPackageSidecarFileName(file.FileName))
            .GroupBy(file => CoffeeMoviePackageService.GetPackageFileNameForSidecarName(file.FileName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var packages = new List<SyncMovieCandidate>();
        foreach (var file in driveFiles.Where(file => CoffeeMoviePackageService.IsReaderPackageFileName(file.FileName)))
        {
            sidecars.TryGetValue(file.FileName, out var sidecar);
            packages.Add(new SyncMovieCandidate
            {
                ContentUri = file.ContentUri,
                FileName = file.FileName,
                LastModified = file.LastModified,
                Size = file.Size,
                SidecarContentUri = sidecar?.ContentUri,
                SidecarFileName = sidecar?.FileName,
                SidecarLastModified = sidecar?.LastModified,
                SidecarSize = sidecar?.Size
            });
        }

        return packages
            .OrderBy(package => package.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
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

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);
        await ClearStoredGoogleDriveTokenAsync(settings, cancellationToken);
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
                var accessToken = await GetValidAccessTokenAsync(settings, cancellationToken);
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
                    var message = GetErrorMessage(body);
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        _accessToken = null;
                        _accessTokenExpiresAt = default;
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

    private async Task ClearStoredGoogleDriveTokenAsync(
        ReaderSyncSettings settings,
        CancellationToken cancellationToken)
    {
        _accessToken = null;
        _accessTokenExpiresAt = default;
        await SecureStorage.Default.SetAsync(RefreshTokenKey, string.Empty);
        settings.GoogleDriveConnectedAt = null;
        await _settingsService.SaveSettingsAsync(settings, cancellationToken);
    }

    private async Task<string> GetValidAccessTokenAsync(
        ReaderSyncSettings settings,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken)
            && DateTimeOffset.UtcNow < _accessTokenExpiresAt.AddSeconds(-60))
        {
            return _accessToken;
        }

        if (string.IsNullOrWhiteSpace(settings.GoogleDriveClientId))
        {
            throw new InvalidOperationException("Google OAuth Client ID が未設定です。");
        }

        var refreshToken = await GetRefreshTokenAsync();
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new InvalidOperationException("Google Driveに接続してください。");
        }

        var form = new Dictionary<string, string>
        {
            ["client_id"] = settings.GoogleDriveClientId,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        };
        if (!string.IsNullOrWhiteSpace(settings.GoogleDriveClientSecret))
        {
            form["client_secret"] = settings.GoogleDriveClientSecret;
        }

        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(TokenUrl, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (string.Equals(GetErrorCode(body), "invalid_grant", StringComparison.OrdinalIgnoreCase))
            {
                await ClearStoredGoogleDriveTokenAsync(settings, cancellationToken);
                throw new GoogleDriveReconnectRequiredException("Google Driveの認証期限が切れたか、Google側で取り消されています。もう一度Google Driveに接続してください。");
            }

            throw new InvalidOperationException($"Google Driveの再接続に失敗しました: {GetErrorMessage(body)}");
        }

        await SaveTokenResponseAsync(settings, body, cancellationToken, keepExistingRefreshToken: true);
        return _accessToken ?? throw new InvalidOperationException("Google Driveのアクセストークンを取得できませんでした。");
    }

    private async Task SaveTokenResponseAsync(
        ReaderSyncSettings settings,
        string body,
        CancellationToken cancellationToken,
        bool keepExistingRefreshToken = false)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        _accessToken = GetString(root, "access_token");
        var expiresIn = GetInt32(root, "expires_in", 3600);
        _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

        var refreshToken = GetString(root, "refresh_token");
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await SecureStorage.Default.SetAsync(RefreshTokenKey, refreshToken);
        }
        else if (!keepExistingRefreshToken)
        {
            throw new InvalidOperationException("Google Driveの更新トークンを取得できませんでした。");
        }

        settings.GoogleDriveConnectedAt = DateTimeOffset.UtcNow;
        await _settingsService.SaveSettingsAsync(settings, cancellationToken);
    }

    private static async Task<string?> GetRefreshTokenAsync()
    {
        try
        {
            var token = await SecureStorage.Default.GetAsync(RefreshTokenKey);
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch
        {
            return null;
        }
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

    private static string ExtractFolderId(string input)
    {
        var value = input.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        const string marker = "/folders/";
        var markerIndex = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            value = value[(markerIndex + marker.Length)..];
        }
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            value = ExtractQueryValue(uri.Query, "id");
        }

        var cutIndex = value.IndexOfAny(['?', '/', '&', '#']);
        if (cutIndex >= 0)
        {
            value = value[..cutIndex];
        }

        return value.Trim();
    }

    private static string ExtractQueryValue(string query, string key)
    {
        var normalized = query.TrimStart('?');
        foreach (var part in normalized.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            if (pieces.Length == 2 && string.Equals(Uri.UnescapeDataString(pieces[0]), key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pieces[1]);
            }
        }

        return string.Empty;
    }

    private static string ExtractFileId(string contentUri)
    {
        const string prefix = "gdrive://files/";
        return contentUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? contentUri[prefix.Length..]
            : string.Empty;
    }

    private static Uri CreateAuthorizationUri(string clientId, string codeChallenge)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = BrowserRedirectUri,
            ["response_type"] = "code",
            ["scope"] = DriveReadonlyScope,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };

        var query = string.Join("&", parameters.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new Uri($"{AuthorizationUrl}?{query}");
    }

    private static string CreateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Base64UrlEncode(bytes);
    }

    private static string CreateCodeChallenge(string codeVerifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static long? GetModifiedTimeMilliseconds(JsonElement element)
    {
        var value = GetString(element, "modifiedTime");
        return DateTimeOffset.TryParse(value, out var modified)
            ? modified.ToUnixTimeMilliseconds()
            : null;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int GetInt32(JsonElement element, string propertyName, int fallback)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return fallback;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return int.TryParse(value.GetString(), out var parsed) ? parsed : fallback;
    }

    private static long? GetInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return long.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    private static string GetErrorCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return GetString(document.RootElement, "error");
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var description = GetString(root, "error_description");
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description;
            }

            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString() ?? body;
                }

                if (error.ValueKind == JsonValueKind.Object)
                {
                    var message = GetString(error, "message");
                    return string.IsNullOrWhiteSpace(message) ? body : message;
                }
            }
        }
        catch
        {
            // The response may be plain text or HTML.
        }

        return body.Length > 300 ? body[..300] : body;
    }

    private sealed record DriveFileInfo(
        string ContentUri,
        string FileName,
        long? LastModified,
        long? Size);

    private sealed record DownloadPaths(
        string CompletedPath,
        string PartialPath,
        string LegacyCompletedPath,
        string LegacyPartialPath);
}

public sealed class GoogleDriveReconnectRequiredException(string message) : InvalidOperationException(message);

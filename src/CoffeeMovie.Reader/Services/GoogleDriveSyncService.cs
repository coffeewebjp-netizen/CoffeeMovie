using CoffeeMovie.Reader.Models;

namespace CoffeeMovie.Reader.Services;

public sealed partial class GoogleDriveSyncService
{
    private readonly ReaderSyncSettingsService _settingsService;
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(30)
    };
    private readonly GoogleDriveAuthService _authService;
    private readonly GoogleDrivePackageListingService _packageListingService = new();
    private readonly DrivePackageDownloadService _packageDownloadService;

    public GoogleDriveSyncService(ReaderSyncSettingsService settingsService)
    {
        _settingsService = settingsService;
        _authService = new GoogleDriveAuthService(_settingsService, _httpClient);
        _packageDownloadService = new DrivePackageDownloadService(
            _settingsService,
            _httpClient,
            _authService.GetValidAccessTokenAsync,
            _authService.ClearCachedAccessToken,
            GoogleDriveAuthService.GetErrorMessage);
    }

    public async Task<IReadOnlyList<SyncMovieCandidate>> FindPackagesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.GoogleDriveFolderId))
        {
            return [];
        }

        var accessToken = await _authService.GetValidAccessTokenAsync(settings, cancellationToken);
        return await _packageListingService.FindPackagesAsync(
            _httpClient,
            settings.GoogleDriveFolderId,
            accessToken,
            body => $"Google Drive一覧の取得に失敗しました: {GoogleDriveAuthService.GetErrorMessage(body)}",
            cancellationToken);
    }
}

public sealed class GoogleDriveReconnectRequiredException(string message) : InvalidOperationException(message);

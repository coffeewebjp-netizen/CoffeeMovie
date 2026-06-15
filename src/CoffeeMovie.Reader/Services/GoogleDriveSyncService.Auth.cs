using CoffeeMovie.Reader.Models;

namespace CoffeeMovie.Reader.Services;

public sealed partial class GoogleDriveSyncService
{
    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        return await _authService.IsConfiguredAsync(cancellationToken);
    }

    public async Task<ReaderSyncSettings> SaveConfigurationAsync(
        string clientId,
        string? clientSecret,
        string folderInput,
        CancellationToken cancellationToken = default)
    {
        return await _authService.SaveConfigurationAsync(clientId, clientSecret, folderInput, cancellationToken);
    }

    public async Task AuthorizeWithBrowserAsync(
        ReaderSyncSettings settings,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _authService.AuthorizeWithBrowserAsync(settings, progress, cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _authService.DisconnectAsync(cancellationToken);
    }
}

using CoffeeMovie.Reader.Models;

namespace CoffeeMovie.Reader.Services;

public sealed partial class GoogleDriveSyncService
{
    public async Task<string> DownloadSidecarToCacheAsync(
        SyncMovieCandidate package,
        IProgress<SyncTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await _packageDownloadService.DownloadSidecarToCacheAsync(package, progress, cancellationToken);
    }

    public async Task<string> DownloadPackageToCacheAsync(
        SyncMovieCandidate package,
        IProgress<SyncTransferProgress>? progress = null,
        bool restartDownload = false,
        CancellationToken cancellationToken = default)
    {
        return await _packageDownloadService.DownloadPackageToCacheAsync(
            package,
            progress,
            restartDownload,
            cancellationToken);
    }

    public SyncDownloadState GetPackageDownloadState(SyncMovieCandidate package)
    {
        return _packageDownloadService.GetPackageDownloadState(package);
    }
}

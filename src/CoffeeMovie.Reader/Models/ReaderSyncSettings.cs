namespace CoffeeMovie.Reader.Models;

public sealed class ReaderSyncSettings
{
    public string? GoogleDriveClientId { get; set; }

    public string? GoogleDriveClientSecret { get; set; }

    public string? GoogleDriveFolderId { get; set; }

    public string? GoogleDriveFolderName { get; set; }

    public DateTimeOffset? GoogleDriveConnectedAt { get; set; }
}

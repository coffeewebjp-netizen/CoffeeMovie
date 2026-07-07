namespace CoffeeMovie.Reader.Models;

public sealed class ReaderSyncSettings
{
    public string? GoogleDriveClientId { get; set; }

    public string? GoogleDriveClientSecret { get; set; }

    public string? GoogleDriveFolderId { get; set; }

    public string? GoogleDriveFolderName { get; set; }

    public DateTimeOffset? GoogleDriveConnectedAt { get; set; }

    public string? CoffeeLearningBaseUrl { get; set; }

    public string? CoffeeLearningDeckId { get; set; }

    public string CoffeeLearningScoringMode { get; set; } = "ai-provider";

    public string? CoffeeLearningScoringProvider { get; set; } = "openai";

    public string? CoffeeLearningScoringProviderBaseUrl { get; set; }

    public string? CoffeeLearningScoringProviderModel { get; set; }
}

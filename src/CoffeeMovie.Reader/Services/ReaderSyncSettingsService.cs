using System.Text.Json;
using CoffeeMovie.Reader.Models;

namespace CoffeeMovie.Reader.Services;

public sealed class ReaderSyncSettingsService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private string SettingsPath => Path.Combine(FileSystem.AppDataDirectory, "reader-sync-settings.json");

    public async Task<ReaderSyncSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            return new ReaderSyncSettings();
        }

        await using var stream = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await JsonSerializer.DeserializeAsync<ReaderSyncSettings>(stream, _jsonOptions, cancellationToken)
            ?? new ReaderSyncSettings();
    }

    public async Task SaveSettingsAsync(ReaderSyncSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(FileSystem.AppDataDirectory);
        var tempPath = SettingsPath + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, settings, _jsonOptions, cancellationToken);
        }

        File.Move(tempPath, SettingsPath, overwrite: true);
    }
}

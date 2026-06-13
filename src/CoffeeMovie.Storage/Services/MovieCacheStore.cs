using System.Text.Json;
using CoffeeMovie.Storage.Models;

namespace CoffeeMovie.Storage.Services;

public sealed class MovieCacheStore
{
    private readonly CoffeeMoviePaths _paths;

    public MovieCacheStore(CoffeeMoviePaths paths)
    {
        _paths = paths;
    }

    public async Task<MovieCacheIndex> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.CacheIndexPath))
        {
            return new MovieCacheIndex();
        }

        await using var stream = File.OpenRead(_paths.CacheIndexPath);
        return await JsonSerializer.DeserializeAsync<MovieCacheIndex>(
            stream,
            JsonStoreOptions.Default,
            cancellationToken) ?? new MovieCacheIndex();
    }

    public async Task SaveAsync(MovieCacheIndex index, CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        await using var stream = File.Create(_paths.CacheIndexPath);
        await JsonSerializer.SerializeAsync(stream, index, JsonStoreOptions.Default, cancellationToken);
    }

    public async Task<MovieCacheEntry?> FindFreshEntryAsync(
        string sourceKey,
        long sizeBytes,
        DateTimeOffset? sourceModifiedAt,
        string? sourceFingerprint,
        CancellationToken cancellationToken = default)
    {
        var index = await LoadAsync(cancellationToken);
        return index.Entries.FirstOrDefault(entry =>
            string.Equals(entry.SourceKey, sourceKey, StringComparison.Ordinal)
            && entry.Matches(sizeBytes, sourceModifiedAt, sourceFingerprint));
    }

    public async Task UpsertAsync(MovieCacheEntry entry, CancellationToken cancellationToken = default)
    {
        var index = await LoadAsync(cancellationToken);
        index.Entries.RemoveAll(existing =>
            string.Equals(existing.SourceKey, entry.SourceKey, StringComparison.Ordinal));
        index.Entries.Add(entry);
        await SaveAsync(index, cancellationToken);
    }
}


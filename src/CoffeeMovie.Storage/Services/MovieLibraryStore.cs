using System.Text.Json;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Storage.Models;

namespace CoffeeMovie.Storage.Services;

public sealed class MovieLibraryStore
{
    private readonly CoffeeMoviePaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MovieLibrary? _cachedLibrary;

    public MovieLibraryStore(CoffeeMoviePaths paths)
    {
        _paths = paths;
    }

    public int Revision { get; private set; }

    public async Task<MovieLibrary> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MovieLibrary> ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _cachedLibrary = await ReadLibraryAsync(cancellationToken);
            Revision++;
            return _cachedLibrary;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(MovieLibrary library, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await SaveCoreAsync(library, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertMovieAsync(Movie movie, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var library = await LoadCoreAsync(cancellationToken);
            library.Movies.RemoveAll(existing => string.Equals(existing.Id, movie.Id, StringComparison.Ordinal));
            movie.UpdatedAt = DateTimeOffset.UtcNow;
            library.Movies.Add(movie);
            library.Movies.Sort((left, right) => right.UpdatedAt.CompareTo(left.UpdatedAt));
            await SaveCoreAsync(library, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<MovieLibrary> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_cachedLibrary is not null)
        {
            return _cachedLibrary;
        }

        _cachedLibrary = await ReadLibraryAsync(cancellationToken);
        return _cachedLibrary;
    }

    private async Task<MovieLibrary> ReadLibraryAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.LibraryPath))
        {
            return new MovieLibrary();
        }

        await using var stream = File.OpenRead(_paths.LibraryPath);
        return await JsonSerializer.DeserializeAsync<MovieLibrary>(
            stream,
            JsonStoreOptions.Default,
            cancellationToken) ?? new MovieLibrary();
    }

    private async Task SaveCoreAsync(MovieLibrary library, CancellationToken cancellationToken)
    {
        _paths.EnsureCreated();
        await using var stream = File.Create(_paths.LibraryPath);
        await JsonSerializer.SerializeAsync(stream, library, JsonStoreOptions.Default, cancellationToken);
        _cachedLibrary = library;
        Revision++;
    }
}


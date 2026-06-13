using System.Text.Json;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Storage.Models;

namespace CoffeeMovie.Storage.Services;

public sealed class MovieLibraryStore
{
    private readonly CoffeeMoviePaths _paths;

    public MovieLibraryStore(CoffeeMoviePaths paths)
    {
        _paths = paths;
    }

    public async Task<MovieLibrary> LoadAsync(CancellationToken cancellationToken = default)
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

    public async Task SaveAsync(MovieLibrary library, CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        await using var stream = File.Create(_paths.LibraryPath);
        await JsonSerializer.SerializeAsync(stream, library, JsonStoreOptions.Default, cancellationToken);
    }

    public async Task UpsertMovieAsync(Movie movie, CancellationToken cancellationToken = default)
    {
        var library = await LoadAsync(cancellationToken);
        library.Movies.RemoveAll(existing => string.Equals(existing.Id, movie.Id, StringComparison.Ordinal));
        movie.UpdatedAt = DateTimeOffset.UtcNow;
        library.Movies.Add(movie);
        library.Movies.Sort((left, right) => right.UpdatedAt.CompareTo(left.UpdatedAt));
        await SaveAsync(library, cancellationToken);
    }
}


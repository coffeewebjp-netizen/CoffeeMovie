using CoffeeMovie.Storage.Models;
using CoffeeMovie.Storage.Services;

namespace CoffeeMovie.Reader.Services;

public sealed partial class ReaderLibraryService
{
    private readonly CoffeeLearningRegistrationSyncService _coffeeLearningRegistrationSyncService = new();

    public async Task<byte[]> ExportCoffeeLearningRegistrationSyncAsync(
        CancellationToken cancellationToken = default)
    {
        var library = await _libraryStore.LoadAsync(cancellationToken);
        return _coffeeLearningRegistrationSyncService.Serialize(library);
    }

    public async Task<CoffeeLearningRegistrationSyncMergeResult> ImportCoffeeLearningRegistrationSyncAsync(
        Stream input,
        CancellationToken cancellationToken = default)
    {
        var document = await _coffeeLearningRegistrationSyncService.ReadAsync(input, cancellationToken);
        var library = await _libraryStore.LoadAsync(cancellationToken);
        var result = _coffeeLearningRegistrationSyncService.Merge(library, document);
        if (result.MoviesChanged > 0)
        {
            await _libraryStore.SaveAsync(library, cancellationToken);
        }

        return result;
    }
}

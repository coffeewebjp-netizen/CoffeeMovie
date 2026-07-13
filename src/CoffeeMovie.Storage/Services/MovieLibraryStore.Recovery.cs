using System.Text;
using System.Text.Json;
using CoffeeMovie.Storage.Models;

namespace CoffeeMovie.Storage.Services;

public sealed partial class MovieLibraryStore
{
    private string? _recoveryMessage;

    public string? ConsumeRecoveryMessage()
    {
        var message = _recoveryMessage;
        _recoveryMessage = null;
        return message;
    }

    private async Task<MovieLibrary> ReadLibraryWithRecoveryAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.LibraryPath))
        {
            return new MovieLibrary();
        }

        try
        {
            return await DeserializeLibraryAsync(_paths.LibraryPath, cancellationToken);
        }
        catch (JsonException)
        {
            return await RecoverLibraryAsync(cancellationToken);
        }
        catch (NotSupportedException)
        {
            return await RecoverLibraryAsync(cancellationToken);
        }
    }

    private async Task<MovieLibrary> RecoverLibraryAsync(CancellationToken cancellationToken)
    {
        var backupPath = _paths.LibraryPath + ".bak";
        MovieLibrary? recovered = null;
        var recoverySource = string.Empty;
        if (File.Exists(backupPath))
        {
            try
            {
                recovered = await DeserializeLibraryAsync(backupPath, cancellationToken);
                recoverySource = "自動バックアップ";
            }
            catch (JsonException)
            {
                // Fall through to completed-movie salvage.
            }
            catch (NotSupportedException)
            {
                // Fall through to completed-movie salvage.
            }
        }

        if (recovered is null)
        {
            var bytes = await File.ReadAllBytesAsync(_paths.LibraryPath, cancellationToken);
            recovered = TrySalvageCompletedMovies(bytes);
            recoverySource = "破損前までの動画データ";
        }

        if (recovered is null)
        {
            throw new InvalidDataException(
                $"CoffeeMovieライブラリが破損しており、自動復旧できません。元ファイル: {_paths.LibraryPath}");
        }

        var corruptCopyPath = CreateCorruptCopyPath(_paths.LibraryPath);
        File.Copy(_paths.LibraryPath, corruptCopyPath, overwrite: false);
        await WriteLibraryAtomicallyAsync(recovered, backupCurrent: false, cancellationToken);
        _recoveryMessage =
            $"library.json の破損を検出し、{recoverySource}から動画 {recovered.Movies.Count} 本を復旧しました。"
            + $" 元ファイルは {Path.GetFileName(corruptCopyPath)} として保存しています。"
            + " Driveにだけ存在する動画は［Driveから取り込み］で復元できます。";
        return recovered;
    }

    private async Task WriteLibraryAtomicallyAsync(
        MovieLibrary library,
        bool backupCurrent,
        CancellationToken cancellationToken)
    {
        _paths.EnsureCreated();
        var tempPath = _paths.LibraryPath + ".tmp";
        var backupPath = _paths.LibraryPath + ".bak";
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, library, JsonStoreOptions.Default, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            _ = await DeserializeLibraryAsync(tempPath, cancellationToken);
            if (backupCurrent && File.Exists(_paths.LibraryPath))
            {
                File.Copy(_paths.LibraryPath, backupPath, overwrite: true);
            }

            File.Move(tempPath, _paths.LibraryPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static async Task<MovieLibrary> DeserializeLibraryAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<MovieLibrary>(
            stream,
            JsonStoreOptions.Default,
            cancellationToken) ?? new MovieLibrary();
    }

    private static MovieLibrary? TrySalvageCompletedMovies(ReadOnlySpan<byte> bytes)
    {
        long lastCompletedMovieOffset = 0;
        var waitingForMoviesArray = false;
        int? moviesArrayDepth = null;
        var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName
                    && reader.CurrentDepth == 1
                    && reader.ValueTextEquals("movies"))
                {
                    waitingForMoviesArray = true;
                    continue;
                }

                if (waitingForMoviesArray && reader.TokenType == JsonTokenType.StartArray)
                {
                    moviesArrayDepth = reader.CurrentDepth;
                    waitingForMoviesArray = false;
                    continue;
                }

                if (moviesArrayDepth is not null
                    && reader.TokenType == JsonTokenType.EndObject
                    && reader.CurrentDepth == moviesArrayDepth.Value + 1)
                {
                    lastCompletedMovieOffset = reader.BytesConsumed;
                }
            }
        }
        catch (JsonException)
        {
            // The last successfully completed movie offset is still usable.
        }

        if (lastCompletedMovieOffset <= 0 || lastCompletedMovieOffset > bytes.Length)
        {
            return null;
        }

        using var repaired = new MemoryStream((int)lastCompletedMovieOffset + 16);
        repaired.Write(bytes[..(int)lastCompletedMovieOffset]);
        repaired.Write(Encoding.UTF8.GetBytes("\n  ]\n}\n"));
        repaired.Position = 0;
        try
        {
            return JsonSerializer.Deserialize<MovieLibrary>(repaired, JsonStoreOptions.Default);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string CreateCorruptCopyPath(string libraryPath)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var candidate = $"{libraryPath}.corrupt-{timestamp}";
        var suffix = 1;
        while (File.Exists(candidate))
        {
            candidate = $"{libraryPath}.corrupt-{timestamp}-{suffix++}";
        }

        return candidate;
    }
}

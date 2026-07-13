using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Storage.Models;

namespace CoffeeMovie.Storage.Services;

public sealed class CoffeeLearningRegistrationSyncService
{
    public const string FileNamePrefix = "coffeemovie-coffeelearning-registration-";
    public const string FileNameSuffix = ".json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    public static bool IsSyncFileName(string? fileName)
    {
        return !string.IsNullOrWhiteSpace(fileName)
            && fileName.StartsWith(FileNamePrefix, StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(FileNameSuffix, StringComparison.OrdinalIgnoreCase);
    }

    public static string CreateFileName(string deviceId)
    {
        var safeId = new string((deviceId ?? string.Empty)
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .ToArray());
        if (string.IsNullOrWhiteSpace(safeId))
        {
            safeId = "device";
        }

        return FileNamePrefix + safeId.ToLowerInvariant() + FileNameSuffix;
    }

    public CoffeeLearningRegistrationSyncDocument CreateDocument(MovieLibrary library)
    {
        return new CoffeeLearningRegistrationSyncDocument
        {
            ExportedAt = DateTimeOffset.UtcNow,
            Movies = library.Movies
                .Select(CreateMovie)
                .Where(movie => movie.SubtitleTracks.Count > 0)
                .OrderBy(movie => movie.Title, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    public async Task WriteAsync(
        MovieLibrary library,
        string path,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, CreateDocument(library), JsonOptions, cancellationToken);
    }

    public async Task<CoffeeLearningRegistrationSyncDocument> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        return await ReadAsync(stream, cancellationToken);
    }

    public async Task<CoffeeLearningRegistrationSyncDocument> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var document = await JsonSerializer.DeserializeAsync<CoffeeLearningRegistrationSyncDocument>(
            stream,
            JsonOptions,
            cancellationToken) ?? new CoffeeLearningRegistrationSyncDocument();
        if (document.SchemaVersion != 1
            || !string.Equals(document.PackageType, "coffeelearning-registration-state", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unsupported CoffeeLearning registration sync document.");
        }

        return document;
    }

    public byte[] Serialize(MovieLibrary library)
    {
        return JsonSerializer.SerializeToUtf8Bytes(CreateDocument(library), JsonOptions);
    }

    public CoffeeLearningRegistrationSyncMergeResult Merge(
        MovieLibrary library,
        CoffeeLearningRegistrationSyncDocument document)
    {
        var moviesChanged = 0;
        var tracksChanged = 0;
        var cuesChanged = 0;
        var moviesSkipped = 0;

        foreach (var incomingMovie in document.Movies ?? [])
        {
            var movie = FindMovie(library.Movies, incomingMovie);
            if (movie is null)
            {
                moviesSkipped++;
                continue;
            }

            var movieChanged = false;
            foreach (var incomingTrack in incomingMovie.SubtitleTracks ?? [])
            {
                var track = FindTrack(movie, incomingTrack);
                if (track is null)
                {
                    continue;
                }

                var trackChanged = false;
                foreach (var incomingCue in incomingTrack.Cues ?? [])
                {
                    if (!IsRegistered(incomingCue))
                    {
                        continue;
                    }

                    var state = FindOrCreateState(track, incomingCue);
                    if (!MergeRegistration(state, incomingCue))
                    {
                        continue;
                    }

                    cuesChanged++;
                    trackChanged = true;
                    movieChanged = true;
                }

                if (trackChanged)
                {
                    tracksChanged++;
                }
            }

            if (movieChanged)
            {
                movie.UpdatedAt = DateTimeOffset.UtcNow;
                moviesChanged++;
            }
        }

        return new CoffeeLearningRegistrationSyncMergeResult(
            moviesChanged,
            tracksChanged,
            cuesChanged,
            moviesSkipped);
    }

    private static CoffeeLearningRegistrationSyncMovie CreateMovie(Movie movie)
    {
        return new CoffeeLearningRegistrationSyncMovie
        {
            MovieId = movie.Id,
            Title = movie.Title,
            SourceContentFingerprint = movie.SourceContentFingerprint,
            SourcePackageName = movie.SourcePackageName,
            SubtitleTracks = movie.SubtitleTracks
                .Select(CreateTrack)
                .Where(track => track.Cues.Count > 0)
                .ToList()
        };
    }

    private static CoffeeLearningRegistrationSyncTrack CreateTrack(SubtitleTrack track)
    {
        return new CoffeeLearningRegistrationSyncTrack
        {
            TrackId = track.Id,
            SourceFileName = track.SourceFileName,
            Language = track.Language,
            Role = track.Role.ToString(),
            GroupKey = track.GroupKey,
            CueCount = track.CueCount,
            Cues = (track.CueLearningStates ?? [])
                .Where(IsRegistered)
                .OrderBy(state => state.CueIndex)
                .Select(state => new CoffeeLearningRegistrationSyncCue
                {
                    CueId = state.CueId,
                    CueIndex = state.CueIndex,
                    RegisteredAt = state.CoffeeLearningRegisteredAt,
                    WordId = state.CoffeeLearningWordId,
                    DeckId = state.CoffeeLearningDeckId,
                    UpdatedAt = state.UpdatedAt
                })
                .ToList()
        };
    }

    private static Movie? FindMovie(
        IEnumerable<Movie> movies,
        CoffeeLearningRegistrationSyncMovie incoming)
    {
        return movies.FirstOrDefault(movie => string.Equals(movie.Id, incoming.MovieId, StringComparison.Ordinal))
            ?? movies.FirstOrDefault(movie =>
                !string.IsNullOrWhiteSpace(movie.SourceContentFingerprint)
                && string.Equals(movie.SourceContentFingerprint, incoming.SourceContentFingerprint, StringComparison.Ordinal))
            ?? movies.FirstOrDefault(movie =>
                !string.IsNullOrWhiteSpace(movie.SourcePackageName)
                && string.Equals(movie.SourcePackageName, incoming.SourcePackageName, StringComparison.OrdinalIgnoreCase));
    }

    private static SubtitleTrack? FindTrack(Movie movie, CoffeeLearningRegistrationSyncTrack incoming)
    {
        return movie.SubtitleTracks.FirstOrDefault(track =>
                !string.IsNullOrWhiteSpace(incoming.TrackId)
                && string.Equals(track.Id, incoming.TrackId, StringComparison.Ordinal))
            ?? movie.SubtitleTracks.FirstOrDefault(track =>
                string.Equals(track.SourceFileName, incoming.SourceFileName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(track.Language, incoming.Language, StringComparison.OrdinalIgnoreCase))
            ?? movie.SubtitleTracks.FirstOrDefault(track =>
                string.Equals(track.GroupKey, incoming.GroupKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(track.Role.ToString(), incoming.Role, StringComparison.OrdinalIgnoreCase));
    }

    private static SubtitleCueLearningState FindOrCreateState(
        SubtitleTrack track,
        CoffeeLearningRegistrationSyncCue incoming)
    {
        var state = track.CueLearningStates.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(incoming.CueId)
                && string.Equals(candidate.CueId, incoming.CueId, StringComparison.Ordinal))
            ?? track.CueLearningStates.FirstOrDefault(candidate =>
                incoming.CueIndex > 0 && candidate.CueIndex == incoming.CueIndex);
        if (state is not null)
        {
            return state;
        }

        state = new SubtitleCueLearningState
        {
            CueId = incoming.CueId,
            CueIndex = incoming.CueIndex
        };
        track.CueLearningStates.Add(state);
        return state;
    }

    private static bool MergeRegistration(
        SubtitleCueLearningState state,
        CoffeeLearningRegistrationSyncCue incoming)
    {
        var changed = false;
        if (incoming.RegisteredAt is { } registeredAt
            && (state.CoffeeLearningRegisteredAt is null || registeredAt > state.CoffeeLearningRegisteredAt))
        {
            state.CoffeeLearningRegisteredAt = registeredAt;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(state.CoffeeLearningWordId)
            && !string.IsNullOrWhiteSpace(incoming.WordId))
        {
            state.CoffeeLearningWordId = incoming.WordId.Trim();
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(state.CoffeeLearningDeckId)
            && !string.IsNullOrWhiteSpace(incoming.DeckId))
        {
            state.CoffeeLearningDeckId = incoming.DeckId.Trim();
            changed = true;
        }

        if (changed && incoming.UpdatedAt > state.UpdatedAt)
        {
            state.UpdatedAt = incoming.UpdatedAt;
        }

        return changed;
    }

    private static bool IsRegistered(SubtitleCueLearningState state)
    {
        return state.CoffeeLearningRegisteredAt is not null
            || !string.IsNullOrWhiteSpace(state.CoffeeLearningWordId);
    }

    private static bool IsRegistered(CoffeeLearningRegistrationSyncCue cue)
    {
        return cue.RegisteredAt is not null || !string.IsNullOrWhiteSpace(cue.WordId);
    }
}

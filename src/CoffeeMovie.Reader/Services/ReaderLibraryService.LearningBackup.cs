using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Reader.Models;

namespace CoffeeMovie.Reader.Services;

public sealed partial class ReaderLibraryService
{
    private static readonly JsonSerializerOptions LearningBackupJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    public async Task<LearningStateBackupExportResult> ExportLearningStateBackupAsync(
        CancellationToken cancellationToken = default)
    {
        var library = await _libraryStore.LoadAsync(cancellationToken);
        var backup = new LearningStateBackup
        {
            ExportedAt = DateTimeOffset.UtcNow,
            Movies = library.Movies
                .Select(CreateLearningStateBackupMovie)
                .Where(HasBackupPayload)
                .OrderBy(movie => movie.SeriesTitle ?? movie.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(movie => movie.SeasonNumber ?? int.MaxValue)
                .ThenBy(movie => movie.EpisodeNumber ?? int.MaxValue)
                .ToList()
        };

        var backupDirectory = Path.Combine(FileSystem.AppDataDirectory, "learning-state-backups");
        Directory.CreateDirectory(backupDirectory);
        var filePath = Path.Combine(
            backupDirectory,
            $"coffeemovie-learning-state-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.json");

        await using (var stream = File.Create(filePath))
        {
            await JsonSerializer.SerializeAsync(stream, backup, LearningBackupJsonOptions, cancellationToken);
        }

        return new LearningStateBackupExportResult(
            filePath,
            backup.Movies.Count,
            backup.Movies.Sum(movie => movie.SubtitleTracks.Sum(track => track.LearningStates.Count)));
    }

    public async Task<LearningStateBackupImportResult> ImportLearningStateBackupAsync(
        FileResult file,
        CancellationToken cancellationToken = default)
    {
        await using var input = await file.OpenReadAsync();
        return await ImportLearningStateBackupAsync(input, cancellationToken);
    }

    public async Task<LearningStateBackupImportResult> ImportLearningStateBackupAsync(
        Stream input,
        CancellationToken cancellationToken = default)
    {
        var backup = await JsonSerializer.DeserializeAsync<LearningStateBackup>(
            input,
            LearningBackupJsonOptions,
            cancellationToken) ?? new LearningStateBackup();

        var library = await _libraryStore.LoadAsync(cancellationToken);
        var moviesChanged = 0;
        var tracksChanged = 0;
        var statesImported = 0;
        var moviesSkipped = 0;

        foreach (var backupMovie in backup.Movies)
        {
            var movie = FindBackupTargetMovie(library.Movies, backupMovie);
            if (movie is null)
            {
                moviesSkipped++;
                continue;
            }

            var movieChanged = MergeMovieLearningBackup(movie, backupMovie, ref tracksChanged, ref statesImported);
            if (!movieChanged)
            {
                continue;
            }

            moviesChanged++;
            movie.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (moviesChanged > 0)
        {
            await _libraryStore.SaveAsync(library, cancellationToken);
        }

        return new LearningStateBackupImportResult(
            moviesChanged,
            tracksChanged,
            statesImported,
            moviesSkipped);
    }

    private static LearningStateBackupMovie CreateLearningStateBackupMovie(Movie movie)
    {
        return new LearningStateBackupMovie
        {
            MovieId = movie.Id,
            Title = movie.Title,
            SeriesTitle = movie.SeriesTitle,
            SeasonNumber = movie.SeasonNumber,
            EpisodeNumber = movie.EpisodeNumber,
            SourcePackageUri = movie.SourcePackageUri,
            SourceContentFingerprint = movie.SourceContentFingerprint,
            Tags = (movie.Tags ?? [])
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Playback = movie.Playback ?? new PlaybackState(),
            UpdatedAt = movie.UpdatedAt,
            SubtitleTracks = movie.SubtitleTracks
                .Select(CreateLearningStateBackupTrack)
                .Where(track => track.LearningStates.Count > 0)
                .ToList()
        };
    }

    private static LearningStateBackupTrack CreateLearningStateBackupTrack(SubtitleTrack track)
    {
        return new LearningStateBackupTrack
        {
            TrackId = track.Id,
            SourceFileName = track.SourceFileName,
            Language = track.Language,
            Role = track.Role.ToString(),
            GroupKey = track.GroupKey,
            CueCount = track.CueCount,
            LearningStates = (track.CueLearningStates ?? [])
                .Select(NormalizeLearningState)
                .Where(HasLearningStatePayload)
                .OrderBy(state => state.CueIndex)
                .ToList()
        };
    }

    private static bool HasBackupPayload(LearningStateBackupMovie movie)
    {
        return movie.Tags.Count > 0
            || HasPlaybackPayload(movie.Playback)
            || movie.SubtitleTracks.Any(track => track.LearningStates.Count > 0);
    }

    private static bool HasLearningStatePayload(SubtitleCueLearningState state)
    {
        return state.IsFlagged
            || state.Tags.Count > 0
            || !string.IsNullOrWhiteSpace(state.Note)
            || !string.IsNullOrWhiteSpace(state.AiNote)
            || state.CoffeeLearningRegisteredAt is not null
            || !string.IsNullOrWhiteSpace(state.CoffeeLearningWordId)
            || !string.IsNullOrWhiteSpace(state.CoffeeLearningDeckId)
            || HasPracticePayload(state.Listening)
            || HasPracticePayload(state.Shadowing);
    }

    private static bool HasPracticePayload(CuePracticeMetric? metric)
    {
        return metric is not null
            && (metric.AttemptCount > 0
                || metric.OkCount > 0
                || metric.NgCount > 0
                || metric.LastAccuracy is not null
                || metric.BestAccuracy is not null
                || !string.IsNullOrWhiteSpace(metric.LastTranscript)
                || metric.LastDuration is not null
                || metric.LastPracticedAt is not null);
    }

    private static bool HasPlaybackPayload(PlaybackState? playback)
    {
        return playback is not null
            && (playback.PositionSeconds > 0
                || playback.DurationSeconds > 0
                || playback.LastWatchedAt is not null);
    }

    private static Movie? FindBackupTargetMovie(
        IEnumerable<Movie> movies,
        LearningStateBackupMovie backupMovie)
    {
        return movies.FirstOrDefault(movie =>
                string.Equals(movie.Id, backupMovie.MovieId, StringComparison.Ordinal))
            ?? movies.FirstOrDefault(movie =>
                !string.IsNullOrWhiteSpace(movie.SourceContentFingerprint)
                && string.Equals(movie.SourceContentFingerprint, backupMovie.SourceContentFingerprint, StringComparison.Ordinal))
            ?? movies.FirstOrDefault(movie =>
                !string.IsNullOrWhiteSpace(movie.SourcePackageUri)
                && string.Equals(movie.SourcePackageUri, backupMovie.SourcePackageUri, StringComparison.Ordinal));
    }

    private static bool MergeMovieLearningBackup(
        Movie movie,
        LearningStateBackupMovie backupMovie,
        ref int tracksChanged,
        ref int statesImported)
    {
        var changed = false;
        var currentTags = movie.Tags ?? [];
        var mergedTags = currentTags
            .Concat(backupMovie.Tags ?? [])
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!currentTags.SequenceEqual(mergedTags, StringComparer.OrdinalIgnoreCase))
        {
            movie.Tags = mergedTags;
            changed = true;
        }

        if (ShouldUseBackupPlayback(movie.Playback, backupMovie.Playback))
        {
            movie.Playback = backupMovie.Playback;
            changed = true;
        }

        foreach (var backupTrack in backupMovie.SubtitleTracks)
        {
            var track = FindBackupTargetTrack(movie, backupTrack);
            if (track is null || backupTrack.LearningStates.Count == 0)
            {
                continue;
            }

            var beforeSignature = CreateLearningStateSignature(track.CueLearningStates);
            track.CueLearningStates = MergeLearningStates(track.CueLearningStates, backupTrack.LearningStates);
            var afterSignature = CreateLearningStateSignature(track.CueLearningStates);
            if (string.Equals(beforeSignature, afterSignature, StringComparison.Ordinal))
            {
                continue;
            }

            tracksChanged++;
            statesImported += backupTrack.LearningStates.Count;
            changed = true;
        }

        return changed;
    }

    private static bool ShouldUseBackupPlayback(PlaybackState current, PlaybackState backup)
    {
        if (!HasPlaybackPayload(backup))
        {
            return false;
        }

        if (!HasPlaybackPayload(current))
        {
            return true;
        }

        if (backup.LastWatchedAt is null)
        {
            return false;
        }

        return current.LastWatchedAt is null || backup.LastWatchedAt >= current.LastWatchedAt;
    }

    private static SubtitleTrack? FindBackupTargetTrack(Movie movie, LearningStateBackupTrack backupTrack)
    {
        var role = Enum.TryParse<SubtitleTrackRole>(backupTrack.Role, ignoreCase: true, out var parsedRole)
            ? parsedRole
            : SubtitleTrackRole.Unknown;
        var probe = new SubtitleTrack
        {
            Id = backupTrack.TrackId,
            SourceFileName = backupTrack.SourceFileName,
            Language = backupTrack.Language,
            Role = role,
            GroupKey = backupTrack.GroupKey,
            CueCount = backupTrack.CueCount
        };

        return FindMatchingTrack(movie, probe);
    }

    private static string CreateLearningStateSignature(IEnumerable<SubtitleCueLearningState> states)
    {
        return JsonSerializer.Serialize(
            states.OrderBy(GetLearningStateKey),
            LearningBackupJsonOptions);
    }
}

using System.IO.Compression;
using System.Text;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Reader.Models;
using CoffeeMovie.Storage.Models;
using CoffeeMovie.Storage.Services;

namespace CoffeeMovie.Reader.Services;

public sealed partial class ReaderLibraryService
{
    public async Task<bool> ImportDriveSidecarAsync(
        SyncMovieCandidate package,
        string sidecarPath,
        CancellationToken cancellationToken = default)
    {
        var sidecar = await _packageService.ReadReaderPackageSidecarAsync(sidecarPath, cancellationToken);
        var existing = await GetMovieAsync(sidecar.SourceMovieId, cancellationToken);
        if (existing is not null
            && !string.IsNullOrWhiteSpace(sidecar.ContentFingerprint)
            && string.Equals(existing.SourceContentFingerprint, sidecar.ContentFingerprint, StringComparison.Ordinal))
        {
            ApplyPackageSourceMetadata(existing, sidecar, package);
            await ApplySidecarThumbnailAsync(existing, sidecar, cancellationToken);
            await _libraryStore.UpsertMovieAsync(existing, cancellationToken);
            return false;
        }

        var movie = CreateMovieFromSidecar(sidecar, package, existing);
        await ApplySidecarThumbnailAsync(movie, sidecar, cancellationToken);
        await _libraryStore.UpsertMovieAsync(movie, cancellationToken);
        return true;
    }

    public async Task<Movie> ImportCoffeeMoviePackageFileAsync(
        string packagePath,
        SyncMovieCandidate package,
        CancellationToken cancellationToken = default)
    {
        var manifest = await _packageService.ReadReaderPackageManifestAsync(packagePath, cancellationToken);
        var existing = await GetMovieAsync(manifest.SourceMovieId, cancellationToken);
        var preserveSidecarMetadata = existing is not null
            && package.HasSidecar
            && !string.IsNullOrWhiteSpace(existing.SourceContentFingerprint)
            && !string.Equals(existing.SourceContentFingerprint, manifest.ContentFingerprint, StringComparison.Ordinal);
        Movie movie;
        if (preserveSidecarMetadata)
        {
            movie = existing!;
            await ExtractPackageFilesAsync(
                packagePath,
                manifest,
                movie,
                cancellationToken,
                extractSupportingFiles: false);
        }
        else
        {
            movie = CreateMovieFromSidecar(manifest, package, existing);
            await ExtractPackageFilesAsync(packagePath, manifest, movie, cancellationToken);
            await ApplySidecarThumbnailAsync(movie, manifest, cancellationToken);
            movie.SourceMovieUpdatedAt = manifest.Movie.UpdatedAt;
            movie.SourceContentFingerprint = manifest.ContentFingerprint;
        }

        movie.Video.SourceKind = VideoSourceKind.GoogleDrive;
        movie.Video.SourceUri = package.ContentUri;
        movie.Video.SourceKey = package.ContentUri;
        movie.SourcePackageUri = package.ContentUri;
        movie.SourcePackageName = package.FileName;
        movie.SourcePackageLastModified = package.LastModified;
        movie.SourcePackageSize = package.Size;
        RefreshMovieSceneMarkers(movie);

        await _libraryStore.UpsertMovieAsync(movie, cancellationToken);
        if (!string.IsNullOrWhiteSpace(package.ContentUri) && !string.IsNullOrWhiteSpace(movie.Video.CachePath))
        {
            await _cacheStore.UpsertAsync(new MovieCacheEntry
            {
                SourceKey = package.ContentUri,
                MovieId = movie.Id,
                FileName = movie.Video.FileName,
                LocalPath = movie.Video.CachePath,
                ContentType = movie.Video.ContentType,
                SizeBytes = package.Size ?? movie.Video.SizeBytes,
                SourceModifiedAt = FromUnixMilliseconds(package.LastModified),
                SourceFingerprint = movie.SourceContentFingerprint
            }, cancellationToken);
        }

        return movie;
    }

    public async Task<bool> HasCurrentDriveSidecarAsync(
        SyncMovieCandidate package,
        CancellationToken cancellationToken = default)
    {
        var existing = await FindMovieByDrivePackageAsync(package, cancellationToken);
        return existing is not null && HasCurrentSidecarMetadata(existing, package);
    }

    private static void ApplyPackageSourceMetadata(
        Movie movie,
        CoffeeMovieSidecar sidecar,
        SyncMovieCandidate package)
    {
        movie.SourcePackageUri = package.ContentUri;
        movie.SourcePackageName = package.FileName;
        movie.SourcePackageLastModified = package.LastModified;
        movie.SourcePackageSize = package.Size;
        movie.SourceSidecarUri = package.SidecarContentUri;
        movie.SourceSidecarName = package.SidecarFileName;
        movie.SourceSidecarLastModified = package.SidecarLastModified;
        movie.SourceSidecarSize = package.SidecarSize;
        movie.SourceMovieUpdatedAt = sidecar.Movie.UpdatedAt;
        movie.SourceContentFingerprint = sidecar.ContentFingerprint;
    }

    private async Task<Movie?> FindMovieByDrivePackageAsync(
        SyncMovieCandidate package,
        CancellationToken cancellationToken)
    {
        var library = await _libraryStore.LoadAsync(cancellationToken);
        return library.Movies.FirstOrDefault(movie =>
                !string.IsNullOrWhiteSpace(package.ContentUri)
                && string.Equals(movie.SourcePackageUri, package.ContentUri, StringComparison.Ordinal))
            ?? library.Movies.FirstOrDefault(movie =>
                !string.IsNullOrWhiteSpace(package.SidecarContentUri)
                && string.Equals(movie.SourceSidecarUri, package.SidecarContentUri, StringComparison.Ordinal));
    }

    private static bool HasCurrentSidecarMetadata(Movie movie, SyncMovieCandidate package)
    {
        if (!package.HasSidecar)
        {
            return false;
        }

        var hasStoredSidecarMetadata = movie.SourceSidecarLastModified is not null
            || movie.SourceSidecarSize is not null
            || !string.IsNullOrWhiteSpace(movie.SourceSidecarUri);
        var hasComparableMetadata = false;
        if (package.SidecarLastModified is not null)
        {
            hasComparableMetadata = true;
            if (movie.SourceSidecarLastModified != package.SidecarLastModified)
            {
                return !hasStoredSidecarMetadata && HasCurrentPackageMetadata(movie, package);
            }
        }

        if (package.SidecarSize is not null)
        {
            hasComparableMetadata = true;
            if (movie.SourceSidecarSize != package.SidecarSize)
            {
                return !hasStoredSidecarMetadata && HasCurrentPackageMetadata(movie, package);
            }
        }

        if (!hasComparableMetadata)
        {
            return HasCurrentPackageMetadata(movie, package);
        }

        if (!string.IsNullOrWhiteSpace(movie.SourceSidecarUri)
            && !string.Equals(movie.SourceSidecarUri, package.SidecarContentUri, StringComparison.Ordinal))
        {
            return !hasStoredSidecarMetadata && HasCurrentPackageMetadata(movie, package);
        }

        return !string.IsNullOrWhiteSpace(movie.SourceContentFingerprint);
    }

    private static bool HasCurrentPackageMetadata(Movie movie, SyncMovieCandidate package)
    {
        if (string.IsNullOrWhiteSpace(movie.SourceContentFingerprint))
        {
            return false;
        }

        var hasComparableMetadata = false;
        if (package.LastModified is not null)
        {
            hasComparableMetadata = true;
            if (movie.SourcePackageLastModified != package.LastModified)
            {
                return false;
            }
        }

        if (package.Size is not null)
        {
            hasComparableMetadata = true;
            if (movie.SourcePackageSize != package.Size)
            {
                return false;
            }
        }

        return hasComparableMetadata;
    }

    private async Task ExtractPackageFilesAsync(
        string packagePath,
        CoffeeMovieSidecar manifest,
        Movie movie,
        CancellationToken cancellationToken,
        bool extractSupportingFiles = true)
    {
        await using var packageStream = File.OpenRead(packagePath);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);

        if (string.IsNullOrWhiteSpace(manifest.Video.PackagePath))
        {
            throw new InvalidOperationException("CoffeeMovieパッケージ内の動画パスがありません。");
        }

        var videoEntry = archive.GetEntry(manifest.Video.PackagePath)
            ?? throw new InvalidOperationException($"CoffeeMovieパッケージ内の動画が見つかりません: {manifest.Video.PackagePath}");
        var videoDirectory = _paths.GetMovieVideoDirectory(movie.Id);
        Directory.CreateDirectory(videoDirectory);
        var videoFileName = SanitizeFileName(string.IsNullOrWhiteSpace(movie.Video.FileName)
            ? videoEntry.Name
            : movie.Video.FileName);
        var videoPath = Path.Combine(videoDirectory, videoFileName);
        await ExtractEntryAsync(videoEntry, videoPath, cancellationToken);
        movie.Video.FileName = videoFileName;
        movie.Video.CachePath = videoPath;
        movie.Video.SizeBytes = new FileInfo(videoPath).Length;
        movie.Video.ContentType = string.IsNullOrWhiteSpace(movie.Video.ContentType)
            ? GuessVideoContentType(videoFileName, null)
            : movie.Video.ContentType;

        if (!extractSupportingFiles)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(manifest.Video.ThumbnailPackagePath)
            && archive.GetEntry(manifest.Video.ThumbnailPackagePath) is { } thumbnailEntry)
        {
            var thumbnailPath = GetThumbnailCachePath(movie.Id, thumbnailEntry.Name);
            await ExtractEntryAsync(thumbnailEntry, thumbnailPath, cancellationToken);
            movie.Video.ThumbnailPath = thumbnailPath;
        }

        var subtitleDirectory = _paths.GetMovieSubtitleDirectory(movie.Id);
        Directory.CreateDirectory(subtitleDirectory);
        foreach (var track in movie.SubtitleTracks)
        {
            var packagedSubtitle = manifest.Subtitles.FirstOrDefault(subtitle =>
                string.Equals(subtitle.Id, track.Id, StringComparison.Ordinal));
            if (packagedSubtitle is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(packagedSubtitle.PackagePath)
                && archive.GetEntry(packagedSubtitle.PackagePath) is { } subtitleEntry)
            {
                var subtitleFileName = SanitizeFileName(string.IsNullOrWhiteSpace(track.SourceFileName)
                    ? subtitleEntry.Name
                    : track.SourceFileName);
                var subtitlePath = Path.Combine(subtitleDirectory, subtitleFileName);
                await ExtractEntryAsync(subtitleEntry, subtitlePath, cancellationToken);
                track.LocalPath = subtitlePath;
            }

            if (!string.IsNullOrWhiteSpace(packagedSubtitle.VttPackagePath)
                && archive.GetEntry(packagedSubtitle.VttPackagePath) is { } vttEntry)
            {
                var vttPath = Path.Combine(
                    subtitleDirectory,
                    Path.GetFileNameWithoutExtension(SanitizeFileName(track.SourceFileName)) + ".vtt");
                await ExtractEntryAsync(vttEntry, vttPath, cancellationToken);
                track.VttCachePath = vttPath;
            }
            else if (track.Cues.Count > 0)
            {
                var vttPath = Path.Combine(
                    subtitleDirectory,
                    Path.GetFileNameWithoutExtension(SanitizeFileName(track.SourceFileName)) + ".vtt");
                await File.WriteAllTextAsync(vttPath, SubtitleParser.ToWebVtt(track.Cues), Encoding.UTF8, cancellationToken);
                track.VttCachePath = vttPath;
            }
        }
    }

    private static async Task ExtractEntryAsync(
        ZipArchiveEntry entry,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var input = entry.Open();
        await using var output = File.Create(destinationPath);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static Movie CreateMovieFromSidecar(
        CoffeeMovieSidecar sidecar,
        SyncMovieCandidate package,
        Movie? existing)
    {
        var movieId = string.IsNullOrWhiteSpace(sidecar.SourceMovieId)
            ? sidecar.Movie.Id
            : sidecar.SourceMovieId;
        var movie = new Movie
        {
            Id = movieId,
            Title = sidecar.Movie.Title,
            SeriesTitle = sidecar.Movie.SeriesTitle,
            SeasonNumber = sidecar.Movie.SeasonNumber,
            EpisodeNumber = sidecar.Movie.EpisodeNumber,
            Description = sidecar.Movie.Description,
            Tags = (sidecar.Movie.Tags ?? []).Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Playback = existing?.Playback ?? new PlaybackState { DurationSeconds = sidecar.Movie.DurationSeconds },
            SourcePackageUri = package.ContentUri,
            SourcePackageName = package.FileName,
            SourcePackageLastModified = package.LastModified,
            SourcePackageSize = package.Size,
            SourceSidecarUri = package.SidecarContentUri,
            SourceSidecarName = package.SidecarFileName,
            SourceSidecarLastModified = package.SidecarLastModified,
            SourceSidecarSize = package.SidecarSize,
            SourceMovieUpdatedAt = sidecar.Movie.UpdatedAt,
            SourceContentFingerprint = sidecar.ContentFingerprint,
            CreatedAt = sidecar.Movie.CreatedAt == default ? DateTimeOffset.UtcNow : sidecar.Movie.CreatedAt,
            UpdatedAt = sidecar.Movie.UpdatedAt == default ? DateTimeOffset.UtcNow : sidecar.Movie.UpdatedAt,
            Video = new VideoAsset
            {
                SourceKind = VideoSourceKind.GoogleDrive,
                SourceUri = package.ContentUri,
                SourceKey = package.ContentUri,
                FileName = sidecar.Video.FileName,
                ContentType = sidecar.Video.ContentType,
                SizeBytes = sidecar.Video.SizeBytes,
                ModifiedAt = sidecar.Video.ModifiedAt,
                ContentFingerprint = sidecar.Video.ContentFingerprint,
                ThumbnailTimestampSeconds = sidecar.Video.ThumbnailTimestampSeconds
            },
            SubtitleTracks = (sidecar.Subtitles ?? [])
                .Select(CreateSubtitleTrackFromSidecar)
                .ToList()
        };

        MergeExistingMovieState(movie, existing);
        RefreshMovieSceneMarkers(movie);
        return movie;
    }

    private static SubtitleTrack CreateSubtitleTrackFromSidecar(CoffeeMovieSidecarSubtitle subtitle)
    {
        var format = Enum.TryParse<SubtitleFormat>(subtitle.Format, ignoreCase: true, out var parsedFormat)
            ? parsedFormat
            : SubtitleFormat.Unknown;
        var role = Enum.TryParse<SubtitleTrackRole>(subtitle.Role, ignoreCase: true, out var parsedRole)
            ? parsedRole
            : SubtitleTrackRole.Unknown;

        var track = new SubtitleTrack
        {
            Id = string.IsNullOrWhiteSpace(subtitle.Id) ? Guid.NewGuid().ToString("N") : subtitle.Id,
            Label = subtitle.Label,
            Language = subtitle.Language,
            Role = role,
            GroupKey = subtitle.GroupKey,
            Format = format,
            SourceFileName = string.IsNullOrWhiteSpace(subtitle.SourceFileName) ? subtitle.FileName : subtitle.SourceFileName,
            CueCount = subtitle.CueCount,
            Cues = (subtitle.Cues ?? [])
                .OrderBy(cue => cue.Index)
                .Select(cue => new SubtitleCue
                {
                    Id = cue.Id,
                    Index = cue.Index,
                    Start = TimeSpan.FromSeconds(cue.StartSeconds),
                    End = TimeSpan.FromSeconds(cue.EndSeconds),
                    Text = cue.Text
                })
                .ToList(),
            CueLearningStates = (subtitle.LearningStates ?? [])
                .Select(NormalizeLearningState)
                .ToList()
        };
        if (track.CueCount <= 0)
        {
            track.CueCount = track.Cues.Count;
        }

        return track;
    }

    private static void MergeExistingMovieState(Movie movie, Movie? existing)
    {
        if (existing is null)
        {
            return;
        }

        movie.Tags = movie.Tags
            .Concat(existing.Tags ?? [])
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (IsSameVideoAsset(movie.Video, existing.Video)
            && !string.IsNullOrWhiteSpace(existing.Video.CachePath)
            && File.Exists(existing.Video.CachePath))
        {
            movie.Video.CachePath = existing.Video.CachePath;
        }

        if (!string.IsNullOrWhiteSpace(existing.Video.ThumbnailPath)
            && File.Exists(existing.Video.ThumbnailPath))
        {
            movie.Video.ThumbnailPath = existing.Video.ThumbnailPath;
        }

        if (existing.Playback is not null)
        {
            movie.Playback = existing.Playback;
        }

        foreach (var track in movie.SubtitleTracks)
        {
            var existingTrack = FindMatchingTrack(existing, track);
            if (existingTrack is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(track.LocalPath)
                && !string.IsNullOrWhiteSpace(existingTrack.LocalPath)
                && File.Exists(existingTrack.LocalPath))
            {
                track.LocalPath = existingTrack.LocalPath;
            }

            if (string.IsNullOrWhiteSpace(track.VttCachePath)
                && !string.IsNullOrWhiteSpace(existingTrack.VttCachePath)
                && File.Exists(existingTrack.VttCachePath))
            {
                track.VttCachePath = existingTrack.VttCachePath;
            }

            track.CueLearningStates = MergeLearningStates(track.CueLearningStates, existingTrack.CueLearningStates);
        }
    }

    private static SubtitleTrack? FindMatchingTrack(Movie movie, SubtitleTrack track)
    {
        return movie.SubtitleTracks.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, track.Id, StringComparison.Ordinal))
            ?? movie.SubtitleTracks.FirstOrDefault(candidate =>
                string.Equals(candidate.SourceFileName, track.SourceFileName, StringComparison.OrdinalIgnoreCase))
            ?? movie.SubtitleTracks.FirstOrDefault(candidate =>
                string.Equals(candidate.Language, track.Language, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.GroupKey, track.GroupKey, StringComparison.OrdinalIgnoreCase)
                && candidate.Role == track.Role);
    }

    private static bool IsSameVideoAsset(VideoAsset left, VideoAsset right)
    {
        if (!string.IsNullOrWhiteSpace(left.ContentFingerprint)
            || !string.IsNullOrWhiteSpace(right.ContentFingerprint))
        {
            return string.Equals(left.ContentFingerprint, right.ContentFingerprint, StringComparison.Ordinal);
        }

        return string.Equals(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase)
            && left.SizeBytes == right.SizeBytes
            && Nullable.Equals(left.ModifiedAt, right.ModifiedAt);
    }

    private static List<SubtitleCueLearningState> MergeLearningStates(
        IEnumerable<SubtitleCueLearningState> packageStates,
        IEnumerable<SubtitleCueLearningState> localStates)
    {
        var merged = packageStates
            .Select(NormalizeLearningState)
            .ToDictionary(GetLearningStateKey, StringComparer.Ordinal);

        foreach (var local in localStates.Select(NormalizeLearningState))
        {
            var key = GetLearningStateKey(local);
            if (!merged.TryGetValue(key, out var package))
            {
                merged[key] = local;
                continue;
            }

            package.Tags = package.Tags
                .Concat(local.Tags)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            package.IsFlagged = package.IsFlagged || local.IsFlagged;
            package.Note = ChooseLocalText(package.Note, local.Note, package.UpdatedAt, local.UpdatedAt);
            package.AiNote = string.IsNullOrWhiteSpace(package.AiNote) ? local.AiNote : package.AiNote;
            MergeCoffeeLearningRegistration(package, local);
            package.Listening = ChooseMetric(package.Listening, local.Listening);
            package.Shadowing = ChooseMetric(package.Shadowing, local.Shadowing);
            package.UpdatedAt = package.UpdatedAt >= local.UpdatedAt ? package.UpdatedAt : local.UpdatedAt;
        }

        return merged.Values
            .OrderBy(state => state.CueIndex)
            .ToList();
    }

    private static void MergeCoffeeLearningRegistration(
        SubtitleCueLearningState target,
        SubtitleCueLearningState candidate)
    {
        var useCandidate = candidate.CoffeeLearningRegisteredAt is not null
            && (target.CoffeeLearningRegisteredAt is null
                || candidate.CoffeeLearningRegisteredAt > target.CoffeeLearningRegisteredAt);
        if (useCandidate)
        {
            target.CoffeeLearningRegisteredAt = candidate.CoffeeLearningRegisteredAt;
            target.CoffeeLearningWordId = candidate.CoffeeLearningWordId;
            target.CoffeeLearningDeckId = candidate.CoffeeLearningDeckId;
            return;
        }

        target.CoffeeLearningRegisteredAt ??= candidate.CoffeeLearningRegisteredAt;
        target.CoffeeLearningWordId = string.IsNullOrWhiteSpace(target.CoffeeLearningWordId)
            ? candidate.CoffeeLearningWordId
            : target.CoffeeLearningWordId;
        target.CoffeeLearningDeckId = string.IsNullOrWhiteSpace(target.CoffeeLearningDeckId)
            ? candidate.CoffeeLearningDeckId
            : target.CoffeeLearningDeckId;
    }

    private static string GetLearningStateKey(SubtitleCueLearningState state)
    {
        return !string.IsNullOrWhiteSpace(state.CueId)
            ? state.CueId
            : $"index:{state.CueIndex}";
    }

    private static SubtitleCueLearningState NormalizeLearningState(SubtitleCueLearningState state)
    {
        state.Tags ??= [];
        state.Listening ??= new CuePracticeMetric();
        state.Shadowing ??= new CuePracticeMetric();
        if (state.UpdatedAt == default)
        {
            state.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return state;
    }

    private static string? ChooseLocalText(
        string? packageValue,
        string? localValue,
        DateTimeOffset packageUpdatedAt,
        DateTimeOffset localUpdatedAt)
    {
        if (string.IsNullOrWhiteSpace(localValue))
        {
            return packageValue;
        }

        if (string.IsNullOrWhiteSpace(packageValue))
        {
            return localValue;
        }

        return localUpdatedAt >= packageUpdatedAt ? localValue : packageValue;
    }

    private static CuePracticeMetric ChooseMetric(CuePracticeMetric packageMetric, CuePracticeMetric localMetric)
    {
        packageMetric ??= new CuePracticeMetric();
        localMetric ??= new CuePracticeMetric();
        return localMetric.AttemptCount >= packageMetric.AttemptCount ? localMetric : packageMetric;
    }

    private static void RefreshMovieSceneMarkers(Movie movie)
    {
        var track = movie.SubtitleTracks.FirstOrDefault(candidate =>
                candidate.Role == SubtitleTrackRole.LearningTarget && candidate.Cues.Count > 0)
            ?? movie.SubtitleTracks.FirstOrDefault(candidate => candidate.Cues.Count > 0);
        movie.SceneMarkers = track is null ? [] : SubtitleSceneFactory.CreateSceneMarkers(track);
    }

    private static DateTimeOffset? FromUnixMilliseconds(long? value)
    {
        return value is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(value.Value);
    }

    private async Task ApplySidecarThumbnailAsync(
        Movie movie,
        CoffeeMovieSidecar sidecar,
        CancellationToken cancellationToken)
    {
        movie.Video.ThumbnailTimestampSeconds = sidecar.Video.ThumbnailTimestampSeconds;

        if (string.IsNullOrWhiteSpace(sidecar.Video.ThumbnailDataBase64))
        {
            return;
        }

        try
        {
            var bytes = Convert.FromBase64String(sidecar.Video.ThumbnailDataBase64);
            if (bytes.Length == 0)
            {
                return;
            }

            var thumbnailPath = GetThumbnailCachePath(movie.Id, sidecar.Video.ThumbnailFileName);
            Directory.CreateDirectory(_paths.ThumbnailCachePath);
            await File.WriteAllBytesAsync(thumbnailPath, bytes, cancellationToken);
            movie.Video.ThumbnailPath = thumbnailPath;
        }
        catch (FormatException)
        {
            // A bad thumbnail payload should not block metadata sync or video playback.
        }
    }

    private string GetThumbnailCachePath(string movieId, string? sourceFileName)
    {
        var extension = Path.GetExtension(sourceFileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".jpg";
        }

        return Path.Combine(_paths.ThumbnailCachePath, $"{SanitizeFileName(movieId)}{extension}");
    }

}

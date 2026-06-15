using System.Text;
using System.IO.Compression;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Reader.Models;
using CoffeeMovie.Storage.Models;
using CoffeeMovie.Storage.Services;

namespace CoffeeMovie.Reader.Services;

public sealed class ReaderLibraryService
{
    private readonly CoffeeMoviePaths _paths;
    private readonly MovieLibraryStore _libraryStore;
    private readonly MovieCacheStore _cacheStore;
    private readonly CoffeeMoviePackageService _packageService = new();

    public ReaderLibraryService()
    {
        _paths = new CoffeeMoviePaths(FileSystem.AppDataDirectory, FileSystem.CacheDirectory);
        _paths.EnsureCreated();
        _libraryStore = new MovieLibraryStore(_paths);
        _cacheStore = new MovieCacheStore(_paths);
    }

    public async Task<IReadOnlyList<Movie>> LoadMoviesAsync(CancellationToken cancellationToken = default)
    {
        var library = await _libraryStore.LoadAsync(cancellationToken);
        return library.Movies
            .OrderBy(movie => string.IsNullOrWhiteSpace(movie.SeriesTitle) ? movie.Title : movie.SeriesTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(movie => movie.SeasonNumber ?? int.MaxValue)
            .ThenBy(movie => movie.EpisodeNumber ?? int.MaxValue)
            .ThenByDescending(movie => movie.UpdatedAt)
            .ToList();
    }

    public async Task<Movie?> GetMovieAsync(string movieId, CancellationToken cancellationToken = default)
    {
        var library = await _libraryStore.LoadAsync(cancellationToken);
        return library.Movies.FirstOrDefault(movie => string.Equals(movie.Id, movieId, StringComparison.Ordinal));
    }

    public Task SaveMovieAsync(Movie movie, CancellationToken cancellationToken = default)
    {
        return _libraryStore.UpsertMovieAsync(movie, cancellationToken);
    }

    public async Task<Movie> ImportVideoAsync(FileResult file, CancellationToken cancellationToken = default)
    {
        var movieId = MovieId.New();
        var safeFileName = SanitizeFileName(file.FileName);
        var movieDirectory = _paths.GetMovieVideoDirectory(movieId);
        Directory.CreateDirectory(movieDirectory);
        var targetPath = EnsureUniquePath(Path.Combine(movieDirectory, safeFileName));

        await using (var input = await file.OpenReadAsync())
        await using (var output = File.Create(targetPath))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        var fileInfo = new FileInfo(targetPath);
        var movie = new Movie
        {
            Id = movieId,
            Title = Path.GetFileNameWithoutExtension(file.FileName),
            Video = new VideoAsset
            {
                SourceKind = VideoSourceKind.LocalFile,
                SourceUri = file.FullPath ?? file.FileName,
                SourceKey = $"local:{movieId}",
                FileName = safeFileName,
                ContentType = GuessVideoContentType(safeFileName, file.ContentType),
                SizeBytes = fileInfo.Length,
                ModifiedAt = fileInfo.LastWriteTimeUtc,
                CachePath = targetPath
            }
        };

        await _libraryStore.UpsertMovieAsync(movie, cancellationToken);
        return movie;
    }

    public async Task<SubtitleTrack> ImportSubtitleAsync(
        Movie movie,
        FileResult file,
        CancellationToken cancellationToken = default)
    {
        var safeFileName = SanitizeFileName(file.FileName);
        var subtitleDirectory = _paths.GetMovieSubtitleDirectory(movie.Id);
        Directory.CreateDirectory(subtitleDirectory);

        string content;
        await using (var input = await file.OpenReadAsync())
        using (var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            content = await reader.ReadToEndAsync(cancellationToken);
        }

        var originalPath = EnsureUniquePath(Path.Combine(subtitleDirectory, safeFileName));
        await File.WriteAllTextAsync(originalPath, content, Encoding.UTF8, cancellationToken);

        var document = SubtitleParser.Parse(content, file.FileName);
        if (document.Cues.Count == 0)
        {
            throw new InvalidOperationException("字幕キューが見つかりませんでした。SRT または WebVTT の形式を確認してください。");
        }

        var vttPath = Path.Combine(subtitleDirectory, Path.GetFileNameWithoutExtension(safeFileName) + ".vtt");
        await File.WriteAllTextAsync(vttPath, SubtitleParser.ToWebVtt(document.Cues), Encoding.UTF8, cancellationToken);

        var metadata = SubtitleFileMetadataService.Infer(file.FileName);
        var track = new SubtitleTrack
        {
            Label = metadata.Label,
            Language = metadata.Language,
            Role = metadata.Role,
            GroupKey = metadata.GroupKey,
            Format = document.Format,
            SourceFileName = file.FileName,
            LocalPath = originalPath,
            VttCachePath = vttPath,
            CueCount = document.Cues.Count,
            Cues = document.Cues
        };

        movie.SubtitleTracks.RemoveAll(existing =>
            string.Equals(existing.SourceFileName, track.SourceFileName, StringComparison.OrdinalIgnoreCase));
        movie.SubtitleTracks.Add(track);
        movie.SceneMarkers = SubtitleSceneFactory.CreateSceneMarkers(track);
        await _libraryStore.UpsertMovieAsync(movie, cancellationToken);
        return track;
    }

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
        var movie = CreateMovieFromSidecar(manifest, package, existing);

        await ExtractPackageFilesAsync(packagePath, manifest, movie, cancellationToken);
        await ApplySidecarThumbnailAsync(movie, manifest, cancellationToken);
        movie.Video.SourceKind = VideoSourceKind.GoogleDrive;
        movie.Video.SourceUri = package.ContentUri;
        movie.Video.SourceKey = package.ContentUri;
        movie.SourcePackageUri = package.ContentUri;
        movie.SourcePackageName = package.FileName;
        movie.SourcePackageLastModified = package.LastModified;
        movie.SourcePackageSize = package.Size;
        movie.SourceMovieUpdatedAt = manifest.Movie.UpdatedAt;
        movie.SourceContentFingerprint = manifest.ContentFingerprint;
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
                SourceFingerprint = manifest.ContentFingerprint
            }, cancellationToken);
        }

        return movie;
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
        movie.SourceMovieUpdatedAt = sidecar.Movie.UpdatedAt;
        movie.SourceContentFingerprint = sidecar.ContentFingerprint;
    }

    private async Task ExtractPackageFilesAsync(
        string packagePath,
        CoffeeMovieSidecar manifest,
        Movie movie,
        CancellationToken cancellationToken)
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
            package.Listening = ChooseMetric(package.Listening, local.Listening);
            package.Shadowing = ChooseMetric(package.Shadowing, local.Shadowing);
            package.UpdatedAt = package.UpdatedAt >= local.UpdatedAt ? package.UpdatedAt : local.UpdatedAt;
        }

        return merged.Values
            .OrderBy(state => state.CueIndex)
            .ToList();
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

    private static string EnsureUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 2; index < 1000; index++)
        {
            var candidate = Path.Combine(directory, $"{name}-{index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"保存先ファイル名を決定できませんでした: {path}");
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(fileName.Length);
        foreach (var character in fileName)
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
        }

        var sanitized = builder.ToString().Trim();
        return sanitized.Length == 0 ? "movie" : sanitized;
    }

    private static string GuessVideoContentType(string fileName, string? pickerContentType)
    {
        if (!string.IsNullOrWhiteSpace(pickerContentType))
        {
            return pickerContentType;
        }

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".m4v" => "video/x-m4v",
            ".mkv" => "video/x-matroska",
            _ => "video/mp4"
        };
    }
}


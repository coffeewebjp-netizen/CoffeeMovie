using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Storage.Models;

namespace CoffeeMovie.Storage.Services;

public sealed class CoffeeMoviePackageService
{
    private const int SchemaVersion = 1;
    private const string ManifestEntryName = "manifest.json";
    private const string PackageExtension = ".coffeemovie";
    private const string SidecarFileSuffix = ".coffeemovie.json";

    public async Task<CoffeeMoviePackageExportResult> ExportReaderPackageAsync(
        Movie movie,
        string outputDirectory,
        IProgress<CoffeeMoviePackageExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(movie.Video.CachePath) || !File.Exists(movie.Video.CachePath))
        {
            throw new InvalidOperationException("書き出す動画キャッシュが見つかりません。");
        }

        Directory.CreateDirectory(outputDirectory);

        var outputPath = Path.Combine(
            outputDirectory,
            $"{CreateSafeFileName(movie.Title)}_{GetShortMovieId(movie.Id)}{PackageExtension}");
        var sidecarPath = GetReaderPackageSidecarPath(outputPath);
        var manifest = CoffeeMovieSidecarService.Create(movie);
        manifest.SchemaVersion = SchemaVersion;
        manifest.PackageType = "reader";
        manifest.Video.PackagePath = $"video/{CreateSafeFileName(movie.Video.FileName)}";
        manifest.Video.ThumbnailPackagePath = CreateThumbnailPackagePath(movie.Video.ThumbnailPath);

        var existingSidecar = await TryReadSidecarAsync(sidecarPath, cancellationToken);
        if (File.Exists(outputPath)
            && existingSidecar is not null
            && string.Equals(existingSidecar.ContentFingerprint, manifest.ContentFingerprint, StringComparison.Ordinal)
            && HasCurrentThumbnailPayload(existingSidecar, manifest))
        {
            var packageSize = new FileInfo(outputPath).Length;
            ReportProgress(progress, "差分なし", packageSize, Math.Max(1, packageSize));
            return new CoffeeMoviePackageExportResult(
                outputPath,
                packageSize,
                sidecarPath,
                movie.Id,
                GetShortMovieId(movie.Id),
                movie.Title)
            {
                Skipped = true,
                ContentFingerprint = manifest.ContentFingerprint,
                ExportedAt = existingSidecar.ExportedAt
            };
        }

        var tempPath = outputPath + ".tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        var totalBytes = CalculateExportTotalBytes(movie);
        var writtenBytes = 0L;
        ReportProgress(progress, "パッケージを準備中", writtenBytes, totalBytes);

        await using (var stream = File.Create(tempPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            await AddFileAsync(
                archive,
                movie.Video.CachePath,
                manifest.Video.PackagePath,
                CompressionLevel.NoCompression,
                "動画を書き出し中",
                progress,
                totalBytes,
                value => writtenBytes += value,
                () => writtenBytes,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(movie.Video.ThumbnailPath)
                && File.Exists(movie.Video.ThumbnailPath)
                && !string.IsNullOrWhiteSpace(manifest.Video.ThumbnailPackagePath))
            {
                await AddFileAsync(
                    archive,
                    movie.Video.ThumbnailPath,
                    manifest.Video.ThumbnailPackagePath,
                    CompressionLevel.Optimal,
                    "サムネイルを書き出し中",
                    progress,
                    totalBytes,
                    value => writtenBytes += value,
                    () => writtenBytes,
                    cancellationToken);
            }

            foreach (var track in movie.SubtitleTracks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var packageSubtitle = manifest.Subtitles.FirstOrDefault(item =>
                    string.Equals(item.Id, track.Id, StringComparison.Ordinal));
                if (packageSubtitle is null)
                {
                    continue;
                }

                packageSubtitle.PackagePath = await AddSubtitleFileAsync(
                    archive,
                    track.Id,
                    track.LocalPath,
                    track.SourceFileName,
                    progress,
                    totalBytes,
                    value => writtenBytes += value,
                    () => writtenBytes,
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(track.VttCachePath)
                    && File.Exists(track.VttCachePath)
                    && !string.Equals(track.VttCachePath, track.LocalPath, StringComparison.OrdinalIgnoreCase))
                {
                    packageSubtitle.VttPackagePath = await AddSubtitleFileAsync(
                        archive,
                        track.Id,
                        track.VttCachePath,
                        "track.vtt",
                        progress,
                        totalBytes,
                        value => writtenBytes += value,
                        () => writtenBytes,
                        cancellationToken);
                }
            }

            ReportProgress(progress, "メタデータを書き出し中", writtenBytes, totalBytes);
            var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
            await using var manifestStream = manifestEntry.Open();
            await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonStoreOptions.Default, cancellationToken);
        }

        File.Move(tempPath, outputPath, overwrite: true);
        var exportedPackageSize = new FileInfo(outputPath).Length;
        var sidecar = CoffeeMovieSidecarService.Create(
            movie,
            Path.GetFileName(outputPath),
            exportedPackageSize,
            manifest.ContentFingerprint);
        sidecar.PackageType = "reader-sidecar";
        sidecar.Video.PackagePath = manifest.Video.PackagePath;
        sidecar.Video.ThumbnailPackagePath = manifest.Video.ThumbnailPackagePath;
        foreach (var subtitle in sidecar.Subtitles)
        {
            var manifestSubtitle = manifest.Subtitles.FirstOrDefault(item =>
                string.Equals(item.Id, subtitle.Id, StringComparison.Ordinal));
            subtitle.PackagePath = manifestSubtitle?.PackagePath;
            subtitle.VttPackagePath = manifestSubtitle?.VttPackagePath;
        }

        ReportProgress(progress, "サイドカーを書き出し中", writtenBytes, totalBytes);
        await WriteReaderPackageSidecarAsync(sidecarPath, sidecar, cancellationToken);
        ReportProgress(progress, "書き出し完了", totalBytes, totalBytes);
        return new CoffeeMoviePackageExportResult(
            outputPath,
            exportedPackageSize,
            sidecarPath,
            movie.Id,
            GetShortMovieId(movie.Id),
            movie.Title)
        {
            ContentFingerprint = manifest.ContentFingerprint,
            ExportedAt = sidecar.ExportedAt
        };
    }

    public async Task<CoffeeMoviePackageExportResult> ExportReaderMetadataAsync(
        Movie movie,
        string outputDirectory,
        IProgress<CoffeeMoviePackageExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var shortMovieId = GetShortMovieId(movie.Id);
        var expectedPackagePath = Path.Combine(
            outputDirectory,
            $"{CreateSafeFileName(movie.Title)}_{shortMovieId}{PackageExtension}");
        var packagePath = File.Exists(expectedPackagePath)
            ? expectedPackagePath
            : Directory.EnumerateFiles(outputDirectory, $"*{PackageExtension}", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => Path.GetFileName(path).EndsWith(
                    $"_{shortMovieId}{PackageExtension}",
                    StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
        {
            return await ExportReaderPackageAsync(movie, outputDirectory, progress, cancellationToken);
        }

        var packageManifest = await ReadReaderPackageManifestAsync(packagePath, cancellationToken);
        if (!IsSameVideoAsset(movie.Video, packageManifest.Video))
        {
            return await ExportReaderPackageAsync(movie, outputDirectory, progress, cancellationToken);
        }

        var packageInfo = new FileInfo(packagePath);
        var sidecarPath = GetReaderPackageSidecarPath(packagePath);
        var sidecar = CoffeeMovieSidecarService.Create(
            movie,
            packageInfo.Name,
            packageInfo.Length);
        sidecar.PackageType = "reader-sidecar";
        CopyPackageEntryPaths(sidecar, packageManifest);

        var existingSidecar = await TryReadSidecarAsync(sidecarPath, cancellationToken);
        if (existingSidecar is not null
            && string.Equals(existingSidecar.ContentFingerprint, sidecar.ContentFingerprint, StringComparison.Ordinal)
            && HasCurrentThumbnailPayload(existingSidecar, sidecar))
        {
            ReportProgress(progress, "差分なし", 1, 1);
            return new CoffeeMoviePackageExportResult(
                packagePath,
                packageInfo.Length,
                sidecarPath,
                movie.Id,
                shortMovieId,
                movie.Title)
            {
                Skipped = true,
                MetadataOnly = true,
                ContentFingerprint = sidecar.ContentFingerprint,
                ExportedAt = existingSidecar.ExportedAt
            };
        }

        ReportProgress(progress, "メタデータを更新中", 0, 1);
        await WriteReaderPackageSidecarAsync(sidecarPath, sidecar, cancellationToken);
        ReportProgress(progress, "メタデータ更新完了", 1, 1);
        return new CoffeeMoviePackageExportResult(
            packagePath,
            packageInfo.Length,
            sidecarPath,
            movie.Id,
            shortMovieId,
            movie.Title)
        {
            MetadataOnly = true,
            ContentFingerprint = sidecar.ContentFingerprint,
            ExportedAt = sidecar.ExportedAt
        };
    }

    public async Task<CoffeeMovieSidecar> ReadReaderPackageSidecarAsync(
        string sidecarPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sidecarPath))
        {
            throw new FileNotFoundException("CoffeeMovie サイドカーが見つかりません。", sidecarPath);
        }

        await using var stream = File.OpenRead(sidecarPath);
        var sidecar = await JsonSerializer.DeserializeAsync<CoffeeMovieSidecar>(
            stream,
            JsonStoreOptions.Default,
            cancellationToken)
            ?? throw new InvalidOperationException("CoffeeMovie サイドカーを読み込めませんでした。");

        ValidateSidecar(sidecar);
        return sidecar;
    }

    public async Task<CoffeeMovieSidecar> ReadReaderPackageManifestAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("CoffeeMovie パッケージが見つかりません。", packagePath);
        }

        await using var packageStream = File.OpenRead(packagePath);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(ManifestEntryName)
            ?? throw new InvalidOperationException("manifest.json が見つかりません。");

        await using var stream = entry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<CoffeeMovieSidecar>(
            stream,
            JsonStoreOptions.Default,
            cancellationToken)
            ?? throw new InvalidOperationException("manifest.json を読み込めませんでした。");

        ValidateSidecar(manifest);
        return manifest;
    }

    public async Task<Movie> ImportReaderPackageAsync(
        string packagePath,
        CoffeeMoviePaths paths,
        Movie? existing = null,
        string? sourcePackageUri = null,
        string? sourcePackageName = null,
        long? sourcePackageLastModified = null,
        long? sourcePackageSize = null,
        int maxSceneMarkers = 300,
        CoffeeMovieSidecar? authoritativeSidecar = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var packageManifest = await ReadReaderPackageManifestAsync(packagePath, cancellationToken);
        var metadata = authoritativeSidecar ?? packageManifest;
        var packageInfo = new FileInfo(packagePath);
        var packageUri = string.IsNullOrWhiteSpace(sourcePackageUri) ? packagePath : sourcePackageUri;
        var packageName = string.IsNullOrWhiteSpace(sourcePackageName) ? Path.GetFileName(packagePath) : sourcePackageName;
        var packageLastModified = sourcePackageLastModified
            ?? new DateTimeOffset(packageInfo.LastWriteTimeUtc).ToUnixTimeMilliseconds();
        var packageSize = sourcePackageSize ?? packageInfo.Length;

        var movie = CreateMovieFromSidecar(
            metadata,
            existing,
            packageUri,
            packageName,
            packageLastModified,
            packageSize);

        await ExtractPackageFilesAsync(packagePath, paths, packageManifest, movie, cancellationToken);
        await ApplySidecarThumbnailAsync(paths, movie, metadata, cancellationToken);
        movie.Video.SourceKind = VideoSourceKind.GoogleDrive;
        movie.Video.SourceUri = packageUri;
        movie.Video.SourceKey = packageUri;
        movie.SourcePackageUri = packageUri;
        movie.SourcePackageName = packageName;
        movie.SourcePackageLastModified = packageLastModified;
        movie.SourcePackageSize = packageSize;
        movie.SourceMovieUpdatedAt = metadata.Movie.UpdatedAt;
        movie.SourceContentFingerprint = metadata.ContentFingerprint;
        RefreshMovieSceneMarkers(movie, maxSceneMarkers);
        return movie;
    }

    public static string GetReaderPackageSidecarPath(string packagePath)
    {
        return packagePath + ".json";
    }

    public static bool IsReaderPackageFileName(string fileName)
    {
        return fileName.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsReaderPackageSidecarFileName(string fileName)
    {
        return fileName.EndsWith(SidecarFileSuffix, StringComparison.OrdinalIgnoreCase);
    }

    public static string GetPackageFileNameForSidecarName(string sidecarFileName)
    {
        return IsReaderPackageSidecarFileName(sidecarFileName)
            ? sidecarFileName[..^".json".Length]
            : sidecarFileName;
    }

    private static async Task<string?> AddSubtitleFileAsync(
        ZipArchive archive,
        string trackId,
        string? path,
        string fallbackFileName,
        IProgress<CoffeeMoviePackageExportProgress>? progress,
        long totalBytes,
        Action<long> addWrittenBytes,
        Func<long> getWrittenBytes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var fileName = CreateSafeFileName(string.IsNullOrWhiteSpace(Path.GetFileName(path))
            ? fallbackFileName
            : Path.GetFileName(path));
        var entryName = $"subtitles/{CreateSafeFileName(trackId)}/{fileName}";
        await AddFileAsync(
            archive,
            path,
            entryName,
            CompressionLevel.Optimal,
            "字幕を書き出し中",
            progress,
            totalBytes,
            addWrittenBytes,
            getWrittenBytes,
            cancellationToken);
        return entryName;
    }

    private static async Task AddFileAsync(
        ZipArchive archive,
        string sourcePath,
        string entryName,
        CompressionLevel compressionLevel,
        string stage,
        IProgress<CoffeeMoviePackageExportProgress>? progress,
        long totalBytes,
        Action<long> addWrittenBytes,
        Func<long> getWrittenBytes,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, compressionLevel);
        await using var input = File.OpenRead(sourcePath);
        await using var output = entry.Open();
        var buffer = new byte[1024 * 256];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            addWrittenBytes(read);
            ReportProgress(progress, stage, getWrittenBytes(), totalBytes);
        }
    }

    private static long CalculateExportTotalBytes(Movie movie)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddPath(movie.Video.CachePath);
        AddPath(movie.Video.ThumbnailPath);
        foreach (var track in movie.SubtitleTracks)
        {
            AddPath(track.LocalPath);
            if (!string.Equals(track.VttCachePath, track.LocalPath, StringComparison.OrdinalIgnoreCase))
            {
                AddPath(track.VttCachePath);
            }
        }

        return paths.Sum(path => new FileInfo(path).Length);

        void AddPath(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                paths.Add(path);
            }
        }
    }

    private static string? CreateThumbnailPackagePath(string? thumbnailPath)
    {
        if (string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath))
        {
            return null;
        }

        var fileName = CreateSafeFileName(Path.GetFileName(thumbnailPath));
        return $"thumbnails/{fileName}";
    }

    private static bool IsSameVideoAsset(VideoAsset video, CoffeeMovieSidecarVideo packagedVideo)
    {
        if (!string.IsNullOrWhiteSpace(video.ContentFingerprint)
            || !string.IsNullOrWhiteSpace(packagedVideo.ContentFingerprint))
        {
            return string.Equals(video.ContentFingerprint, packagedVideo.ContentFingerprint, StringComparison.Ordinal);
        }

        return string.Equals(video.FileName, packagedVideo.FileName, StringComparison.OrdinalIgnoreCase)
            && video.SizeBytes == packagedVideo.SizeBytes
            && Nullable.Equals(video.ModifiedAt, packagedVideo.ModifiedAt);
    }

    private static void CopyPackageEntryPaths(CoffeeMovieSidecar target, CoffeeMovieSidecar packageManifest)
    {
        target.Video.PackagePath = packageManifest.Video.PackagePath;
        target.Video.ThumbnailPackagePath = packageManifest.Video.ThumbnailPackagePath;
        foreach (var subtitle in target.Subtitles)
        {
            var packagedSubtitle = packageManifest.Subtitles.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, subtitle.Id, StringComparison.Ordinal))
                ?? packageManifest.Subtitles.FirstOrDefault(candidate =>
                    string.Equals(candidate.SourceFileName, subtitle.SourceFileName, StringComparison.OrdinalIgnoreCase));
            subtitle.PackagePath = packagedSubtitle?.PackagePath;
            subtitle.VttPackagePath = packagedSubtitle?.VttPackagePath;
        }
    }
    private static bool HasCurrentThumbnailPayload(CoffeeMovieSidecar existingSidecar, CoffeeMovieSidecar manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Video.ThumbnailFileName))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(existingSidecar.Video.ThumbnailDataBase64)
            && string.Equals(existingSidecar.Video.ThumbnailFileName, manifest.Video.ThumbnailFileName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existingSidecar.Video.ThumbnailContentType, manifest.Video.ThumbnailContentType, StringComparison.OrdinalIgnoreCase);
    }

    private static void ReportProgress(
        IProgress<CoffeeMoviePackageExportProgress>? progress,
        string stage,
        long bytesWritten,
        long totalBytes)
    {
        if (progress is null)
        {
            return;
        }

        var safeTotal = Math.Max(1, totalBytes);
        progress.Report(new CoffeeMoviePackageExportProgress(stage, Math.Clamp(bytesWritten, 0, safeTotal), safeTotal));
    }

    private static async Task ExtractPackageFilesAsync(
        string packagePath,
        CoffeeMoviePaths paths,
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
        var videoDirectory = paths.GetMovieVideoDirectory(movie.Id);
        Directory.CreateDirectory(videoDirectory);
        var videoFileName = CreateSafeFileName(string.IsNullOrWhiteSpace(movie.Video.FileName)
            ? videoEntry.Name
            : movie.Video.FileName);
        var videoPath = Path.Combine(videoDirectory, videoFileName);
        await ExtractEntryAsync(videoEntry, videoPath, cancellationToken);
        movie.Video.FileName = videoFileName;
        movie.Video.CachePath = videoPath;
        movie.Video.SizeBytes = new FileInfo(videoPath).Length;
        movie.Video.ContentType = string.IsNullOrWhiteSpace(movie.Video.ContentType)
            ? GuessVideoContentType(videoFileName)
            : movie.Video.ContentType;

        if (!string.IsNullOrWhiteSpace(manifest.Video.ThumbnailPackagePath)
            && archive.GetEntry(manifest.Video.ThumbnailPackagePath) is { } thumbnailEntry)
        {
            var thumbnailPath = GetThumbnailCachePath(paths, movie.Id, thumbnailEntry.Name);
            await ExtractEntryAsync(thumbnailEntry, thumbnailPath, cancellationToken);
            movie.Video.ThumbnailPath = thumbnailPath;
        }

        var subtitleDirectory = paths.GetMovieSubtitleDirectory(movie.Id);
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
                var subtitleFileName = CreateSafeFileName(string.IsNullOrWhiteSpace(track.SourceFileName)
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
                    Path.GetFileNameWithoutExtension(CreateSafeFileName(track.SourceFileName)) + ".vtt");
                await ExtractEntryAsync(vttEntry, vttPath, cancellationToken);
                track.VttCachePath = vttPath;
            }
            else if (track.Cues.Count > 0)
            {
                var vttPath = Path.Combine(
                    subtitleDirectory,
                    Path.GetFileNameWithoutExtension(CreateSafeFileName(track.SourceFileName)) + ".vtt");
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
        Movie? existing,
        string sourcePackageUri,
        string sourcePackageName,
        long? sourcePackageLastModified,
        long? sourcePackageSize)
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
            SourcePackageUri = sourcePackageUri,
            SourcePackageName = sourcePackageName,
            SourcePackageLastModified = sourcePackageLastModified,
            SourcePackageSize = sourcePackageSize,
            SourceMovieUpdatedAt = sidecar.Movie.UpdatedAt,
            SourceContentFingerprint = sidecar.ContentFingerprint,
            CreatedAt = sidecar.Movie.CreatedAt == default ? DateTimeOffset.UtcNow : sidecar.Movie.CreatedAt,
            UpdatedAt = sidecar.Movie.UpdatedAt == default ? DateTimeOffset.UtcNow : sidecar.Movie.UpdatedAt,
            Video = new VideoAsset
            {
                SourceKind = VideoSourceKind.GoogleDrive,
                SourceUri = sourcePackageUri,
                SourceKey = sourcePackageUri,
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

    private static void RefreshMovieSceneMarkers(Movie movie, int maxSceneMarkers)
    {
        var track = movie.SubtitleTracks.FirstOrDefault(candidate =>
                candidate.Role == SubtitleTrackRole.LearningTarget && candidate.Cues.Count > 0)
            ?? movie.SubtitleTracks.FirstOrDefault(candidate => candidate.Cues.Count > 0);
        movie.SceneMarkers = track is null ? [] : SubtitleSceneFactory.CreateSceneMarkers(track, maxSceneMarkers);
    }

    private static async Task ApplySidecarThumbnailAsync(
        CoffeeMoviePaths paths,
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

            var thumbnailPath = GetThumbnailCachePath(paths, movie.Id, sidecar.Video.ThumbnailFileName);
            Directory.CreateDirectory(paths.ThumbnailCachePath);
            await File.WriteAllBytesAsync(thumbnailPath, bytes, cancellationToken);
            movie.Video.ThumbnailPath = thumbnailPath;
        }
        catch (FormatException)
        {
            // Bad thumbnail payloads should not block package imports.
        }
    }

    private static string GetThumbnailCachePath(CoffeeMoviePaths paths, string movieId, string? sourceFileName)
    {
        var extension = Path.GetExtension(sourceFileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".jpg";
        }

        return Path.Combine(paths.ThumbnailCachePath, $"{CreateSafeFileName(movieId)}{extension}");
    }

    private static async Task WriteReaderPackageSidecarAsync(
        string sidecarPath,
        CoffeeMovieSidecar sidecar,
        CancellationToken cancellationToken)
    {
        var tempPath = sidecarPath + ".tmp";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, sidecar, JsonStoreOptions.Default, cancellationToken);
        }

        File.Move(tempPath, sidecarPath, overwrite: true);
    }

    private static async Task<CoffeeMovieSidecar?> TryReadSidecarAsync(
        string sidecarPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sidecarPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(sidecarPath);
            return await JsonSerializer.DeserializeAsync<CoffeeMovieSidecar>(
                stream,
                JsonStoreOptions.Default,
                cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static void ValidateSidecar(CoffeeMovieSidecar sidecar)
    {
        if (sidecar.SchemaVersion != SchemaVersion)
        {
            throw new InvalidOperationException($"未対応の CoffeeMovie 形式です: schemaVersion={sidecar.SchemaVersion}");
        }

        if (!string.Equals(sidecar.PackageType, "reader-sidecar", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(sidecar.PackageType, "reader", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Reader向けCoffeeMovieサイドカーではありません。");
        }

        if (string.IsNullOrWhiteSpace(sidecar.SourceMovieId) || string.IsNullOrWhiteSpace(sidecar.Movie.Title))
        {
            throw new InvalidOperationException("CoffeeMovieサイドカーの動画情報が不足しています。");
        }
    }

    private static string GetShortMovieId(string movieId)
    {
        return string.IsNullOrWhiteSpace(movieId)
            ? "movie"
            : movieId[..Math.Min(8, movieId.Length)];
    }

    private static string CreateSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(safe) ? "movie" : safe;
    }

    private static string GuessVideoContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".m4v" => "video/x-m4v",
            ".mkv" => "video/x-matroska",
            ".avi" => "video/x-msvideo",
            _ => "video/mp4"
        };
    }
}

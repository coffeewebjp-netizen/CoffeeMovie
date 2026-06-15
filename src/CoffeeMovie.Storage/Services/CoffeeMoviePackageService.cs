using System.IO.Compression;
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
}

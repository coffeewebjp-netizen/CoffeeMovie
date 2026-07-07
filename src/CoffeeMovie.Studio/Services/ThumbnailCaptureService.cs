using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace CoffeeMovie.Studio.Services;

public static class ThumbnailCaptureService
{
    public static string GetThumbnailPath(string thumbnailCachePath, string movieId)
    {
        Directory.CreateDirectory(thumbnailCachePath);
        return GetUniqueThumbnailPath(thumbnailCachePath, SanitizeFileName(movieId));
    }
    private static string GetUniqueThumbnailPath(string thumbnailCachePath, string safeMovieId)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        for (var index = 0; index < 100; index++)
        {
            var suffix = index == 0 ? timestamp : $"{timestamp}-{index}";
            var candidate = Path.Combine(thumbnailCachePath, $"{safeMovieId}-{suffix}.jpg");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(thumbnailCachePath, $"{safeMovieId}-{Guid.NewGuid():N}.jpg");
    }

    public static async Task CaptureAsync(string videoPath, string outputPath, TimeSpan position)
    {
        var ffmpegPath = ResolveFfmpegPath();
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = outputPath + $".{Guid.NewGuid():N}.tmp.jpg";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-ss");
        startInfo.ArgumentList.Add(Math.Max(0, position.TotalSeconds).ToString("0.###", CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(videoPath);
        startInfo.ArgumentList.Add("-frames:v");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add("scale=640:-2:force_original_aspect_ratio=decrease");
        startInfo.ArgumentList.Add("-q:v");
        startInfo.ArgumentList.Add("3");
        startInfo.ArgumentList.Add(tempPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("ffmpegを起動できませんでした。");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw new TimeoutException("ffmpegのサムネイル作成がタイムアウトしました。動画ファイルまたは指定位置を確認してください。");
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0 || !File.Exists(tempPath))
        {
            var message = string.Join(
                Environment.NewLine,
                new[] { error, output }.Where(text => !string.IsNullOrWhiteSpace(text)));
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(message)
                    ? "ffmpegがサムネイルを作成できませんでした。"
                    : message.Trim());
        }

        try
        {
            File.Move(tempPath, outputPath, overwrite: false);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }
    private static string ResolveFfmpegPath()
    {
        foreach (var envName in new[] { "COFFEEMOVIE_FFMPEG_PATH", "FFMPEG_PATH" })
        {
            var configuredPath = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            {
                return configuredPath!;
            }
        }

        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var directory in paths)
        {
            var candidate = Path.Combine(directory, "ffmpeg.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "ffmpeg.exe が見つかりません。PATHにffmpegを追加するか、COFFEEMOVIE_FFMPEG_PATH に ffmpeg.exe のフルパスを設定してください。");
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(fileName.Length);
        foreach (var character in fileName)
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
        }

        var sanitized = builder.ToString().Trim();
        return sanitized.Length == 0 ? "movie" : sanitized;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}

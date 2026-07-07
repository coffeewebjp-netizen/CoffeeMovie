using System.IO;

namespace CoffeeMovie.Studio.Services;

public static class SubtitleGenerationPathService
{
    public static string EnsureFileAvailableInWorkingDirectory(string sourcePath, string workingDirectory)
    {
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var workingFullPath = NormalizeDirectoryPath(workingDirectory);
        var sourceDirectory = Path.GetDirectoryName(sourceFullPath);
        if (sourceDirectory is not null
            && string.Equals(NormalizeDirectoryPath(sourceDirectory), workingFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return sourceFullPath;
        }

        var destinationPath = Path.Combine(workingFullPath, Path.GetFileName(sourceFullPath));
        if (!string.Equals(sourceFullPath, Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourceFullPath, destinationPath, overwrite: true);
        }

        return destinationPath;
    }

    public static string FormatExternalProcessPath(string path, string workingDirectory, bool preferRelativePath)
    {
        if (!preferRelativePath)
        {
            return path;
        }

        var relativePath = Path.GetRelativePath(workingDirectory, path);
        return IsSafeRelativePath(relativePath)
            ? relativePath
            : path;
    }

    public static string FormatExternalProcessDirectory(string path, string workingDirectory, bool preferRelativePath)
    {
        if (!preferRelativePath)
        {
            return path;
        }

        var relativePath = Path.GetRelativePath(workingDirectory, path);
        if (relativePath == ".")
        {
            return ".";
        }

        return IsSafeRelativePath(relativePath)
            ? relativePath
            : path;
    }

    public static void BackupExistingFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileName(path);
        var backupPath = Path.Combine(directory, $"{name}.{DateTime.Now:yyyyMMddHHmmss}.bak");
        File.Move(path, backupPath);
    }

    public static DateTime PrepareGeneratedOutputPath(string path)
    {
        var startedAtUtc = DateTime.UtcNow;
        BackupExistingFile(path);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return startedAtUtc;
    }

    public static void EnsureGeneratedOutputIsFresh(string path, DateTime startedAtUtc, string message)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(message, path);
        }

        var lastWriteUtc = File.GetLastWriteTimeUtc(path);
        if (lastWriteUtc < startedAtUtc.AddSeconds(-2))
        {
            throw new InvalidOperationException(
                $"{message} The existing output was not updated: {path}");
        }
    }

    private static bool IsSafeRelativePath(string path)
    {
        return !Path.IsPathRooted(path)
            && path != ".."
            && !path.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !path.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string NormalizeDirectoryPath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

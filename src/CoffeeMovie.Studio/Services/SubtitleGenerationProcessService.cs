using System.Diagnostics;
using System.IO;
using System.Text;

namespace CoffeeMovie.Studio.Services;

public static class SubtitleGenerationProcessService
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

    public static string ApplyArgumentTemplate(string template, IReadOnlyDictionary<string, string> replacements)
    {
        var result = template;
        foreach (var (key, value) in replacements)
        {
            result = result.Replace("{" + key + "}", value, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    public static ProcessStartInfo CreateTranslationProcessStartInfo(
        string translationCommand,
        IReadOnlyList<string> translationArguments,
        string workingDirectory,
        string codexCommandName,
        string defaultCodexModel,
        string? codexModel = null)
    {
        var fileName = translationCommand;
        var arguments = translationArguments;
        if (IsCodexCommand(translationCommand, codexCommandName))
        {
            fileName = ResolveCodexExecutable();
            arguments = EnsureCodexExecArguments(translationArguments, codexModel, defaultCodexModel);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
            WorkingDirectory = Directory.Exists(workingDirectory)
                ? workingDirectory
                : Environment.CurrentDirectory
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        return startInfo;
    }

    public static bool IsCodexCommand(string command, string codexCommandName)
    {
        return string.Equals(command, codexCommandName, StringComparison.OrdinalIgnoreCase);
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

    public static string FormatProcessCommand(ProcessStartInfo startInfo)
    {
        return string.Join(
            ' ',
            new[] { QuoteCommandPart(startInfo.FileName) }.Concat(startInfo.ArgumentList.Select(QuoteCommandPart)));
    }

    public static List<string> SplitCommandLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var arguments = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;
        foreach (var character in text)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (builder.Length > 0)
                {
                    arguments.Add(builder.ToString());
                    builder.Clear();
                }

                continue;
            }

            builder.Append(character);
        }

        if (builder.Length > 0)
        {
            arguments.Add(builder.ToString());
        }

        return arguments;
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

    private static List<string> EnsureCodexExecArguments(
        IReadOnlyList<string> arguments,
        string? codexModel,
        string defaultCodexModel)
    {
        List<string> result;
        if (arguments.Count > 0
            && string.Equals(arguments[0], "exec", StringComparison.OrdinalIgnoreCase))
        {
            result = arguments.ToList();
        }
        else
        {
            result = ["exec", .. arguments];
        }

        var model = NormalizeOptionalText(codexModel) ?? defaultCodexModel;
        if (!HasCodexModelArgument(result))
        {
            result.Insert(1, model);
            result.Insert(1, "-m");
        }

        return result;
    }

    private static bool HasCodexModelArgument(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (string.Equals(argument, "-m", StringComparison.OrdinalIgnoreCase)
                || string.Equals(argument, "--model", StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith("--model=", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveCodexExecutable()
    {
        var configuredPath = TryReadCodexCliPathFromConfig();
        if (configuredPath is not null)
        {
            return configuredPath;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, "codex.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "codex";
    }

    private static string? TryReadCodexCliPathFromConfig()
    {
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "config.toml");
        if (!File.Exists(configPath))
        {
            return null;
        }

        foreach (var line in File.ReadLines(configPath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("CODEX_CLI_PATH", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            var value = trimmed[(separatorIndex + 1)..].Trim().Trim('"', '\'');
            if (File.Exists(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? NormalizeOptionalText(string? text)
    {
        var normalized = text?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string QuoteCommandPart(string value)
    {
        return value.Any(char.IsWhiteSpace) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;
    }
}

using System.Diagnostics;
using System.IO;
using System.Text;

namespace CoffeeMovie.Studio.Services;

public static class SubtitleGenerationExternalCommandFactory
{
    public static ProcessStartInfo CreateExternalCommandProcessStartInfo(
        string command,
        IReadOnlyList<string> commandArguments,
        string workingDirectory,
        string codexCommandName,
        string defaultCodexModel,
        string? codexModel = null)
    {
        var fileName = command;
        var arguments = commandArguments;
        if (IsCodexCommand(command, codexCommandName))
        {
            fileName = ResolveCodexExecutable();
            arguments = EnsureCodexExecArguments(commandArguments, codexModel, defaultCodexModel);
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

    public static ProcessStartInfo CreateTranslationProcessStartInfo(
        string translationCommand,
        IReadOnlyList<string> translationArguments,
        string workingDirectory,
        string codexCommandName,
        string defaultCodexModel,
        string? codexModel = null)
    {
        return CreateExternalCommandProcessStartInfo(
            translationCommand,
            translationArguments,
            workingDirectory,
            codexCommandName,
            defaultCodexModel,
            codexModel);
    }

    public static bool IsCodexCommand(string command, string codexCommandName)
    {
        return string.Equals(command, codexCommandName, StringComparison.OrdinalIgnoreCase);
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
}

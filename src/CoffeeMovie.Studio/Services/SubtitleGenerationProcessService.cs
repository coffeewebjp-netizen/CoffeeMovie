using System.Diagnostics;

namespace CoffeeMovie.Studio.Services;

public static class SubtitleGenerationProcessService
{
    public static string EnsureFileAvailableInWorkingDirectory(string sourcePath, string workingDirectory)
    {
        return SubtitleGenerationPathService.EnsureFileAvailableInWorkingDirectory(sourcePath, workingDirectory);
    }

    public static string FormatExternalProcessPath(string path, string workingDirectory, bool preferRelativePath)
    {
        return SubtitleGenerationPathService.FormatExternalProcessPath(path, workingDirectory, preferRelativePath);
    }

    public static string FormatExternalProcessDirectory(string path, string workingDirectory, bool preferRelativePath)
    {
        return SubtitleGenerationPathService.FormatExternalProcessDirectory(path, workingDirectory, preferRelativePath);
    }

    public static string ApplyArgumentTemplate(string template, IReadOnlyDictionary<string, string> replacements)
    {
        return SubtitleGenerationCommandLineService.ApplyArgumentTemplate(template, replacements);
    }

    public static ProcessStartInfo CreateExternalCommandProcessStartInfo(
        string command,
        IReadOnlyList<string> commandArguments,
        string workingDirectory,
        string codexCommandName,
        string defaultCodexModel,
        string? codexModel = null)
    {
        return SubtitleGenerationExternalCommandFactory.CreateExternalCommandProcessStartInfo(
            command,
            commandArguments,
            workingDirectory,
            codexCommandName,
            defaultCodexModel,
            codexModel);
    }

    public static ProcessStartInfo CreateTranslationProcessStartInfo(
        string translationCommand,
        IReadOnlyList<string> translationArguments,
        string workingDirectory,
        string codexCommandName,
        string defaultCodexModel,
        string? codexModel = null)
    {
        return SubtitleGenerationExternalCommandFactory.CreateTranslationProcessStartInfo(
            translationCommand,
            translationArguments,
            workingDirectory,
            codexCommandName,
            defaultCodexModel,
            codexModel);
    }

    public static bool IsCodexCommand(string command, string codexCommandName)
    {
        return SubtitleGenerationExternalCommandFactory.IsCodexCommand(command, codexCommandName);
    }

    public static void BackupExistingFile(string path)
    {
        SubtitleGenerationPathService.BackupExistingFile(path);
    }

    public static DateTime PrepareGeneratedOutputPath(string path)
    {
        return SubtitleGenerationPathService.PrepareGeneratedOutputPath(path);
    }

    public static void EnsureGeneratedOutputIsFresh(string path, DateTime startedAtUtc, string message)
    {
        SubtitleGenerationPathService.EnsureGeneratedOutputIsFresh(path, startedAtUtc, message);
    }

    public static string FormatProcessCommand(ProcessStartInfo startInfo)
    {
        return SubtitleGenerationCommandLineService.FormatProcessCommand(startInfo);
    }

    public static List<string> SplitCommandLine(string? text)
    {
        return SubtitleGenerationCommandLineService.SplitCommandLine(text);
    }
}

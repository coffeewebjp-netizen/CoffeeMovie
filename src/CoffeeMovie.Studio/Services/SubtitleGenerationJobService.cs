using System.Diagnostics;
using System.IO;
using System.Text;
using CoffeeMovie.Storage.Services;

namespace CoffeeMovie.Studio.Services;

public sealed class SubtitleGenerationJobService
{
    public async Task<string> GenerateEnglishSubtitleAsync(
        EnglishSubtitleGenerationOptions options,
        Action<string> log)
    {
        Directory.CreateDirectory(options.OutputDirectory);

        var baseName = Path.GetFileNameWithoutExtension(options.VideoPath);
        var generatedSrtPath = Path.Combine(options.OutputDirectory, baseName + ".srt");
        var englishSrtPath = Path.Combine(options.OutputDirectory, baseName + ".en.srt");

        if (File.Exists(englishSrtPath) && !options.Overwrite)
        {
            log($"Existing English subtitle found: {englishSrtPath}");
            return englishSrtPath;
        }

        if (options.Overwrite)
        {
            SubtitleGenerationProcessService.BackupExistingFile(englishSrtPath);
            SubtitleGenerationProcessService.BackupExistingFile(generatedSrtPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = options.PythonCommand,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        foreach (var argument in SubtitleGenerationProcessService.SplitCommandLine(options.PythonArgumentsText))
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add(options.VideoPath);
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(options.Model);
        startInfo.ArgumentList.Add("--language");
        startInfo.ArgumentList.Add(options.Language);
        startInfo.ArgumentList.Add("--output_format");
        startInfo.ArgumentList.Add("srt");
        startInfo.ArgumentList.Add("--output_dir");
        startInfo.ArgumentList.Add(options.OutputDirectory);
        startInfo.ArgumentList.Add("--device");
        startInfo.ArgumentList.Add(options.Device);
        startInfo.ArgumentList.Add("--compute_type");
        startInfo.ArgumentList.Add(options.ComputeType);
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

        log("Command:");
        log(SubtitleGenerationProcessService.FormatProcessCommand(startInfo));
        log("RUNNING: waiting for WhisperX process to finish...");
        await RunExternalProcessAsync(startInfo, "WhisperX", log);

        if (File.Exists(englishSrtPath))
        {
            return englishSrtPath;
        }

        if (File.Exists(generatedSrtPath))
        {
            File.Move(generatedSrtPath, englishSrtPath, overwrite: true);
            log($"Renamed generated SRT: {englishSrtPath}");
            return englishSrtPath;
        }

        throw new FileNotFoundException("WhisperX completed but no SRT file was found.", generatedSrtPath);
    }

    public async Task<string> GenerateJapaneseSubtitleAsync(
        JapaneseSubtitleGenerationOptions options,
        Action<string> log)
    {
        Directory.CreateDirectory(options.OutputDirectory);

        var baseName = Path.GetFileNameWithoutExtension(options.VideoPath);
        var japaneseSrtPath = Path.Combine(options.OutputDirectory, baseName + ".ja.srt");

        if (File.Exists(japaneseSrtPath) && !options.Overwrite)
        {
            log($"Existing Japanese subtitle found: {japaneseSrtPath}");
            return japaneseSrtPath;
        }

        if (options.Overwrite)
        {
            SubtitleGenerationProcessService.BackupExistingFile(japaneseSrtPath);
        }

        var argumentTemplate = options.TranslationArgumentsTemplate;
        if (SubtitleGenerationProcessService.IsCodexCommand(options.TranslationCommand, options.DefaultTranslationCommand)
            && (argumentTemplate.TrimStart().StartsWith("--input", StringComparison.OrdinalIgnoreCase)
                || argumentTemplate.Contains("{notesOutput}", StringComparison.OrdinalIgnoreCase)))
        {
            argumentTemplate = options.DefaultTranslationArguments;
        }

        var useCodexRelativePaths = SubtitleGenerationProcessService.IsCodexCommand(
            options.TranslationCommand,
            options.DefaultTranslationCommand);
        var processEnglishSrtPath = useCodexRelativePaths
            ? SubtitleGenerationProcessService.EnsureFileAvailableInWorkingDirectory(options.EnglishSrtPath, options.OutputDirectory)
            : options.EnglishSrtPath;
        var inputDirectory = Path.GetDirectoryName(processEnglishSrtPath) ?? options.OutputDirectory;

        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["input"] = SubtitleGenerationProcessService.FormatExternalProcessPath(processEnglishSrtPath, options.OutputDirectory, useCodexRelativePaths),
            ["output"] = SubtitleGenerationProcessService.FormatExternalProcessPath(japaneseSrtPath, options.OutputDirectory, useCodexRelativePaths),
            ["inputDir"] = SubtitleGenerationProcessService.FormatExternalProcessDirectory(inputDirectory, options.OutputDirectory, useCodexRelativePaths),
            ["outputDir"] = SubtitleGenerationProcessService.FormatExternalProcessDirectory(options.OutputDirectory, options.OutputDirectory, useCodexRelativePaths),
            ["source"] = options.SourceLanguage,
            ["target"] = options.TargetLanguage,
            ["model"] = options.AiModel,
            ["movie"] = options.VideoPath,
            ["title"] = options.MovieTitle
        };
        var promptText = SubtitleGenerationProcessService.ApplyArgumentTemplate(options.TranslationPromptTemplate, replacements);
        var promptFilePath = Path.Combine(options.OutputDirectory, baseName + ".translation.prompt.md");
        await File.WriteAllTextAsync(promptFilePath, promptText, Encoding.UTF8);
        replacements["promptFile"] = SubtitleGenerationProcessService.FormatExternalProcessPath(promptFilePath, options.OutputDirectory, useCodexRelativePaths);
        replacements["prompt"] = promptText;

        var translationArguments = SubtitleGenerationProcessService.SplitCommandLine(
            SubtitleGenerationProcessService.ApplyArgumentTemplate(argumentTemplate, replacements));
        var startInfo = SubtitleGenerationProcessService.CreateTranslationProcessStartInfo(
            options.TranslationCommand,
            translationArguments,
            options.OutputDirectory,
            options.DefaultTranslationCommand,
            options.DefaultCodexSparkModel,
            options.AiModel);

        log("Translation command:");
        log(SubtitleGenerationProcessService.FormatProcessCommand(startInfo));
        log($"Translation input: {options.EnglishSrtPath}");
        log($"Translation output: {japaneseSrtPath}");
        log($"Translation prompt: {promptFilePath}");
        log("RUNNING: waiting for AI translation process to finish...");
        await RunExternalProcessAsync(startInfo, "Translation", log);

        if (!File.Exists(japaneseSrtPath))
        {
            throw new FileNotFoundException("Translation process completed but no Japanese SRT file was found.", japaneseSrtPath);
        }

        log("Verifying generated Japanese SRT...");
        var translatedContent = await File.ReadAllTextAsync(japaneseSrtPath, Encoding.UTF8);
        var translatedDocument = SubtitleParser.Parse(translatedContent, japaneseSrtPath);
        if (translatedDocument.Cues.Count == 0)
        {
            throw new InvalidOperationException("生成された日本語字幕に字幕キューが見つかりませんでした。");
        }

        log($"Japanese SRT verified: {translatedDocument.Cues.Count} cues.");
        return japaneseSrtPath;
    }

    public async Task<LearningNotesGenerationResult> GenerateLearningNotesAsync(
        LearningNotesGenerationOptions options,
        Action<string> log)
    {
        Directory.CreateDirectory(options.OutputDirectory);

        var baseName = Path.GetFileNameWithoutExtension(options.VideoPath);
        var notesOutputPath = Path.Combine(options.OutputDirectory, baseName + ".learning-notes.json");
        var noteGenerationStartedAtUtc = SubtitleGenerationProcessService.PrepareGeneratedOutputPath(notesOutputPath);

        var useCodexRelativePaths = SubtitleGenerationProcessService.IsCodexCommand(
            options.Command,
            options.DefaultTranslationCommand);
        var processEnglishSrtPath = useCodexRelativePaths
            ? SubtitleGenerationProcessService.EnsureFileAvailableInWorkingDirectory(options.EnglishSrtPath, options.OutputDirectory)
            : options.EnglishSrtPath;
        var promptFilePath = Path.Combine(options.OutputDirectory, baseName + ".learning-notes.prompt.md");
        var inputDirectory = Path.GetDirectoryName(processEnglishSrtPath) ?? options.OutputDirectory;
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["input"] = SubtitleGenerationProcessService.FormatExternalProcessPath(processEnglishSrtPath, options.OutputDirectory, useCodexRelativePaths),
            ["notesOutput"] = SubtitleGenerationProcessService.FormatExternalProcessPath(notesOutputPath, options.OutputDirectory, useCodexRelativePaths),
            ["inputDir"] = SubtitleGenerationProcessService.FormatExternalProcessDirectory(inputDirectory, options.OutputDirectory, useCodexRelativePaths),
            ["outputDir"] = SubtitleGenerationProcessService.FormatExternalProcessDirectory(options.OutputDirectory, options.OutputDirectory, useCodexRelativePaths),
            ["source"] = options.SourceLanguage,
            ["model"] = options.AiModel,
            ["audienceLevel"] = options.AudienceLevel,
            ["movie"] = options.VideoPath,
            ["title"] = options.MovieTitle,
            ["promptFile"] = SubtitleGenerationProcessService.FormatExternalProcessPath(promptFilePath, options.OutputDirectory, useCodexRelativePaths)
        };
        var promptText = SubtitleGenerationProcessService.ApplyArgumentTemplate(options.PromptTemplate, replacements);
        await File.WriteAllTextAsync(promptFilePath, promptText, Encoding.UTF8);

        var noteArguments = SubtitleGenerationProcessService.SplitCommandLine(
            SubtitleGenerationProcessService.ApplyArgumentTemplate(options.DefaultLearningNotesArguments, replacements));
        var startInfo = SubtitleGenerationProcessService.CreateTranslationProcessStartInfo(
            options.Command,
            noteArguments,
            options.OutputDirectory,
            options.DefaultTranslationCommand,
            options.DefaultCodexSparkModel,
            options.AiModel);

        log("AI note command:");
        log(SubtitleGenerationProcessService.FormatProcessCommand(startInfo));
        log($"AI note input: {options.EnglishSrtPath}");
        log($"AI note output: {notesOutputPath}");
        log($"AI note prompt: {promptFilePath}");
        log("RUNNING: waiting for AI note process to finish...");
        await RunExternalProcessAsync(startInfo, "AI note", log);

        SubtitleGenerationProcessService.EnsureGeneratedOutputIsFresh(
            notesOutputPath,
            noteGenerationStartedAtUtc,
            "AI note process completed but no fresh learning notes JSON was found.");

        return new LearningNotesGenerationResult(options.EnglishSrtPath, notesOutputPath);
    }

    private static async Task RunExternalProcessAsync(
        ProcessStartInfo startInfo,
        string processLabel,
        Action<string> log)
    {
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"{processLabel} process could not be started.");
        }

        log($"RUNNING: {processLabel} process started. PID={process.Id}");
        var outputTask = PumpProcessOutputAsync(process.StandardOutput, log);
        var errorTask = PumpProcessOutputAsync(process.StandardError, log);
        await process.WaitForExitAsync();
        await Task.WhenAll(outputTask, errorTask);
        log($"{processLabel} process exited with code {process.ExitCode}.");
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{processLabel} process exited with code {process.ExitCode}.");
        }
    }

    private static async Task PumpProcessOutputAsync(StreamReader reader, Action<string> log)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            log(line);
        }
    }
}

public sealed record EnglishSubtitleGenerationOptions(
    string VideoPath,
    string OutputDirectory,
    bool Overwrite,
    string PythonCommand,
    string? PythonArgumentsText,
    string Model,
    string Language,
    string Device,
    string ComputeType);

public sealed record JapaneseSubtitleGenerationOptions(
    string MovieTitle,
    string VideoPath,
    string EnglishSrtPath,
    string OutputDirectory,
    bool Overwrite,
    string TranslationCommand,
    string TranslationArgumentsTemplate,
    string TranslationPromptTemplate,
    string SourceLanguage,
    string TargetLanguage,
    string AiModel,
    string DefaultTranslationCommand,
    string DefaultTranslationArguments,
    string DefaultCodexSparkModel);

public sealed record LearningNotesGenerationOptions(
    string MovieTitle,
    string VideoPath,
    string EnglishSrtPath,
    string OutputDirectory,
    string Command,
    string SourceLanguage,
    string AiModel,
    string AudienceLevel,
    string PromptTemplate,
    string DefaultLearningNotesArguments,
    string DefaultTranslationCommand,
    string DefaultCodexSparkModel);

public sealed record LearningNotesGenerationResult(
    string EnglishSrtPath,
    string NotesOutputPath);

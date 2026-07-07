using System.IO;
using System.Text;
using CoffeeMovie.Storage.Services;

namespace CoffeeMovie.Studio.Services;

public sealed class SubtitleAiCommandService
{
    private readonly SubtitleGenerationExternalProcessRunner _processRunner;

    public SubtitleAiCommandService(SubtitleGenerationExternalProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<string> TranslateJapaneseSubtitleAsync(
        JapaneseSubtitleGenerationOptions options,
        string japaneseSrtPath,
        Action<string> log)
    {
        var baseName = Path.GetFileNameWithoutExtension(options.VideoPath);
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
        var startInfo = SubtitleGenerationProcessService.CreateExternalCommandProcessStartInfo(
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
        await _processRunner.RunAsync(startInfo, "Translation", log);

        if (!File.Exists(japaneseSrtPath))
        {
            throw new FileNotFoundException("Translation process completed but no Japanese SRT file was found.", japaneseSrtPath);
        }

        log("Verifying generated Japanese SRT...");
        var translatedContent = await File.ReadAllTextAsync(japaneseSrtPath, Encoding.UTF8);
        var translatedDocument = SubtitleParser.Parse(translatedContent, japaneseSrtPath);
        if (translatedDocument.Cues.Count == 0)
        {
            throw new InvalidOperationException("Generated Japanese SRT did not contain any subtitle cues.");
        }

        log($"Japanese SRT verified: {translatedDocument.Cues.Count} cues.");
        return japaneseSrtPath;
    }

    public async Task<LearningNotesGenerationResult> GenerateLearningNotesAsync(
        LearningNotesGenerationOptions options,
        string notesOutputPath,
        DateTime noteGenerationStartedAtUtc,
        Action<string> log)
    {
        var baseName = Path.GetFileNameWithoutExtension(options.VideoPath);
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
        var startInfo = SubtitleGenerationProcessService.CreateExternalCommandProcessStartInfo(
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
        await _processRunner.RunAsync(startInfo, "AI note", log);

        SubtitleGenerationProcessService.EnsureGeneratedOutputIsFresh(
            notesOutputPath,
            noteGenerationStartedAtUtc,
            "AI note process completed but no fresh learning notes JSON was found.");

        return new LearningNotesGenerationResult(options.EnglishSrtPath, notesOutputPath);
    }
}

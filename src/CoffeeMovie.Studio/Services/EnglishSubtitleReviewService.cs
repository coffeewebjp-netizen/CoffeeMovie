using System.IO;
using System.Text;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Storage.Services;

namespace CoffeeMovie.Studio.Services;

public sealed class EnglishSubtitleReviewService
{
    private readonly WhisperXSubtitleRunner _whisperXRunner;
    private readonly SubtitleGenerationExternalProcessRunner _processRunner;

    public EnglishSubtitleReviewService(
        WhisperXSubtitleRunner whisperXRunner,
        SubtitleGenerationExternalProcessRunner processRunner)
    {
        _whisperXRunner = whisperXRunner;
        _processRunner = processRunner;
    }

    public async Task<string> GenerateAsync(
        EnglishSubtitleGenerationOptions options,
        string englishSrtPath,
        Action<string> log)
    {
        var baseName = Path.GetFileNameWithoutExtension(options.VideoPath);
        var reviewRoot = Path.Combine(
            options.OutputDirectory,
            $"{baseName}.review-{DateTime.Now:yyyyMMdd-HHmmss}");
        var cueRuns = new List<IReadOnlyList<SubtitleCue>>();
        var auditPaths = new List<string>();
        for (var run = 1; run <= 3; run++)
        {
            var runDirectory = Path.Combine(reviewRoot, $"run{run}");
            log($"Review mode: WhisperX run {run}/3.");
            var runOutputPath = await _whisperXRunner.RunToDirectoryAsync(
                options,
                runDirectory,
                $"WhisperX review {run}",
                log);
            var auditPath = Path.Combine(options.OutputDirectory, $"{baseName}.review{run}.srt");
            File.Copy(runOutputPath, auditPath, overwrite: true);
            auditPaths.Add(auditPath);

            var content = await File.ReadAllTextAsync(runOutputPath, Encoding.UTF8);
            var document = SubtitleParser.Parse(content, runOutputPath);
            if (document.Cues.Count == 0)
            {
                throw new InvalidOperationException($"Review mode run {run} produced no subtitle cues.");
            }

            cueRuns.Add(document.Cues);
            log($"Review mode run {run}: {document.Cues.Count} cues.");
        }

        if (await TryMergeWithAiAsync(options, reviewRoot, englishSrtPath, auditPaths, log))
        {
            return englishSrtPath;
        }

        var mergedCues = SubtitleConsensusService.Merge(cueRuns);
        if (mergedCues.Count == 0)
        {
            throw new InvalidOperationException("Review mode completed but no merged cues were produced.");
        }

        await File.WriteAllTextAsync(englishSrtPath, SubtitleParser.ToSrt(mergedCues), Encoding.UTF8);
        log($"Review mode merged {string.Join(" / ", cueRuns.Select(cues => cues.Count.ToString()))} cues into {mergedCues.Count} cues.");
        log($"Review mode final English SRT: {englishSrtPath}");
        return englishSrtPath;
    }

    private async Task<bool> TryMergeWithAiAsync(
        EnglishSubtitleGenerationOptions options,
        string reviewRoot,
        string englishSrtPath,
        IReadOnlyList<string> reviewPaths,
        Action<string> log)
    {
        if (reviewPaths.Count < 3)
        {
            return false;
        }

        var reviewCommand = string.IsNullOrWhiteSpace(options.ReviewCommand)
            ? options.DefaultReviewCommand
            : options.ReviewCommand;
        if (string.IsNullOrWhiteSpace(reviewCommand))
        {
            log("Review mode: AI-AGENT command is not configured. Falling back to deterministic merge.");
            return false;
        }

        try
        {
            log("Review mode: AI-AGENT merge started.");
            var startedAtUtc = SubtitleGenerationProcessService.PrepareGeneratedOutputPath(englishSrtPath);
            var outputDirectory = options.OutputDirectory;
            var baseName = Path.GetFileNameWithoutExtension(options.VideoPath);
            var promptFilePath = Path.Combine(reviewRoot, baseName + ".english-review.prompt.md");
            var useCodexRelativePaths = SubtitleGenerationProcessService.IsCodexCommand(
                reviewCommand,
                options.DefaultReviewCommand);
            var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["review1"] = SubtitleGenerationProcessService.FormatExternalProcessPath(reviewPaths[0], outputDirectory, useCodexRelativePaths),
                ["review2"] = SubtitleGenerationProcessService.FormatExternalProcessPath(reviewPaths[1], outputDirectory, useCodexRelativePaths),
                ["review3"] = SubtitleGenerationProcessService.FormatExternalProcessPath(reviewPaths[2], outputDirectory, useCodexRelativePaths),
                ["input1"] = SubtitleGenerationProcessService.FormatExternalProcessPath(reviewPaths[0], outputDirectory, useCodexRelativePaths),
                ["input2"] = SubtitleGenerationProcessService.FormatExternalProcessPath(reviewPaths[1], outputDirectory, useCodexRelativePaths),
                ["input3"] = SubtitleGenerationProcessService.FormatExternalProcessPath(reviewPaths[2], outputDirectory, useCodexRelativePaths),
                ["output"] = SubtitleGenerationProcessService.FormatExternalProcessPath(englishSrtPath, outputDirectory, useCodexRelativePaths),
                ["outputDir"] = SubtitleGenerationProcessService.FormatExternalProcessDirectory(outputDirectory, outputDirectory, useCodexRelativePaths),
                ["reviewDir"] = SubtitleGenerationProcessService.FormatExternalProcessDirectory(reviewRoot, outputDirectory, useCodexRelativePaths),
                ["promptFile"] = SubtitleGenerationProcessService.FormatExternalProcessPath(promptFilePath, outputDirectory, useCodexRelativePaths),
                ["movie"] = options.VideoPath,
                ["title"] = string.IsNullOrWhiteSpace(options.MovieTitle) ? baseName : options.MovieTitle,
                ["model"] = options.AiModel
            };

            var promptTemplate = string.IsNullOrWhiteSpace(options.ReviewPromptTemplate)
                ? "Compare {review1}, {review2}, and {review3}; write the final SRT to {output}."
                : options.ReviewPromptTemplate;
            var promptText = SubtitleGenerationProcessService.ApplyArgumentTemplate(promptTemplate, replacements);
            await File.WriteAllTextAsync(promptFilePath, promptText, Encoding.UTF8);

            var argumentTemplate = string.IsNullOrWhiteSpace(options.ReviewArgumentsTemplate)
                ? "exec --full-auto -C \"{outputDir}\" --add-dir \"{reviewDir}\" --skip-git-repo-check \"Read {promptFile} and write {output}.\""
                : options.ReviewArgumentsTemplate;
            var reviewArguments = SubtitleGenerationProcessService.SplitCommandLine(
                SubtitleGenerationProcessService.ApplyArgumentTemplate(argumentTemplate, replacements));
            var startInfo = SubtitleGenerationProcessService.CreateExternalCommandProcessStartInfo(
                reviewCommand,
                reviewArguments,
                outputDirectory,
                options.DefaultReviewCommand,
                options.DefaultCodexSparkModel,
                options.AiModel);

            log("English subtitle AI review command:");
            log(SubtitleGenerationProcessService.FormatProcessCommand(startInfo));
            log($"English subtitle AI review prompt: {promptFilePath}");
            log($"English subtitle AI review output: {englishSrtPath}");
            log("RUNNING: waiting for AI-AGENT subtitle review to finish...");
            await _processRunner.RunAsync(startInfo, "English subtitle AI review", log);

            SubtitleGenerationProcessService.EnsureGeneratedOutputIsFresh(
                englishSrtPath,
                startedAtUtc,
                "AI-AGENT review completed but no fresh English SRT was found.");
            var reviewedContent = await File.ReadAllTextAsync(englishSrtPath, Encoding.UTF8);
            var reviewedDocument = SubtitleParser.Parse(reviewedContent, englishSrtPath);
            if (reviewedDocument.Cues.Count == 0)
            {
                throw new InvalidOperationException("AI-AGENT review output did not contain any SRT cues.");
            }

            log($"Review mode: AI-AGENT output verified: {reviewedDocument.Cues.Count} cues.");
            return true;
        }
        catch (Exception ex)
        {
            log($"WARNING: AI-AGENT review failed. Falling back to deterministic merge. {ex.Message}");
            return false;
        }
    }
}

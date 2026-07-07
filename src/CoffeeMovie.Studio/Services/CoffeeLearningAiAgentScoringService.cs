using System.Diagnostics;
using System.IO;
using System.Text;
using CoffeeMovie.Core.Services;

namespace CoffeeMovie.Studio.Services;

public sealed class CoffeeLearningAiAgentScoringService
{
    public async Task<CoffeeLearningWordScore> ScoreAsync(
        CoffeeLearningAiAgentScoringSettings settings,
        CoffeeLearningWordScoreInput input,
        CancellationToken cancellationToken = default)
    {
        var workingDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CoffeeMovie",
            "learning-score",
            DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(workingDirectory);

        var promptPath = Path.Combine(workingDirectory, "score.prompt.md");
        var outputPath = Path.Combine(workingDirectory, "score.json");
        var inputPath = Path.Combine(workingDirectory, "input.json");
        await File.WriteAllTextAsync(
            inputPath,
            System.Text.Json.JsonSerializer.Serialize(input, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8,
            cancellationToken);
        await File.WriteAllTextAsync(promptPath, CoffeeLearningWordScorePrompt.Build(input), Encoding.UTF8, cancellationToken);

        var command = string.IsNullOrWhiteSpace(settings.Command)
            ? CoffeeLearningScoringDefaults.DefaultAiAgentCommand
            : settings.Command.Trim();
        var argumentTemplate = string.IsNullOrWhiteSpace(settings.ArgumentsTemplate)
            ? CoffeeLearningScoringDefaults.DefaultAiAgentArguments
            : settings.ArgumentsTemplate.Trim();
        var useCodexRelativePaths = SubtitleGenerationProcessService.IsCodexCommand(
            command,
            CoffeeLearningScoringDefaults.DefaultAiAgentCommand);
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["workingDir"] = SubtitleGenerationProcessService.FormatExternalProcessDirectory(workingDirectory, workingDirectory, useCodexRelativePaths),
            ["promptFile"] = SubtitleGenerationProcessService.FormatExternalProcessPath(promptPath, workingDirectory, useCodexRelativePaths),
            ["inputFile"] = SubtitleGenerationProcessService.FormatExternalProcessPath(inputPath, workingDirectory, useCodexRelativePaths),
            ["outputFile"] = SubtitleGenerationProcessService.FormatExternalProcessPath(outputPath, workingDirectory, useCodexRelativePaths),
            ["word"] = input.Word,
            ["meaning"] = input.Meaning,
            ["memo"] = input.Memo ?? string.Empty
        };

        var arguments = SubtitleGenerationProcessService.SplitCommandLine(
            SubtitleGenerationProcessService.ApplyArgumentTemplate(argumentTemplate, replacements));
        var startInfo = SubtitleGenerationProcessService.CreateExternalCommandProcessStartInfo(
            command,
            arguments,
            workingDirectory,
            CoffeeLearningScoringDefaults.DefaultAiAgentCommand,
            CoffeeLearningScoringDefaults.DefaultAiAgentModel,
            settings.Model);
        var output = await RunProcessAsync(startInfo, cancellationToken);

        if (File.Exists(outputPath))
        {
            output = await File.ReadAllTextAsync(outputPath, Encoding.UTF8, cancellationToken);
        }

        return CoffeeLearningWordScoreParser.Parse(output);
    }

    private static async Task<string> RunProcessAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("AIAGENT scoring process could not be started.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"AIAGENT scoring exited with code {process.ExitCode}: {TrimForError(error)}");
        }

        return string.IsNullOrWhiteSpace(output) ? error : output;
    }

    private static string TrimForError(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 400 ? trimmed : trimmed[..400];
    }
}

public sealed record CoffeeLearningAiAgentScoringSettings(
    string? Command,
    string? Model,
    string? ArgumentsTemplate);

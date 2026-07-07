using System.Diagnostics;
using System.IO;
using System.Text;

namespace CoffeeMovie.Studio.Services;

public sealed class WhisperXSubtitleRunner
{
    private readonly SubtitleGenerationExternalProcessRunner _processRunner;

    public WhisperXSubtitleRunner(SubtitleGenerationExternalProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<string> RunToDirectoryAsync(
        EnglishSubtitleGenerationOptions options,
        string outputDirectory,
        string processLabel,
        Action<string> log)
    {
        Directory.CreateDirectory(outputDirectory);

        var baseName = Path.GetFileNameWithoutExtension(options.VideoPath);
        var generatedSrtPath = Path.Combine(outputDirectory, baseName + ".srt");
        var englishSrtPath = Path.Combine(outputDirectory, baseName + ".en.srt");

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
        startInfo.ArgumentList.Add(outputDirectory);
        startInfo.ArgumentList.Add("--device");
        startInfo.ArgumentList.Add(options.Device);
        startInfo.ArgumentList.Add("--compute_type");
        startInfo.ArgumentList.Add(options.ComputeType);
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

        log("Command:");
        log(SubtitleGenerationProcessService.FormatProcessCommand(startInfo));
        log("RUNNING: waiting for WhisperX process to finish...");
        await _processRunner.RunAsync(startInfo, processLabel, log);

        if (File.Exists(englishSrtPath))
        {
            return englishSrtPath;
        }

        if (File.Exists(generatedSrtPath))
        {
            return generatedSrtPath;
        }

        throw new FileNotFoundException("WhisperX completed but no SRT file was found.", generatedSrtPath);
    }
}

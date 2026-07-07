using System.Diagnostics;
using System.IO;

namespace CoffeeMovie.Studio.Services;

public sealed class SubtitleGenerationExternalProcessRunner
{
    public async Task RunAsync(
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

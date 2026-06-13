namespace CoffeeMovie.Storage.Models;

public sealed class StudioPreferences
{
    public string SubtitleTagHighlightColor { get; set; } = "#F6C945";

    public bool ShowDualSubtitles { get; set; }

    public string? WhisperOutputDirectory { get; set; }

    public string WhisperPythonCommand { get; set; } = "py";

    public string WhisperPythonArguments { get; set; } = "-3.10 -m whisperx";

    public string WhisperModel { get; set; } = "medium";

    public string WhisperLanguage { get; set; } = "en";

    public string WhisperDevice { get; set; } = "cuda";

    public string WhisperComputeType { get; set; } = "float16";
}

namespace CoffeeMovie.Storage.Models;

public sealed class StudioPreferences
{
    public string SubtitleTagHighlightColor { get; set; } = "#F6C945";

    public bool ShowDualSubtitles { get; set; }

    public bool ShowLearningNotes { get; set; }

    public string EnglishSubtitleOverlayPosition { get; set; } = "below2";

    public string JapaneseSubtitleOverlayPosition { get; set; } = "below1";

    public string AiNoteOverlayPosition { get; set; } = "above1";

    public string UserNoteOverlayPosition { get; set; } = "above2";

    public string? WhisperOutputDirectory { get; set; }

    public string? GoogleDriveRootPath { get; set; }

    public string WhisperPythonCommand { get; set; } = "py";

    public string WhisperPythonArguments { get; set; } = "-3.10 -m whisperx";

    public string WhisperModel { get; set; } = "medium";

    public string WhisperLanguage { get; set; } = "en";

    public string WhisperDevice { get; set; } = "cuda";

    public string WhisperComputeType { get; set; } = "float16";

    public string EnglishSubtitleGenerationMode { get; set; } = "normal";

    public string? TranslationCommand { get; set; } = "codex-spark";

    public string? TranslationModel { get; set; } = "gpt-5.3-codex-spark";

    public string TranslationArguments { get; set; } = "exec --full-auto -C \"{outputDir}\" --add-dir \"{inputDir}\" --skip-git-repo-check \"You are codex-spark for CoffeeMovie. Read the prompt file at {promptFile}, translate {input}, and write the Japanese SRT to {output}.\"";

    public string TranslationSourceLanguage { get; set; } = "en";

    public string TranslationTargetLanguage { get; set; } = "ja";

    public string? TranslationPrompt { get; set; }

    public string? LearningNotesPrompt { get; set; }

    public string LearningNotesAudienceLevel { get; set; } = "B1";
}

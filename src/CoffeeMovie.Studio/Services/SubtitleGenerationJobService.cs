using System.IO;

namespace CoffeeMovie.Studio.Services;

public sealed class SubtitleGenerationJobService
{
    private readonly SubtitleGenerationExternalProcessRunner _processRunner;
    private readonly WhisperXSubtitleRunner _whisperXRunner;
    private readonly EnglishSubtitleReviewService _englishReviewService;
    private readonly SubtitleAiCommandService _aiCommandService;

    public SubtitleGenerationJobService()
    {
        _processRunner = new SubtitleGenerationExternalProcessRunner();
        _whisperXRunner = new WhisperXSubtitleRunner(_processRunner);
        _englishReviewService = new EnglishSubtitleReviewService(_whisperXRunner, _processRunner);
        _aiCommandService = new SubtitleAiCommandService(_processRunner);
    }

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

        if (options.Mode == EnglishSubtitleGenerationMode.Review)
        {
            return await _englishReviewService.GenerateAsync(options, englishSrtPath, log);
        }

        var outputPath = await _whisperXRunner.RunToDirectoryAsync(options, options.OutputDirectory, "WhisperX", log);
        if (!string.Equals(outputPath, englishSrtPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(outputPath, englishSrtPath, overwrite: true);
            log($"Renamed generated SRT: {englishSrtPath}");
        }

        return englishSrtPath;
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

        return await _aiCommandService.TranslateJapaneseSubtitleAsync(options, japaneseSrtPath, log);
    }

    public async Task<LearningNotesGenerationResult> GenerateLearningNotesAsync(
        LearningNotesGenerationOptions options,
        Action<string> log)
    {
        Directory.CreateDirectory(options.OutputDirectory);

        var baseName = Path.GetFileNameWithoutExtension(options.VideoPath);
        var notesOutputPath = Path.Combine(options.OutputDirectory, baseName + ".learning-notes.json");
        var noteGenerationStartedAtUtc = SubtitleGenerationProcessService.PrepareGeneratedOutputPath(notesOutputPath);

        return await _aiCommandService.GenerateLearningNotesAsync(
            options,
            notesOutputPath,
            noteGenerationStartedAtUtc,
            log);
    }

}

namespace CoffeeMovie.Studio.Services;

public sealed record EnglishSubtitleGenerationOptions(
    string VideoPath,
    string OutputDirectory,
    bool Overwrite,
    string PythonCommand,
    string? PythonArgumentsText,
    string Model,
    string Language,
    string Device,
    string ComputeType,
    EnglishSubtitleGenerationMode Mode,
    string MovieTitle,
    string ReviewCommand,
    string ReviewArgumentsTemplate,
    string ReviewPromptTemplate,
    string AiModel,
    string DefaultReviewCommand,
    string DefaultCodexSparkModel);

public enum EnglishSubtitleGenerationMode
{
    Normal,
    Review
}

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

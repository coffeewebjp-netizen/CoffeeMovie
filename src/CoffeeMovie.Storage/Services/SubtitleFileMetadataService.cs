using CoffeeMovie.Core.Models;

namespace CoffeeMovie.Storage.Services;

public sealed record SubtitleFileMetadata(
    string Label,
    string? Language,
    SubtitleTrackRole Role,
    string? GroupKey);

public static class SubtitleFileMetadataService
{
    public static SubtitleFileMetadata Infer(string fileName)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var language = SplitLanguageSuffix(nameWithoutExtension, out var groupKey);

        if (language is null)
        {
            return new SubtitleFileMetadata(nameWithoutExtension, null, SubtitleTrackRole.Unknown, null);
        }

        return new SubtitleFileMetadata(
            LabelForLanguage(language),
            language,
            RoleForLanguage(language),
            groupKey);
    }

    private static string? SplitLanguageSuffix(string nameWithoutExtension, out string? groupKey)
    {
        foreach (var separator in new[] { '.', '_', '-' })
        {
            var marker = separator.ToString();
            var index = nameWithoutExtension.LastIndexOf(separator);
            if (index <= 0 || index >= nameWithoutExtension.Length - 2)
            {
                continue;
            }

            var suffix = nameWithoutExtension[(index + marker.Length)..].Trim().ToLowerInvariant();
            if (!IsSupportedLanguage(suffix))
            {
                continue;
            }

            groupKey = nameWithoutExtension[..index].Trim();
            return NormalizeLanguage(suffix);
        }

        groupKey = null;
        return null;
    }

    private static bool IsSupportedLanguage(string language)
    {
        return language is "en" or "ja" or "jp" or "jpn";
    }

    private static string LabelForLanguage(string language)
    {
        return language switch
        {
            "en" => "English",
            "ja" or "jp" or "jpn" => "Japanese",
            _ => language
        };
    }

    private static string NormalizeLanguage(string language)
    {
        return language is "jp" or "jpn" ? "ja" : language;
    }

    private static SubtitleTrackRole RoleForLanguage(string language)
    {
        return language switch
        {
            "en" => SubtitleTrackRole.LearningTarget,
            "ja" or "jp" or "jpn" => SubtitleTrackRole.Translation,
            _ => SubtitleTrackRole.Unknown
        };
    }
}

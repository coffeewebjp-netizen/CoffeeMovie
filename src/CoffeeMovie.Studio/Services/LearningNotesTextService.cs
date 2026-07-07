namespace CoffeeMovie.Studio.Services;

internal static class LearningNotesTextService
{
    internal static string? NormalizeNoteText(LearningNoteImportRow note)
    {
        var text = NormalizeOptionalText(note.Note);
        var cefr = NormalizeOptionalText(note.Cefr);
        if (text is null)
        {
            return null;
        }

        if (IsNoDisplayLearningNoteText(text))
        {
            return null;
        }

        if (cefr is not null
            && !text.Contains("CEFR", StringComparison.OrdinalIgnoreCase)
            && !text.Contains(cefr, StringComparison.OrdinalIgnoreCase))
        {
            text = $"CEFR {cefr}: {text}";
        }

        return text;
    }

    internal static string FormatTextSample(IReadOnlyList<string> values)
    {
        const int maxShown = 8;
        var sample = string.Join(" / ", values.Take(maxShown));
        return values.Count > maxShown
            ? $"{sample} / ... ({values.Count}件)"
            : sample;
    }

    private static bool IsNoDisplayLearningNoteText(string text)
    {
        return text.Contains("コメント不要", StringComparison.Ordinal)
            || text.Contains("解説不要", StringComparison.Ordinal)
            || text.Contains("メモ不要", StringComparison.Ordinal)
            || text.Contains("対象者レベル以下", StringComparison.Ordinal)
            || text.Contains("対象レベル以下", StringComparison.Ordinal);
    }

    internal static bool ContainsNormalizedFocus(string cueText, string focus)
    {
        var normalizedCue = NormalizeWhitespace(cueText);
        var normalizedFocus = NormalizeWhitespace(focus);
        return normalizedFocus.Length > 0
            && normalizedCue.IndexOf(normalizedFocus, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string NormalizeWhitespace(string text)
    {
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    internal static string? NormalizeOptionalText(string? text)
    {
        var normalized = text?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    internal static string FormatIndexSample(IReadOnlyList<int> indexes)
    {
        const int maxShown = 12;
        var sample = string.Join(", ", indexes.Take(maxShown));
        return indexes.Count > maxShown
            ? $"{sample}, ... ({indexes.Count}件)"
            : sample;
    }
}

using System.Globalization;
using System.Text.Json;
using CoffeeMovie.Core.Models;

namespace CoffeeMovie.Studio.Services;

public static class LearningNotesImportService
{
    public static LearningNotesImportPlan CreateImportPlan(string json, IReadOnlyList<SubtitleCue> expectedCues)
    {
        var notes = ParseJson(json);
        if (notes.Count == 0)
        {
            throw new InvalidOperationException("AIメモJSONに取り込めるメモが見つかりませんでした。");
        }

        ValidateQuality(notes, expectedCues);

        var cueByIndex = expectedCues
            .GroupBy(cue => cue.Index)
            .ToDictionary(group => group.Key, group => group.First());
        var notesToImport = new List<LearningNoteImportRow>();
        var relocatedFocusNotes = new List<LearningNoteFocusRelocation>();
        var unresolvedFocusNotes = new List<LearningNoteFocusIssue>();
        foreach (var note in notes)
        {
            if (note.Index <= 0 || !cueByIndex.TryGetValue(note.Index, out var cue))
            {
                continue;
            }

            if (NormalizeNoteText(note) is null)
            {
                notesToImport.Add(note);
                continue;
            }

            if (!HasValidFocus(note, cue.Text))
            {
                var focus = NormalizeOptionalText(note.Focus);
                if (focus is not null
                    && TryFindCueContainingFocus(expectedCues, note.Index, focus, out var retargetCue)
                    && retargetCue is not null)
                {
                    notesToImport.Add(note with { Index = retargetCue.Index });
                    relocatedFocusNotes.Add(new LearningNoteFocusRelocation(note.Index, retargetCue.Index, focus));
                    continue;
                }

                unresolvedFocusNotes.Add(new LearningNoteFocusIssue(note.Index, focus ?? "(focusなし)"));
                continue;
            }

            notesToImport.Add(note);
        }

        return new LearningNotesImportPlan(
            MergeByIndex(notesToImport),
            relocatedFocusNotes,
            unresolvedFocusNotes);
    }

    public static string? NormalizeNoteText(LearningNoteImportRow note)
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

    public static string FormatTextSample(IReadOnlyList<string> values)
    {
        const int maxShown = 8;
        var sample = string.Join(" / ", values.Take(maxShown));
        return values.Count > maxShown
            ? $"{sample} / ... ({values.Count}件)"
            : sample;
    }

    private static List<LearningNoteImportRow> ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var notesElement = root;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (TryGetProperty(root, "notes", out var notesProperty))
            {
                notesElement = notesProperty;
            }
            else if (TryGetProperty(root, "items", out var itemsProperty))
            {
                notesElement = itemsProperty;
            }
        }

        if (notesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var rows = new List<LearningNoteImportRow>();
        foreach (var item in notesElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            rows.Add(new LearningNoteImportRow(
                TryGetInt(item, "index")
                    ?? TryGetInt(item, "cueIndex")
                    ?? TryGetInt(item, "cue_index")
                    ?? 0,
                TryGetString(item, "cefr")
                    ?? TryGetString(item, "level"),
                TryGetString(item, "focus")
                    ?? TryGetString(item, "phrase")
                    ?? TryGetString(item, "word"),
                TryGetString(item, "note")
                    ?? TryGetString(item, "memo")
                    ?? TryGetString(item, "comment")));
        }

        return rows;
    }

    private static bool IsNoDisplayLearningNoteText(string text)
    {
        return text.Contains("コメント不要", StringComparison.Ordinal)
            || text.Contains("解説不要", StringComparison.Ordinal)
            || text.Contains("メモ不要", StringComparison.Ordinal)
            || text.Contains("対象者レベル以下", StringComparison.Ordinal)
            || text.Contains("対象レベル以下", StringComparison.Ordinal);
    }

    private static void ValidateQuality(IReadOnlyList<LearningNoteImportRow> notes, IReadOnlyList<SubtitleCue> expectedCues)
    {
        var expectedCueCount = expectedCues.Count;
        if (expectedCueCount > 0 && notes.Count > expectedCueCount)
        {
            throw new InvalidOperationException(
                $"AIメモJSONの件数が字幕数より多すぎます。 notes={notes.Count}, cues={expectedCueCount}");
        }

        var expectedIndexes = expectedCues
            .Select(cue => cue.Index)
            .Where(index => index > 0)
            .Distinct()
            .Order()
            .ToList();
        if (expectedIndexes.Count == expectedCueCount)
        {
            ValidateIndexes(notes, expectedIndexes);
        }

        var cueTextByIndex = expectedCues
            .GroupBy(cue => cue.Index)
            .ToDictionary(group => group.Key, group => group.First().Text);
        var missingFocusNotes = new List<int>();
        var mismatchedFocusNotes = new List<string>();
        foreach (var note in notes)
        {
            var normalizedNote = NormalizeNoteText(note);
            if (normalizedNote is null)
            {
                continue;
            }

            var focus = NormalizeOptionalText(note.Focus);
            if (focus is null)
            {
                missingFocusNotes.Add(note.Index);
                continue;
            }

            if (cueTextByIndex.TryGetValue(note.Index, out var cueText)
                && !ContainsNormalizedFocus(cueText, focus))
            {
                mismatchedFocusNotes.Add($"{note.Index}: {focus}");
            }
        }

        if (missingFocusNotes.Count > 0)
        {
            throw new InvalidOperationException(
                "AIメモJSONの実メモにfocusがありません: "
                + FormatIndexSample(missingFocusNotes));
        }

        var normalizedNotes = notes
            .Select(NormalizeNoteText)
            .Where(note => !string.IsNullOrWhiteSpace(note))
            .Cast<string>()
            .ToList();

        if (mismatchedFocusNotes.Count > 0)
        {
            var maxAllowedMismatches = Math.Max(10, (int)Math.Ceiling(normalizedNotes.Count * 0.25));
            if (mismatchedFocusNotes.Count > maxAllowedMismatches)
            {
                throw new InvalidOperationException(
                    "AIメモJSONのfocusが対象字幕本文に存在しないnoteが多すぎます: "
                    + FormatTextSample(mismatchedFocusNotes)
                    + "。前後の字幕ではなく、同じ番号の字幕本文にある語句だけを使ってください。");
            }
        }

        ValidateNormalizedNotes(normalizedNotes, expectedCueCount);
    }

    private static void ValidateIndexes(IReadOnlyList<LearningNoteImportRow> notes, IReadOnlyList<int> expectedIndexes)
    {
        var noteIndexes = notes.Select(note => note.Index).ToList();
        var invalidIndexes = noteIndexes
            .Where(index => index <= 0)
            .Distinct()
            .Order()
            .ToList();
        if (invalidIndexes.Count > 0)
        {
            throw new InvalidOperationException(
                "AIメモJSONに無効な字幕番号があります: "
                + FormatIndexSample(invalidIndexes));
        }

        var duplicatedIndexes = noteIndexes
            .GroupBy(index => index)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order()
            .ToList();
        if (duplicatedIndexes.Count > 0)
        {
            throw new InvalidOperationException(
                "AIメモJSONに重複した字幕番号があります: "
                + FormatIndexSample(duplicatedIndexes));
        }

        var noteIndexSet = noteIndexes.Distinct().Order().ToList();
        var unexpectedIndexes = noteIndexSet.Except(expectedIndexes).ToList();
        if (unexpectedIndexes.Count > 0)
        {
            throw new InvalidOperationException(
                "AIメモJSONに対象字幕に存在しない番号があります: "
                + FormatIndexSample(unexpectedIndexes));
        }
    }

    private static void ValidateNormalizedNotes(IReadOnlyList<string> normalizedNotes, int expectedCueCount)
    {
        if (normalizedNotes.Count == 0)
        {
            throw new InvalidOperationException("AIメモJSONに取り込めるnoteがありません。重要な語彙・構文・世界観語だけをnoteにしてください。");
        }

        var placeholderNotes = normalizedNotes
            .Where(note =>
                note.Contains("$k", StringComparison.OrdinalIgnoreCase)
                || note.Contains("{", StringComparison.Ordinal)
                || note.Contains("}", StringComparison.Ordinal)
                || note.Contains("など抽象語", StringComparison.Ordinal)
                || note.Contains("基本表現。", StringComparison.Ordinal)
                || note.Contains("最小文型", StringComparison.Ordinal)
                || note.Contains("日常の応答や確認", StringComparison.Ordinal)
                || note.Contains("動詞フレーズ中心", StringComparison.Ordinal)
                || note.Contains("時制・文型", StringComparison.Ordinal)
                || note.Contains("理由や条件を含み", StringComparison.Ordinal)
                || note.Contains("習得しやすい", StringComparison.Ordinal)
                || note.Contains("基礎語彙", StringComparison.Ordinal))
            .Take(3)
            .ToList();
        if (placeholderNotes.Count > 0)
        {
            throw new InvalidOperationException(
                "生成されたAIメモの品質が低いため取り込みませんでした。テンプレート/プレースホルダのような文があります: "
                + string.Join(" / ", placeholderNotes));
        }

        if (expectedCueCount >= 40 && normalizedNotes.Count > (int)Math.Ceiling(expectedCueCount * 0.35))
        {
            throw new InvalidOperationException(
                $"生成されたAIメモの品質が低いため取り込みませんでした。noteが多すぎます。 notes={normalizedNotes.Count}, cues={expectedCueCount}。"
                + " B1以上の重要表現、世界観語、特殊な口語だけに絞ってください。");
        }

        if (normalizedNotes.Count < 30)
        {
            return;
        }

        var noteGroups = normalizedNotes
            .GroupBy(note => note, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ToList();
        var mostRepeated = noteGroups.First();
        var uniqueCount = noteGroups.Count;
        var maxAllowedRepeat = Math.Max(8, (int)Math.Ceiling(normalizedNotes.Count * 0.20));
        var minExpectedUnique = Math.Max(15, (int)Math.Ceiling(normalizedNotes.Count * 0.30));

        if (mostRepeated.Count() > maxAllowedRepeat)
        {
            throw new InvalidOperationException(
                $"生成されたAIメモの品質が低いため取り込みませんでした。同じnoteが多すぎます。 {mostRepeated.Count()}件 / {normalizedNotes.Count}件。"
                + $" 例: {mostRepeated.Key}");
        }

        if (uniqueCount < minExpectedUnique)
        {
            throw new InvalidOperationException(
                $"生成されたAIメモの品質が低いため取り込みませんでした。内容が単調すぎます。 unique={uniqueCount}, notes={normalizedNotes.Count}。"
                + " 各字幕固有の語句と理由を含めて再生成してください。");
        }
    }

    private static bool HasValidFocus(LearningNoteImportRow note, string cueText)
    {
        var focus = NormalizeOptionalText(note.Focus);
        return focus is not null && ContainsNormalizedFocus(cueText, focus);
    }

    private static bool TryFindCueContainingFocus(
        IReadOnlyList<SubtitleCue> cues,
        int sourceIndex,
        string focus,
        out SubtitleCue? matchedCue)
    {
        const int nearbyCueWindow = 8;
        matchedCue = cues
            .Where(cue => Math.Abs(cue.Index - sourceIndex) <= nearbyCueWindow)
            .Where(cue => ContainsNormalizedFocus(cue.Text, focus))
            .OrderBy(cue => Math.Abs(cue.Index - sourceIndex))
            .ThenBy(cue => cue.Index)
            .FirstOrDefault();
        if (matchedCue is not null)
        {
            return true;
        }

        matchedCue = cues
            .Where(cue => ContainsNormalizedFocus(cue.Text, focus))
            .OrderBy(cue => Math.Abs(cue.Index - sourceIndex))
            .ThenBy(cue => cue.Index)
            .FirstOrDefault();
        return matchedCue is not null;
    }

    private static List<LearningNoteImportRow> MergeByIndex(IReadOnlyList<LearningNoteImportRow> notes)
    {
        return notes
            .GroupBy(note => note.Index)
            .OrderBy(group => group.Key)
            .Select(MergeGroup)
            .ToList();
    }

    private static LearningNoteImportRow MergeGroup(IGrouping<int, LearningNoteImportRow> group)
    {
        var rows = group.ToList();
        if (rows.Count == 1)
        {
            return rows[0];
        }

        var first = rows[0];
        var mergedNotes = rows
            .Select(NormalizeNoteText)
            .Where(note => !string.IsNullOrWhiteSpace(note))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (mergedNotes.Count == 0)
        {
            return first;
        }

        var mergedFocus = string.Join(
            " / ",
            rows
                .Select(row => NormalizeOptionalText(row.Focus))
                .Where(focus => !string.IsNullOrWhiteSpace(focus))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase));

        return first with
        {
            Focus = string.IsNullOrWhiteSpace(mergedFocus) ? first.Focus : mergedFocus,
            Note = string.Join(" / ", mergedNotes)
        };
    }

    private static bool ContainsNormalizedFocus(string cueText, string focus)
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

    private static string? NormalizeOptionalText(string? text)
    {
        var normalized = text?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string FormatIndexSample(IReadOnlyList<int> indexes)
    {
        const int maxShown = 12;
        var sample = string.Join(", ", indexes.Take(maxShown));
        return indexes.Count > maxShown
            ? $"{sample}, ... ({indexes.Count}件)"
            : sample;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static int? TryGetInt(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? number
                : null;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }
}

public sealed record LearningNotesImportPlan(
    IReadOnlyList<LearningNoteImportRow> NotesToImport,
    IReadOnlyList<LearningNoteFocusRelocation> RelocatedFocusNotes,
    IReadOnlyList<LearningNoteFocusIssue> UnresolvedFocusNotes);

public sealed record LearningNoteImportRow(int Index, string? Cefr, string? Focus, string? Note);

public sealed record LearningNoteFocusRelocation(int SourceIndex, int TargetIndex, string Focus);

public sealed record LearningNoteFocusIssue(int Index, string Focus);

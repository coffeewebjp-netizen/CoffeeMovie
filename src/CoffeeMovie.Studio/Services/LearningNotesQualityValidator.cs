using CoffeeMovie.Core.Models;

namespace CoffeeMovie.Studio.Services;

internal static class LearningNotesQualityValidator
{
    internal static void Validate(IReadOnlyList<LearningNoteImportRow> notes, IReadOnlyList<SubtitleCue> expectedCues)
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
            var normalizedNote = LearningNotesTextService.NormalizeNoteText(note);
            if (normalizedNote is null)
            {
                continue;
            }

            var focus = LearningNotesTextService.NormalizeOptionalText(note.Focus);
            if (focus is null)
            {
                missingFocusNotes.Add(note.Index);
                continue;
            }

            if (cueTextByIndex.TryGetValue(note.Index, out var cueText)
                && !LearningNotesTextService.ContainsNormalizedFocus(cueText, focus))
            {
                mismatchedFocusNotes.Add($"{note.Index}: {focus}");
            }
        }

        if (missingFocusNotes.Count > 0)
        {
            throw new InvalidOperationException(
                "AIメモJSONの実メモにfocusがありません: "
                + LearningNotesTextService.FormatIndexSample(missingFocusNotes));
        }

        var normalizedNotes = notes
            .Select(LearningNotesTextService.NormalizeNoteText)
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
                    + LearningNotesTextService.FormatTextSample(mismatchedFocusNotes)
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
                + LearningNotesTextService.FormatIndexSample(invalidIndexes));
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
                + LearningNotesTextService.FormatIndexSample(duplicatedIndexes));
        }

        var noteIndexSet = noteIndexes.Distinct().Order().ToList();
        var unexpectedIndexes = noteIndexSet.Except(expectedIndexes).ToList();
        if (unexpectedIndexes.Count > 0)
        {
            throw new InvalidOperationException(
                "AIメモJSONに対象字幕に存在しない番号があります: "
                + LearningNotesTextService.FormatIndexSample(unexpectedIndexes));
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
}

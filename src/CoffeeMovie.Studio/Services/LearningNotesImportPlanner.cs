using CoffeeMovie.Core.Models;

namespace CoffeeMovie.Studio.Services;

internal static class LearningNotesImportPlanner
{
    internal static LearningNotesImportPlan Create(IReadOnlyList<LearningNoteImportRow> notes, IReadOnlyList<SubtitleCue> expectedCues)
    {
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

            if (LearningNotesTextService.NormalizeNoteText(note) is null)
            {
                notesToImport.Add(note);
                continue;
            }

            if (!HasValidFocus(note, cue.Text))
            {
                var focus = LearningNotesTextService.NormalizeOptionalText(note.Focus);
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

    private static bool HasValidFocus(LearningNoteImportRow note, string cueText)
    {
        var focus = LearningNotesTextService.NormalizeOptionalText(note.Focus);
        return focus is not null && LearningNotesTextService.ContainsNormalizedFocus(cueText, focus);
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
            .Where(cue => LearningNotesTextService.ContainsNormalizedFocus(cue.Text, focus))
            .OrderBy(cue => Math.Abs(cue.Index - sourceIndex))
            .ThenBy(cue => cue.Index)
            .FirstOrDefault();
        if (matchedCue is not null)
        {
            return true;
        }

        matchedCue = cues
            .Where(cue => LearningNotesTextService.ContainsNormalizedFocus(cue.Text, focus))
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
            .Select(LearningNotesTextService.NormalizeNoteText)
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
                .Select(row => LearningNotesTextService.NormalizeOptionalText(row.Focus))
                .Where(focus => !string.IsNullOrWhiteSpace(focus))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase));

        return first with
        {
            Focus = string.IsNullOrWhiteSpace(mergedFocus) ? first.Focus : mergedFocus,
            Note = string.Join(" / ", mergedNotes)
        };
    }
}

using CoffeeMovie.Core.Models;

namespace CoffeeMovie.Studio.Services;

public static class LearningNotesImportService
{
    public static LearningNotesImportPlan CreateImportPlan(string json, IReadOnlyList<SubtitleCue> expectedCues)
    {
        var notes = LearningNotesJsonParser.Parse(json);
        if (notes.Count == 0)
        {
            throw new InvalidOperationException("AIメモJSONに取り込めるメモが見つかりませんでした。");
        }

        LearningNotesQualityValidator.Validate(notes, expectedCues);
        return LearningNotesImportPlanner.Create(notes, expectedCues);
    }

    public static string? NormalizeNoteText(LearningNoteImportRow note)
    {
        return LearningNotesTextService.NormalizeNoteText(note);
    }

    public static string FormatTextSample(IReadOnlyList<string> values)
    {
        return LearningNotesTextService.FormatTextSample(values);
    }
}

public sealed record LearningNotesImportPlan(
    IReadOnlyList<LearningNoteImportRow> NotesToImport,
    IReadOnlyList<LearningNoteFocusRelocation> RelocatedFocusNotes,
    IReadOnlyList<LearningNoteFocusIssue> UnresolvedFocusNotes);

public sealed record LearningNoteImportRow(int Index, string? Cefr, string? Focus, string? Note);

public sealed record LearningNoteFocusRelocation(int SourceIndex, int TargetIndex, string Focus);

public sealed record LearningNoteFocusIssue(int Index, string Focus);

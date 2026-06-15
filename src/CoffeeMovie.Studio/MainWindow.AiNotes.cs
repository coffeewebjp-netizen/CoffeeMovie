using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Storage.Services;
using CoffeeMovie.Studio.Services;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
    private async void OnGenerateAiNotesClicked(object sender, RoutedEventArgs e)
    {
        await RunLearningNotesGenerationJobAsync();
    }

    private async Task RunLearningNotesGenerationJobAsync()
    {
        if (_isSubtitleGenerationRunning)
        {
            return;
        }

        if (_selectedMovie is null)
        {
            SetStatus("AIメモを追加する動画を選択してください。");
            return;
        }

        try
        {
            _isSubtitleGenerationRunning = true;
            SetSubtitleGenerationEnabled(false);
            SubtitleGenerationLogTextBox.Clear();
            var startedAt = DateTimeOffset.Now;
            SetSubtitleGenerationState("AIメモ実行中");
            SetStatus("AIメモを生成中です。");
            AppendSubtitleGenerationLog("AI learning note generation started.");
            AppendSubtitleGenerationLog("RUNNING: external AI note job is active.");

            var importedNoteCount = await GenerateLearningNotesAsync(_selectedMovie);
            await RefreshMoviesAsync(_selectedMovie.Id);
            var elapsed = DateTimeOffset.Now - startedAt;
            SetSubtitleGenerationState($"完了 ({FormatElapsed(elapsed)})");
            SetStatus($"AIメモを追加しました: {importedNoteCount} cues");
            AppendSubtitleGenerationLog($"COMPLETED: {importedNoteCount} AI notes imported in {FormatElapsed(elapsed)}.");
        }
        catch (Exception ex)
        {
            SetSubtitleGenerationState("失敗");
            ShowError("AIメモの追加に失敗しました", ex);
            AppendSubtitleGenerationLog("ERROR: " + ex.Message);
        }
        finally
        {
            _isSubtitleGenerationRunning = false;
            SetSubtitleGenerationEnabled(_selectedMovie is not null);
        }
    }

    private static bool IsLegacyLearningNotesPrompt(string prompt)
    {
        return !prompt.Contains("{audienceLevel}", StringComparison.OrdinalIgnoreCase)
            || !prompt.Contains("重要", StringComparison.Ordinal)
            || !prompt.Contains("未出力", StringComparison.Ordinal);
    }

    private async Task<int> GenerateLearningNotesAsync(Movie movie)
    {
        var videoPath = ResolveGenerationVideoPath(movie);
        var outputDirectory = NormalizeOptionalText(WhisperOutputDirectoryTextBox.Text)
            ?? GetDefaultSubtitleGenerationDirectory(movie);
        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        var englishSrtPath = ResolveEnglishSubtitlePath(movie, outputDirectory, baseName);

        var command = NormalizeOptionalText(TranslationCommandTextBox.Text) ?? DefaultTranslationCommand;
        var options = new LearningNotesGenerationOptions(
            movie.Title,
            videoPath,
            englishSrtPath,
            outputDirectory,
            command,
            NormalizeOptionalText(TranslationSourceLanguageTextBox.Text) ?? "en",
            NormalizeOptionalText(TranslationModelTextBox.Text) ?? DefaultCodexSparkModel,
            SelectedComboText(LearningNotesAudienceLevelComboBox, DefaultLearningNotesAudienceLevel),
            NormalizeOptionalText(LearningNotesPromptTextBox.Text) ?? DefaultLearningNotesPrompt,
            DefaultLearningNotesArguments,
            DefaultTranslationCommand,
            DefaultCodexSparkModel);
        var result = await _subtitleGenerationJobService.GenerateLearningNotesAsync(
            options,
            AppendSubtitleGenerationLog);

        var targetTrack = FindLearningNotesTargetTrack(movie, result.EnglishSrtPath);
        if (targetTrack is null)
        {
            AppendSubtitleGenerationLog("English subtitle track was not imported yet. Importing it before applying AI notes.");
            targetTrack = await ImportSubtitleAsync(movie, result.EnglishSrtPath);
        }

        var importedCount = await ImportLearningNotesAsync(movie, targetTrack, result.NotesOutputPath);
        AppendSubtitleGenerationLog($"AI notes imported into {targetTrack.Label}: {importedCount} cues.");
        return importedCount;
    }

    private async Task<int> ImportLearningNotesAsync(Movie movie, SubtitleTrack targetTrack, string notesOutputPath)
    {
        var json = await File.ReadAllTextAsync(notesOutputPath, Encoding.UTF8);
        var plan = LearningNotesImportService.CreateImportPlan(json, targetTrack.Cues);
        if (plan.RelocatedFocusNotes.Count > 0)
        {
            AppendSubtitleGenerationLog(
                "WARNING: relocated AI notes to the cue containing their focus: "
                + LearningNotesImportService.FormatTextSample(plan.RelocatedFocusNotes
                    .Select(note => $"{note.SourceIndex}->{note.TargetIndex}: {note.Focus}")
                    .ToList()));
        }

        if (plan.UnresolvedFocusNotes.Count > 0)
        {
            AppendSubtitleGenerationLog(
                "WARNING: skipped AI notes whose focus was not found in any cue: "
                + LearningNotesImportService.FormatTextSample(plan.UnresolvedFocusNotes
                    .Select(note => $"{note.Index}: {note.Focus}")
                    .ToList()));
        }

        var importedCount = 0;
        var notesToImport = plan.NotesToImport;
        var noteIndexes = notesToImport
            .Where(note => note.Index > 0)
            .Select(note => note.Index)
            .ToHashSet();
        foreach (var cue in targetTrack.Cues)
        {
            if (noteIndexes.Contains(cue.Index))
            {
                continue;
            }

            var existingState = FindCueLearningState(targetTrack, cue.Id, cue.Index);
            if (!string.IsNullOrWhiteSpace(existingState?.AiNote))
            {
                existingState.AiNote = null;
                existingState.UpdatedAt = DateTimeOffset.UtcNow;
                importedCount++;
            }
        }

        foreach (var note in notesToImport)
        {
            if (note.Index <= 0)
            {
                continue;
            }

            var cue = targetTrack.Cues.FirstOrDefault(candidate => candidate.Index == note.Index);
            if (cue is null)
            {
                continue;
            }

            var aiNote = LearningNotesImportService.NormalizeNoteText(note);
            if (aiNote is null)
            {
                var existingState = FindCueLearningState(targetTrack, cue.Id, cue.Index);
                if (!string.IsNullOrWhiteSpace(existingState?.AiNote))
                {
                    existingState.AiNote = null;
                    existingState.UpdatedAt = DateTimeOffset.UtcNow;
                    importedCount++;
                }

                continue;
            }

            var state = EnsureCueLearningState(targetTrack, cue.Id, cue.Index);
            if (string.Equals(state.AiNote, aiNote, StringComparison.Ordinal))
            {
                continue;
            }

            state.AiNote = aiNote;
            state.UpdatedAt = DateTimeOffset.UtcNow;
            importedCount++;
        }

        foreach (var unresolved in plan.UnresolvedFocusNotes)
        {
            var cue = targetTrack.Cues.FirstOrDefault(candidate => candidate.Index == unresolved.Index);
            if (cue is null)
            {
                continue;
            }

            var existingState = FindCueLearningState(targetTrack, cue.Id, cue.Index);
            if (!string.IsNullOrWhiteSpace(existingState?.AiNote))
            {
                existingState.AiNote = null;
                existingState.UpdatedAt = DateTimeOffset.UtcNow;
                importedCount++;
            }
        }

        if (importedCount > 0)
        {
            await _libraryStore.UpsertMovieAsync(movie);
        }

        return importedCount;
    }

    private static SubtitleTrack? FindLearningNotesTargetTrack(Movie movie, string englishSrtPath)
    {
        var sourceFileName = Path.GetFileName(englishSrtPath);
        var exactTrack = movie.SubtitleTracks.FirstOrDefault(track =>
            IsEnglishSubtitleTrack(track)
            && (string.Equals(track.SourceUri, englishSrtPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(track.SourceFileName, sourceFileName, StringComparison.OrdinalIgnoreCase)));
        if (exactTrack is not null)
        {
            return exactTrack;
        }

        var metadata = SubtitleFileMetadataService.Infer(sourceFileName);
        if (!string.IsNullOrWhiteSpace(metadata.GroupKey))
        {
            var groupedTrack = movie.SubtitleTracks.FirstOrDefault(track =>
                IsEnglishSubtitleTrack(track)
                && string.Equals(track.GroupKey, metadata.GroupKey, StringComparison.OrdinalIgnoreCase));
            if (groupedTrack is not null)
            {
                return groupedTrack;
            }
        }

        return movie.SubtitleTracks.FirstOrDefault(IsEnglishSubtitleTrack);
    }

}

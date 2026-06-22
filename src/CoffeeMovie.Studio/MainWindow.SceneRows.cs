using System.Globalization;
using System.Windows;
using System.Windows.Media;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Storage.Models;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
    private void RenderSceneRows(SubtitleTrack? subtitleTrack)
    {
        SetSceneGridGlobalMode(false);
        if (subtitleTrack is null)
        {
            ScenesDataGrid.ItemsSource = null;
            SceneResultsModeTextBlock.Text = "動画または字幕を選択してください";
            return;
        }

        var rows = subtitleTrack.Cues
            .Where(cue => !string.IsNullOrWhiteSpace(cue.Text))
            .Take(1000)
            .Select(cue =>
            {
                var learningState = FindCueLearningState(subtitleTrack, cue);
                return new SceneRow(cue, learningState, CreateSceneRowBackground(learningState, _subtitleTagHighlightColor));
            })
            .ToList();

        if (FlaggedOnlyCheckBox.IsChecked == true)
        {
            rows = rows.Where(row => row.IsFlagged).ToList();
        }

        var tagFilters = ParseTags(SceneTagFilterTextBox.Text);
        if (tagFilters.Count > 0)
        {
            rows = rows
                .Where(row => SceneRowMatchesTags(row, tagFilters))
                .ToList();
        }

        ScenesDataGrid.ItemsSource = rows;
        SceneResultsModeTextBlock.Text = $"{rows.Count} / {subtitleTrack.Cues.Count} cues";
    }

    private Task RenderGlobalSubtitleTagResultsAsync()
    {
        RenderGlobalSubtitleTagResults(_currentLibrary);
        return Task.CompletedTask;
    }

    private void RenderGlobalSubtitleTagResults(MovieLibrary library)
    {
        var subtitleTagFilters = ParseTags(SubtitleTagFilterTextBox.Text);
        if (subtitleTagFilters.Count == 0)
        {
            RenderSceneRows(_previewSubtitleTrack);
            return;
        }

        SetSceneGridGlobalMode(true);

        var sceneTagFilters = ParseTags(SceneTagFilterTextBox.Text);
        var allTagFilters = subtitleTagFilters
            .Concat(sceneTagFilters)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rows = new List<SceneRow>();
        var matchedMovies = library.Movies
            .Where(MatchesMovieFilters)
            .OrderBy(movie => string.IsNullOrWhiteSpace(movie.SeriesTitle) ? movie.Title : movie.SeriesTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(movie => movie.SeasonNumber ?? int.MaxValue)
            .ThenBy(movie => movie.EpisodeNumber ?? int.MaxValue)
            .ThenBy(movie => movie.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var movie in matchedMovies)
        {
            foreach (var track in movie.SubtitleTracks)
            {
                foreach (var cue in track.Cues.Where(cue => !string.IsNullOrWhiteSpace(cue.Text)))
                {
                    var learningState = FindCueLearningState(track, cue);
                    var row = new SceneRow(
                        cue,
                        learningState,
                        CreateSceneRowBackground(learningState, _subtitleTagHighlightColor),
                        movie,
                        track,
                        isGlobalResult: true);
                    if (FlaggedOnlyCheckBox.IsChecked == true && !row.IsFlagged)
                    {
                        continue;
                    }

                    if (!SceneRowMatchesTags(row, allTagFilters))
                    {
                        continue;
                    }

                    rows.Add(row);
                    if (rows.Count >= 1500)
                    {
                        break;
                    }
                }

                if (rows.Count >= 1500)
                {
                    break;
                }
            }

            if (rows.Count >= 1500)
            {
                break;
            }
        }

        ScenesDataGrid.ItemsSource = rows;
        var limitText = rows.Count >= 1500 ? " / 上限1500件" : string.Empty;
        SceneResultsModeTextBlock.Text = $"字幕タグ検索: {string.Join(", ", subtitleTagFilters)} / {rows.Count}件{limitText}";
    }

    private bool HasGlobalSubtitleTagFilter()
    {
        return ParseTags(SubtitleTagFilterTextBox.Text).Count > 0;
    }

    private void SetSceneGridGlobalMode(bool isGlobal)
    {
        SceneMovieColumn.Visibility = isGlobal ? Visibility.Visible : Visibility.Collapsed;
        SceneTrackColumn.Visibility = isGlobal ? Visibility.Visible : Visibility.Collapsed;
        ScenesDataGrid.IsReadOnly = isGlobal;
        SelectSelectedSceneTagsButton.IsEnabled = !isGlobal && _selectedMovie is not null;
        ClearSelectedSceneTagsButton.IsEnabled = !isGlobal && _selectedMovie is not null;
    }

    private static bool SceneRowMatchesTags(SceneRow row, IReadOnlyCollection<string> tagFilters)
    {
        if (tagFilters.Count == 0)
        {
            return true;
        }

        var tags = ParseTags(row.Tags);
        if (row.IsFlagged)
        {
            AddTag(tags, FlagTagName);
        }

        return tagFilters.All(filter => tags.Any(tag => ContainsText(tag, filter)));
    }

    private async Task SaveSceneRowLearningStateAsync(SceneRow row)
    {
        if (row.IsGlobalResult || _selectedMovie is null || _previewSubtitleTrack is null)
        {
            return;
        }

        var tags = ParseTags(row.Tags);
        if (row.IsFlagged)
        {
            AddTag(tags, FlagTagName);
        }
        else
        {
            tags.RemoveAll(tag => IsFlagTag(tag));
        }

        var note = NormalizeOptionalText(row.Note);
        var state = FindCueLearningState(_previewSubtitleTrack, row.CueId, row.CueIndex);
        if (state is null && !row.IsFlagged && tags.Count == 0 && note is null)
        {
            return;
        }

        state ??= EnsureCueLearningState(_previewSubtitleTrack, row.CueId, row.CueIndex);
        var isDirty = state.IsFlagged != row.IsFlagged
            || !state.Tags.SequenceEqual(tags, StringComparer.OrdinalIgnoreCase)
            || !string.Equals(state.Note, note, StringComparison.Ordinal);
        if (!isDirty)
        {
            return;
        }

        state.IsFlagged = tags.Any(IsFlagTag);
        state.Tags = tags;
        state.Note = note;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        row.IsFlagged = state.IsFlagged;
        row.Tags = string.Join(", ", tags);
        row.Note = note ?? string.Empty;

        await _libraryStore.UpsertMovieAsync(_selectedMovie);
        if (FlaggedOnlyCheckBox.IsChecked == true && !row.IsFlagged)
        {
            RenderSceneRows(_previewSubtitleTrack);
        }

        SetStatus("字幕の学習メタデータを保存しました。");
    }

    private async Task ShiftSelectedCueTimingAsync(int direction)
    {
        if (_selectedMovie is null || _previewSubtitleTrack is null || ScenesDataGrid.SelectedItem is not SceneRow row)
        {
            SetStatus("タイミングを調整する字幕行を選択してください。");
            return;
        }

        if (row.IsGlobalResult)
        {
            SetStatus("動画またぎの字幕タグ検索結果は、ダブルクリックで動画を開いてから編集してください。");
            return;
        }

        if (!TryGetTimingShiftMilliseconds(out var milliseconds))
        {
            SetStatus("タイミング補正値は 1 以上のミリ秒で入力してください。");
            return;
        }

        var offset = TimeSpan.FromMilliseconds(direction * milliseconds);
        var targetTracks = GetTimingShiftTargetTracks(_selectedMovie, _previewSubtitleTrack).ToList();
        var changedTracks = new List<SubtitleTrack>();
        var originalWriteCount = 0;
        TimeSpan? selectedStart = null;
        TimeSpan? selectedEnd = null;

        foreach (var track in targetTracks)
        {
            var cue = string.Equals(track.Id, _previewSubtitleTrack.Id, StringComparison.Ordinal)
                ? track.Cues.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, row.CueId, StringComparison.Ordinal)
                    || candidate.Index == row.CueIndex)
                : track.Cues.FirstOrDefault(candidate => candidate.Index == row.CueIndex);
            if (cue is null)
            {
                continue;
            }

            ShiftCue(cue, offset);
            if (string.Equals(track.Id, _previewSubtitleTrack.Id, StringComparison.Ordinal))
            {
                selectedStart = cue.Start;
                selectedEnd = cue.End;
            }

            changedTracks.Add(track);
            if (await RewriteSubtitleTrackFilesAsync(track, OriginalSubtitleWriteBackCheckBox.IsChecked == true))
            {
                originalWriteCount++;
            }
        }

        if (changedTracks.Count == 0)
        {
            SetStatus("調整対象の字幕が見つかりませんでした。");
            return;
        }

        RefreshMovieSceneMarkers(_selectedMovie);
        await _libraryStore.UpsertMovieAsync(_selectedMovie);
        RenderSceneRows(_previewSubtitleTrack);
        if (selectedStart is not null && selectedEnd is not null)
        {
            row.Timestamp = FormatCueEditTimestamp(selectedStart.Value);
            row.EndTimestamp = FormatCueEditTimestamp(selectedEnd.Value);
        }

        SelectSceneRow(row.CueId);
        UpdatePreviewSubtitleAtCurrentPosition();

        var directionText = direction > 0 ? "遅らせました" : "早めました";
        var syncText = changedTracks.Count > 1 ? $" / {changedTracks.Count} tracks synced" : string.Empty;
        var originalText = OriginalSubtitleWriteBackCheckBox.IsChecked == true
            ? $" / 原本更新 {originalWriteCount}"
            : string.Empty;
        SetStatus($"字幕タイミングを {milliseconds}ms {directionText}{syncText}{originalText}");
    }

    private async Task SetSelectedCueBoundaryFromPreviewAsync(bool setStart)
    {
        if (PreviewPlayer.Source is null || ScenesDataGrid.SelectedItem is not SceneRow row)
        {
            SetStatus("プレビュー再生中に字幕行を選択してください。");
            return;
        }

        if (row.IsGlobalResult)
        {
            SetStatus("動画またぎの字幕タグ検索結果は、ダブルクリックで動画を開いてから編集してください。");
            return;
        }

        var value = FormatCueEditTimestamp(PreviewPlayer.Position);
        if (setStart)
        {
            row.Timestamp = value;
        }
        else
        {
            row.EndTimestamp = value;
        }

        await SaveSceneRowTimingAsync(row);
    }

    private async Task SaveSceneRowTimingAsync(SceneRow row)
    {
        if (row.IsGlobalResult || _selectedMovie is null || _previewSubtitleTrack is null)
        {
            return;
        }

        if (!TryParseCueTimestamp(row.Timestamp, out var start)
            || !TryParseCueTimestamp(row.EndTimestamp, out var end))
        {
            SetStatus("Start / End は 01:23.456 または 00:01:23.456 の形式で入力してください。");
            return;
        }

        if (end <= start)
        {
            SetStatus("End は Start より後にしてください。");
            return;
        }

        var targetTracks = GetTimingShiftTargetTracks(_selectedMovie, _previewSubtitleTrack).ToList();
        var changedTracks = new List<SubtitleTrack>();
        var originalWriteCount = 0;

        foreach (var track in targetTracks)
        {
            var cue = string.Equals(track.Id, _previewSubtitleTrack.Id, StringComparison.Ordinal)
                ? track.Cues.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, row.CueId, StringComparison.Ordinal)
                    || candidate.Index == row.CueIndex)
                : track.Cues.FirstOrDefault(candidate => candidate.Index == row.CueIndex);
            if (cue is null)
            {
                continue;
            }

            cue.Start = start;
            cue.End = end;
            changedTracks.Add(track);
            if (await RewriteSubtitleTrackFilesAsync(track, OriginalSubtitleWriteBackCheckBox.IsChecked == true))
            {
                originalWriteCount++;
            }
        }

        if (changedTracks.Count == 0)
        {
            SetStatus("調整対象の字幕が見つかりませんでした。");
            return;
        }

        RefreshMovieSceneMarkers(_selectedMovie);
        await _libraryStore.UpsertMovieAsync(_selectedMovie);
        row.Timestamp = FormatCueEditTimestamp(start);
        row.EndTimestamp = FormatCueEditTimestamp(end);
        RenderSceneRows(_previewSubtitleTrack);
        SelectSceneRow(row.CueId);
        UpdatePreviewSubtitleAtCurrentPosition();

        var syncText = changedTracks.Count > 1 ? $" / {changedTracks.Count} tracks synced" : string.Empty;
        var originalText = OriginalSubtitleWriteBackCheckBox.IsChecked == true
            ? $" / 原本更新 {originalWriteCount}"
            : string.Empty;
        SetStatus($"字幕タイミングを保存しました: {row.Timestamp} - {row.EndTimestamp}{syncText}{originalText}");
    }

    private bool TryGetTimingShiftMilliseconds(out int milliseconds)
    {
        return int.TryParse(TimingShiftTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out milliseconds)
            && milliseconds > 0
            && milliseconds <= 60_000;
    }

    private IEnumerable<SubtitleTrack> GetTimingShiftTargetTracks(Movie movie, SubtitleTrack selectedTrack)
    {
        yield return selectedTrack;

        if (SyncPairedSubtitleCheckBox.IsChecked != true)
        {
            yield break;
        }

        foreach (var track in movie.SubtitleTracks)
        {
            if (!string.Equals(track.Id, selectedTrack.Id, StringComparison.Ordinal)
                && HasSameSubtitleGroup(selectedTrack, track))
            {
                yield return track;
            }
        }
    }

    private static void ShiftCue(SubtitleCue cue, TimeSpan offset)
    {
        var duration = cue.End > cue.Start
            ? cue.End - cue.Start
            : TimeSpan.FromMilliseconds(1);
        var start = cue.Start + offset;
        if (start < TimeSpan.Zero)
        {
            start = TimeSpan.Zero;
        }

        cue.Start = start;
        cue.End = start + duration;
    }

    private void SelectSceneRow(string cueId)
    {
        if (ScenesDataGrid.ItemsSource is not IEnumerable<SceneRow> rows)
        {
            return;
        }

        var row = rows.FirstOrDefault(candidate => string.Equals(candidate.CueId, cueId, StringComparison.Ordinal));
        if (row is null)
        {
            return;
        }

        ScenesDataGrid.SelectedItem = row;
        ScenesDataGrid.ScrollIntoView(row);
    }

    private static SubtitleCueLearningState? FindCueLearningState(SubtitleTrack track, SubtitleCue cue)
    {
        return FindCueLearningState(track, cue.Id, cue.Index);
    }

    private static SubtitleCueLearningState? FindCueLearningState(SubtitleTrack track, string cueId, int cueIndex)
    {
        return track.CueLearningStates.FirstOrDefault(state =>
            string.Equals(state.CueId, cueId, StringComparison.Ordinal)
            || state.CueIndex == cueIndex);
    }

    private static SubtitleCueLearningState EnsureCueLearningState(SubtitleTrack track, string cueId, int cueIndex)
    {
        var state = FindCueLearningState(track, cueId, cueIndex);
        if (state is not null)
        {
            if (string.IsNullOrWhiteSpace(state.CueId))
            {
                state.CueId = cueId;
            }

            return state;
        }

        state = new SubtitleCueLearningState
        {
            CueId = cueId,
            CueIndex = cueIndex
        };
        track.CueLearningStates.Add(state);
        return state;
    }

    private static bool IsFlaggedLearningState(SubtitleCueLearningState? state)
    {
        return state?.IsFlagged == true || state?.Tags.Any(IsFlagTag) == true;
    }

    private static bool HasSubtitleTags(SubtitleCueLearningState? state)
    {
        return state?.IsFlagged == true || state?.Tags.Count > 0;
    }

    private static System.Windows.Media.Brush CreateSceneRowBackground(SubtitleCueLearningState? state, string highlightColor)
    {
        return HasSubtitleTags(state)
            ? CreateBrush(highlightColor, 0x4D)
            : new SolidColorBrush(Color.FromRgb(0x0B, 0x11, 0x1A));
    }
}

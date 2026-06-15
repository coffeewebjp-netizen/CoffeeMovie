using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
    private async void OnToggleLearningNotesClicked(object sender, RoutedEventArgs e)
    {
        _showLearningNotes = !_showLearningNotes;
        UpdateLearningNotesButton();
        UpdatePreviewSubtitleAtCurrentPosition();
        UpdateFullPreviewSubtitle(FullPreviewPlayer.Position);
        await SaveStudioPreferencesAsync();
    }

    private async void OnOverlayPositionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingPreferences)
        {
            return;
        }

        ReadOverlayPositionComboBoxes();
        UpdatePreviewSubtitleAtCurrentPosition();
        UpdateFullPreviewSubtitle(FullPreviewPlayer.Position);
        await SaveStudioPreferencesAsync();
    }

    private async void OnResetOverlayLayoutClicked(object sender, RoutedEventArgs e)
    {
        SetDefaultOverlayPositions();
        _isUpdatingPreferences = true;
        try
        {
            ApplyOverlayPositionComboBoxes();
        }
        finally
        {
            _isUpdatingPreferences = false;
        }

        UpdatePreviewSubtitleAtCurrentPosition();
        UpdateFullPreviewSubtitle(FullPreviewPlayer.Position);
        await SaveStudioPreferencesAsync();
        SetStatus("表示位置を既定に戻しました。");
    }

    private async void OnToggleDualSubtitleClicked(object sender, RoutedEventArgs e)
    {
        _showDualSubtitles = !_showDualSubtitles;
        UpdateDualSubtitleButton();
        UpdatePreviewSubtitleAtCurrentPosition();
        UpdateFullPreviewSubtitle(FullPreviewPlayer.Position);
        await SaveStudioPreferencesAsync();
    }

    private async void OnHighlightColorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingPreferences || HighlightColorComboBox.SelectedValue is not string color)
        {
            return;
        }

        _subtitleTagHighlightColor = color;
        RenderSceneRows(_previewSubtitleTrack);
        UpdatePreviewSubtitleAtCurrentPosition();
        await SaveStudioPreferencesAsync();
    }

    private void OnPlayPreviewClicked(object sender, RoutedEventArgs e)
    {
        _previewStopAt = null;
        StartPreview();
    }

    private void OnPausePreviewClicked(object sender, RoutedEventArgs e)
    {
        TogglePreviewPlayback();
    }

    private void OnStopPreviewClicked(object sender, RoutedEventArgs e)
    {
        _previewTimer.Stop();
        _playPreviewWhenMediaOpened = false;
        _isPreviewPlaying = false;
        _previewStopAt = null;
        PreviewPlayer.Stop();
        SetPreviewSeek(TimeSpan.Zero);
        HidePreviewSubtitle();
        UpdatePlaybackButtonContent();
        SetStatus("プレビューを停止しました。");
    }

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.Handled || IsInteractiveInputFocused(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (FullPreviewTabItem.IsSelected)
        {
            ToggleFullPreviewPlayback();
        }
        else if (EditTabItem.IsSelected)
        {
            TogglePreviewPlayback();
        }

        e.Handled = true;
    }

    private void OnPreviewSubtitleClicked(object sender, MouseButtonEventArgs e)
    {
        if (_currentPreviewCue is not null)
        {
            JumpPreviewTo(_currentPreviewCue.Start);
        }
    }

    private void OnSceneMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ScenesDataGrid.SelectedItem is SceneRow row)
        {
            JumpPreviewTo(row.Start);
        }
    }

    private async void OnSceneCurrentCellChanged(object sender, EventArgs e)
    {
        if (_isUpdatingSelection
            || _selectedMovie is null
            || _previewSubtitleTrack is null
            || ScenesDataGrid.CurrentItem is not SceneRow row)
        {
            return;
        }

        await SaveSceneRowLearningStateAsync(row);
    }

    private async void OnSceneCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_isUpdatingSelection || e.Row.Item is not SceneRow row)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        var header = e.Column.Header?.ToString();
        if (string.Equals(header, "Start", StringComparison.OrdinalIgnoreCase)
            || string.Equals(header, "End", StringComparison.OrdinalIgnoreCase))
        {
            await SaveSceneRowTimingAsync(row);
            return;
        }

        if (string.Equals(e.Column.Header?.ToString(), "Tags", StringComparison.OrdinalIgnoreCase))
        {
            row.IsFlagged = ParseTags(row.Tags).Any(IsFlagTag);
        }

        await SaveSceneRowLearningStateAsync(row);
    }

    private void OnFlaggedOnlyChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSelection)
        {
            return;
        }

        RenderSceneRows(_previewSubtitleTrack);
    }

    private void OnSceneFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingSelection)
        {
            return;
        }

        RenderSceneRows(_previewSubtitleTrack);
    }

    private void OnPlayNextFlaggedCueClicked(object sender, RoutedEventArgs e)
    {
        if (_previewSubtitleTrack is null)
        {
            SetStatus("字幕を選択してください。");
            return;
        }

        var flaggedCues = _previewSubtitleTrack.Cues
            .Where(cue => IsFlaggedLearningState(FindCueLearningState(_previewSubtitleTrack, cue)))
            .OrderBy(cue => cue.Start)
            .ToList();
        if (flaggedCues.Count == 0)
        {
            SetStatus("flagタグ付き字幕がありません。");
            return;
        }

        var currentPosition = PreviewPlayer.Source is null ? TimeSpan.Zero : PreviewPlayer.Position;
        var nextCue = flaggedCues.FirstOrDefault(cue => cue.Start > currentPosition.Add(TimeSpan.FromMilliseconds(250)))
            ?? flaggedCues[0];

        SelectSceneRow(nextCue.Id);
        JumpPreviewTo(nextCue.Start);
    }

    private async void OnShiftSelectedCueEarlierClicked(object sender, RoutedEventArgs e)
    {
        await ShiftSelectedCueTimingAsync(-1);
    }

    private async void OnShiftSelectedCueLaterClicked(object sender, RoutedEventArgs e)
    {
        await ShiftSelectedCueTimingAsync(1);
    }

    private async void OnSetSelectedCueStartFromPreviewClicked(object sender, RoutedEventArgs e)
    {
        await SetSelectedCueBoundaryFromPreviewAsync(setStart: true);
    }

    private async void OnSetSelectedCueEndFromPreviewClicked(object sender, RoutedEventArgs e)
    {
        await SetSelectedCueBoundaryFromPreviewAsync(setStart: false);
    }

    private void OnPreviewMediaOpened(object sender, RoutedEventArgs e)
    {
        _isPreviewMediaOpened = true;
        if (PreviewPlayer.NaturalDuration.HasTimeSpan)
        {
            _previewDuration = PreviewPlayer.NaturalDuration.TimeSpan;
            PreviewSeekSlider.Maximum = Math.Max(1.0, _previewDuration.TotalSeconds);
            PreviewSeekSlider.IsEnabled = _previewDuration > TimeSpan.Zero;
        }
        else
        {
            ResetPreviewSeek();
        }

        if (_pendingPreviewSeek is { } pendingPosition)
        {
            _pendingPreviewSeek = null;
            SeekPreviewTo(pendingPosition);
        }
        else
        {
            SetPreviewSeek(PreviewPlayer.Position);
        }

        if (_playPreviewWhenMediaOpened)
        {
            _playPreviewWhenMediaOpened = false;
            PreviewPlayer.Play();
            _isPreviewPlaying = true;
            _previewTimer.Start();
            UpdatePlaybackButtonContent();
            SetStatus("プレビュー再生中です。");
            return;
        }

        SetStatus("プレビューの準備ができました。");
    }

    private void OnPreviewMediaEnded(object sender, RoutedEventArgs e)
    {
        _previewTimer.Stop();
        _playPreviewWhenMediaOpened = false;
        _isPreviewPlaying = false;
        PreviewPlayer.Stop();
        SetPreviewSeek(TimeSpan.Zero);
        UpdatePlaybackButtonContent();
        SetStatus("プレビューが終了しました。");
    }

    private void OnPreviewSeekStarted(object sender, MouseButtonEventArgs e)
    {
        BeginPreviewSeek();
    }

    private void OnPreviewSeekCompleted(object sender, MouseButtonEventArgs e)
    {
        CompletePreviewSeek();
    }

    private void OnPreviewSeekDragStarted(object sender, DragStartedEventArgs e)
    {
        BeginPreviewSeek();
    }

    private void OnPreviewSeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        CompletePreviewSeek();
    }

    private void OnPreviewSeekLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isPreviewSeeking && Mouse.LeftButton != MouseButtonState.Pressed)
        {
            CompletePreviewSeek();
        }
    }

    private void OnPreviewSeekKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (PreviewSeekSlider.IsEnabled)
        {
            SeekPreviewToSliderValue();
        }
    }

    private void OnPreviewSeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingPreviewSlider)
        {
            return;
        }

        var position = TimeSpan.FromSeconds(Math.Clamp(e.NewValue, 0.0, PreviewSeekSlider.Maximum));
        PreviewPositionTextBlock.Text = FormatPlaybackPosition(position, _previewDuration);
        UpdatePreviewSubtitle(position);
        if (_isPreviewSeeking && PreviewPlayer.Source is not null && _previewDuration > TimeSpan.Zero)
        {
            PreviewPlayer.Position = ClampPreviewPosition(position);
        }
    }

    private void OnFullPreviewPlayClicked(object sender, RoutedEventArgs e)
    {
        StartFullPreview();
    }

    private void OnPauseFullPreviewClicked(object sender, RoutedEventArgs e)
    {
        ToggleFullPreviewPlayback();
    }

    private void OnFullPreviewStopClicked(object sender, RoutedEventArgs e)
    {
        _playFullPreviewWhenMediaOpened = false;
        _isFullPreviewSeeking = false;
        _isFullPreviewPlaying = false;
        FullPreviewPlayer.Stop();
        SetFullPreviewSeek(TimeSpan.Zero);
        HideFullPreviewSubtitle();
        UpdatePlaybackButtonContent();
        SetStatus("フルプレビューを停止しました。");
    }

    private void OnFullPreviewMediaOpened(object sender, RoutedEventArgs e)
    {
        _isFullPreviewMediaOpened = true;
        if (FullPreviewPlayer.NaturalDuration.HasTimeSpan)
        {
            _fullPreviewDuration = FullPreviewPlayer.NaturalDuration.TimeSpan;
            FullPreviewSeekSlider.Maximum = Math.Max(1.0, _fullPreviewDuration.TotalSeconds);
            FullPreviewSeekSlider.IsEnabled = _fullPreviewDuration > TimeSpan.Zero;
        }
        else
        {
            ResetFullPreviewSeek();
        }

        if (_pendingFullPreviewSeek is { } pendingPosition)
        {
            _pendingFullPreviewSeek = null;
            SeekFullPreviewTo(pendingPosition);
        }
        else
        {
            SetFullPreviewSeek(FullPreviewPlayer.Position);
        }

        if (_playFullPreviewWhenMediaOpened)
        {
            _playFullPreviewWhenMediaOpened = false;
            FullPreviewPlayer.Play();
            _isFullPreviewPlaying = true;
            _previewTimer.Start();
            UpdatePlaybackButtonContent();
            SetStatus("フルプレビュー再生中です。");
            return;
        }

        SetStatus("フルプレビューの準備ができました。");
    }

    private void OnFullPreviewMediaEnded(object sender, RoutedEventArgs e)
    {
        _playFullPreviewWhenMediaOpened = false;
        _isFullPreviewPlaying = false;
        FullPreviewPlayer.Stop();
        SetFullPreviewSeek(TimeSpan.Zero);
        UpdatePlaybackButtonContent();
        SetStatus("フルプレビューが終了しました。");
    }

    private void OnFullPreviewSeekStarted(object sender, MouseButtonEventArgs e)
    {
        BeginFullPreviewSeek();
    }

    private void OnFullPreviewSeekCompleted(object sender, MouseButtonEventArgs e)
    {
        CompleteFullPreviewSeek();
    }

    private void OnFullPreviewSeekDragStarted(object sender, DragStartedEventArgs e)
    {
        BeginFullPreviewSeek();
    }

    private void OnFullPreviewSeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        CompleteFullPreviewSeek();
    }

    private void OnFullPreviewSeekLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isFullPreviewSeeking && Mouse.LeftButton != MouseButtonState.Pressed)
        {
            CompleteFullPreviewSeek();
        }
    }

    private void OnFullPreviewSeekKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (FullPreviewSeekSlider.IsEnabled)
        {
            SeekFullPreviewToSliderValue();
        }
    }

    private void OnFullPreviewSeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingFullPreviewSlider)
        {
            return;
        }

        var position = TimeSpan.FromSeconds(Math.Clamp(e.NewValue, 0.0, FullPreviewSeekSlider.Maximum));
        FullPreviewPositionTextBlock.Text = FormatPlaybackPosition(position, _fullPreviewDuration);
        UpdateFullPreviewSubtitle(position);
        if (_isFullPreviewSeeking && FullPreviewPlayer.Source is not null && _fullPreviewDuration > TimeSpan.Zero)
        {
            FullPreviewPlayer.Position = ClampFullPreviewPosition(position);
        }
    }

}

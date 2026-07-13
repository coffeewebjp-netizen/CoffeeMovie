using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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
        SyncPreviewPopupFromActiveSurface(forceSeek: true);
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
        SyncPreviewPopupFromActiveSurface(forceSeek: true);
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
        SyncPreviewPopupFromActiveSurface(forceSeek: true);
        await SaveStudioPreferencesAsync();
        SetStatus("表示位置を既定に戻しました。");
    }

    private async void OnToggleDualSubtitleClicked(object sender, RoutedEventArgs e)
    {
        _showDualSubtitles = !_showDualSubtitles;
        UpdateDualSubtitleButton();
        UpdatePreviewSubtitleAtCurrentPosition();
        UpdateFullPreviewSubtitle(FullPreviewPlayer.Position);
        SyncPreviewPopupFromActiveSurface(forceSeek: true);
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
        SyncPreviewPopupFromActiveSurface(forceSeek: true);
        await SaveStudioPreferencesAsync();
    }

    private void OnPlayPreviewClicked(object sender, RoutedEventArgs e)
    {
        _previewStopAt = null;
        _previewStallRecoveryCount = 0;
        ReloadPreviewMedia(TimeSpan.Zero, shouldPlay: true, "最初から再生します...");
    }

    private void OnPausePreviewClicked(object sender, RoutedEventArgs e)
    {
        TogglePreviewPlayback();
    }

    private void OnStopPreviewClicked(object sender, RoutedEventArgs e)
    {
        CaptureActivePlaybackPosition(force: true);
        _previewTimer.Stop();
        _playPreviewWhenMediaOpened = false;
        _isPreviewPlaying = false;
        _previewStopAt = null;
        _previewSeekConfirmationTarget = null;
        _isRecoveringPreviewMedia = false;
        _isPreviewAudioSuppressed = false;
        UpdatePreviewAudioRouting();
        PreviewPlayer.Stop();
        SetPreviewSeek(TimeSpan.Zero);
        HidePreviewSubtitle();
        UpdatePlaybackButtonContent();
        SetStatus("プレビューを停止しました。");
    }

    private void OnPreviewSeekOffsetClicked(object sender, RoutedEventArgs e)
    {
        if (TryGetPreviewSeekOffset(sender, out var offsetSeconds))
        {
            SeekPreviewBySeconds(offsetSeconds);
        }
    }

    private void OnFullPreviewSeekOffsetClicked(object sender, RoutedEventArgs e)
    {
        if (TryGetPreviewSeekOffset(sender, out var offsetSeconds))
        {
            SeekFullPreviewBySeconds(offsetSeconds);
        }
    }

    private async void OnCustomPreviewSeekSecondsLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            await SaveCustomPreviewSeekSecondsAsync(textBox.Text);
        }
    }

    private async void OnCustomPreviewSeekSecondsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox textBox)
        {
            return;
        }

        await SaveCustomPreviewSeekSecondsAsync(textBox.Text);
        e.Handled = true;
    }

    private async Task SaveCustomPreviewSeekSecondsAsync(string? value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            UpdateCustomPreviewSeekControls();
            SetStatus("\u30AB\u30B9\u30BF\u30E0\u79D2\u306F1\u301C9999\u306E\u6574\u6570\u3067\u6307\u5B9A\u3057\u3066\u304F\u3060\u3055\u3044\u3002");
            return;
        }

        var normalized = NormalizeCustomPreviewSeekSeconds(seconds);
        var changed = _customPreviewSeekSeconds != normalized;
        _customPreviewSeekSeconds = normalized;
        UpdateCustomPreviewSeekControls();
        if (changed)
        {
            await SaveStudioPreferencesAsync();
        }
    }

    private bool TryGetPreviewSeekOffset(object sender, out int offsetSeconds)
    {
        offsetSeconds = 0;
        if (sender is not FrameworkElement { Tag: string value }
            || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var multiplier))
        {
            return false;
        }

        offsetSeconds = multiplier switch
        {
            -1 => -1,
            -5 => -5,
            1 => 1,
            5 => 5,
            -100 => -_customPreviewSeekSeconds,
            100 => _customPreviewSeekSeconds,
            _ => 0
        };
        return offsetSeconds != 0;
    }

    private void OnOpenPreviewPopupClicked(object sender, RoutedEventArgs e)
    {
        if (_previewPopupWindow is not null)
        {
            _previewPopupWindow.Activate();
            SyncPreviewPopupFromActiveSurface(forceSeek: true);
            return;
        }

        _previewPopupWindow = new FullPreviewPopupWindow
        {
            Owner = this
        };
        _previewPopupWindow.Closed += (_, _) =>
        {
            _previewPopupWindow = null;
            _previewPopupVideoPath = null;
            _previewPopupVideoAvailable = false;
            UpdatePreviewAudioRouting();
        };
        _previewPopupWindow.Show();
        UpdatePreviewAudioRouting();
        SyncPreviewPopupFromActiveSurface(forceSeek: true);
        SetStatus("別窓プレビューを開きました。");
    }

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Handled || IsInteractiveInputFocused(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (e.Key == Key.Space)
        {
            if (FullPreviewTabItem.IsSelected)
            {
                ToggleFullPreviewPlayback();
            }
            else if (EditTabItem.IsSelected)
            {
                TogglePreviewPlayback();
            }

            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Left or Key.Right))
        {
            return;
        }

        var seconds = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            ? 1
            : Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                ? 5
                : _customPreviewSeekSeconds;
        var offset = e.Key == Key.Left ? -seconds : seconds;
        e.Handled = SeekActivePreviewBySeconds(offset);
    }

    private void OnMainTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabControl) || _selectedMovie is null)
        {
            return;
        }

        if (FullPreviewTabItem.IsSelected)
        {
            SyncFullPreviewFromEdit(transferPlayback: true);
        }
        else if (EditTabItem.IsSelected)
        {
            SyncEditPreviewFromFull(transferPlayback: true);
        }

        SyncPreviewPopupFromActiveSurface(forceSeek: true);
    }

    private async void OnPreviewSubtitleClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PreviewOverlayItem item })
        {
            e.Handled = true;
            await RegisterPreviewCueInCoffeeLearningAsync(item);
        }
    }

    private async void OnSceneMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ScenesDataGrid.SelectedItem is not SceneRow row)
        {
            return;
        }

        if (row.IsGlobalResult)
        {
            await OpenGlobalSceneRowAsync(row);
            return;
        }

        if (IsSceneEditableTextCell(e.OriginalSource as DependencyObject))
        {
            e.Handled = true;
            ScenesDataGrid.BeginEdit(e);
            return;
        }

        JumpPreviewTo(row.Start);
    }

    private static bool IsSceneEditableTextCell(DependencyObject? source)
    {
        var cell = FindVisualParent<DataGridCell>(source);
        if (cell?.Column is not DataGridBoundColumn boundColumn
            || boundColumn.Binding is not Binding binding)
        {
            return false;
        }

        var path = binding.Path?.Path;
        return string.Equals(path, nameof(SceneRow.AiNote), StringComparison.Ordinal)
            || string.Equals(path, nameof(SceneRow.Note), StringComparison.Ordinal);
    }

    private static T? FindVisualParent<T>(DependencyObject? source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            try
            {
                current = VisualTreeHelper.GetParent(current);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        return null;
    }

    private async void OnSceneCurrentCellChanged(object sender, EventArgs e)
    {
        if (_isUpdatingSelection
            || _selectedMovie is null
            || _previewSubtitleTrack is null
            || ScenesDataGrid.CurrentItem is not SceneRow row
            || row.IsGlobalResult)
        {
            return;
        }

        await SaveSceneRowLearningStateAsync(row);
    }

    private async void OnSceneCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_isUpdatingSelection || e.Row.Item is not SceneRow row || row.IsGlobalResult)
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

    private async void OnFlaggedOnlyChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingSelection)
        {
            return;
        }

        if (HasGlobalSubtitleTagFilter())
        {
            await RenderGlobalSubtitleTagResultsAsync();
            return;
        }

        RenderSceneRows(_previewSubtitleTrack);
    }

    private async void OnSceneFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingSelection)
        {
            return;
        }

        if (HasGlobalSubtitleTagFilter())
        {
            await RenderGlobalSubtitleTagResultsAsync();
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

        var pendingPosition = _pendingPreviewSeek;
        _pendingPreviewSeek = null;
        var shouldPlay = _playPreviewWhenMediaOpened;
        _playPreviewWhenMediaOpened = false;
        if (shouldPlay)
        {
            PreviewPlayer.Play();
            _isPreviewPlaying = true;
            _previewTimer.Start();
            ResetPreviewPlaybackHealth(PreviewPlayer.Position);
        }

        if (pendingPosition is { } position)
        {
            SetPreviewSeek(position);
            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(() =>
                {
                    if (PreviewPlayer.Source is not null && _isPreviewMediaOpened)
                    {
                        SeekPreviewTo(position);
                    }
                }));
        }
        else
        {
            SetPreviewSeek(PreviewPlayer.Position);
        }

        UpdatePlaybackButtonContent();
        SetStatus(shouldPlay ? "プレビュー再生中です。" : "プレビューの準備ができました。");
    }

    private void OnPreviewMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _previewSeekConfirmationTarget = null;
        _isRecoveringPreviewMedia = false;
        _isPreviewAudioSuppressed = false;
        UpdatePreviewAudioRouting();
        _isPreviewPlaying = false;
        _previewTimer.Stop();
        UpdatePlaybackButtonContent();
        SetStatus($"プレビュー再生エラー: {e.ErrorException?.Message ?? "不明なメディアエラー"}");
    }

    private void OnPreviewMediaEnded(object sender, RoutedEventArgs e)
    {
        CaptureActivePlaybackPosition(force: true, ended: true);
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
        if (IsSliderThumbInput(e.OriginalSource as DependencyObject, PreviewSeekSlider))
        {
            return;
        }

        PreviewSeekSlider.Value = GetSliderClickValue(PreviewSeekSlider, e);
        CompletePreviewSeek();
        e.Handled = true;
    }

    private static bool IsSliderThumbInput(DependencyObject? source, Slider slider)
    {
        while (source is not null && !ReferenceEquals(source, slider))
        {
            if (source is Thumb)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static double GetSliderClickValue(Slider slider, MouseButtonEventArgs e)
    {
        var width = Math.Max(1d, slider.ActualWidth);
        var ratio = Math.Clamp(e.GetPosition(slider).X / width, 0d, 1d);
        if (slider.FlowDirection == FlowDirection.RightToLeft)
        {
            ratio = 1d - ratio;
        }

        return slider.Minimum + ((slider.Maximum - slider.Minimum) * ratio);
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
    }

    private void OnFullPreviewPlayClicked(object sender, RoutedEventArgs e)
    {
        _fullPreviewStallRecoveryCount = 0;
        ReloadFullPreviewMedia(TimeSpan.Zero, shouldPlay: true, "最初から再生します...");
    }

    private void OnPauseFullPreviewClicked(object sender, RoutedEventArgs e)
    {
        ToggleFullPreviewPlayback();
    }

    private void OnFullPreviewStopClicked(object sender, RoutedEventArgs e)
    {
        CaptureActivePlaybackPosition(force: true);
        _playFullPreviewWhenMediaOpened = false;
        _isFullPreviewSeeking = false;
        _isFullPreviewPlaying = false;
        _fullPreviewSeekConfirmationTarget = null;
        _isRecoveringFullPreviewMedia = false;
        _isFullPreviewAudioSuppressed = false;
        UpdatePreviewAudioRouting();
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

        var pendingPosition = _pendingFullPreviewSeek;
        _pendingFullPreviewSeek = null;
        var shouldPlay = _playFullPreviewWhenMediaOpened;
        _playFullPreviewWhenMediaOpened = false;
        if (shouldPlay)
        {
            FullPreviewPlayer.Play();
            _isFullPreviewPlaying = true;
            _previewTimer.Start();
            ResetFullPreviewPlaybackHealth(FullPreviewPlayer.Position);
        }

        if (pendingPosition is { } position)
        {
            SetFullPreviewSeek(position);
            Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(() =>
                {
                    if (FullPreviewPlayer.Source is not null && _isFullPreviewMediaOpened)
                    {
                        SeekFullPreviewTo(position);
                    }
                }));
        }
        else
        {
            SetFullPreviewSeek(FullPreviewPlayer.Position);
        }

        UpdatePlaybackButtonContent();
        SetStatus(shouldPlay ? "フルプレビュー再生中です。" : "フルプレビューの準備ができました。");
    }

    private void OnFullPreviewMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _fullPreviewSeekConfirmationTarget = null;
        _isRecoveringFullPreviewMedia = false;
        _isFullPreviewAudioSuppressed = false;
        UpdatePreviewAudioRouting();
        _isFullPreviewPlaying = false;
        UpdatePlaybackButtonContent();
        SetStatus($"フルプレビュー再生エラー: {e.ErrorException?.Message ?? "不明なメディアエラー"}");
    }

    private void OnFullPreviewMediaEnded(object sender, RoutedEventArgs e)
    {
        CaptureActivePlaybackPosition(force: true, ended: true);
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
        if (IsSliderThumbInput(e.OriginalSource as DependencyObject, FullPreviewSeekSlider))
        {
            return;
        }

        FullPreviewSeekSlider.Value = GetSliderClickValue(FullPreviewSeekSlider, e);
        CompleteFullPreviewSeek();
        e.Handled = true;
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
    }

}

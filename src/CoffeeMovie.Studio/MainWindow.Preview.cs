using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CoffeeMovie.Core.Models;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
    private void UpdatePreviewSubtitleAtCurrentPosition()
    {
        if (PreviewPlayer.Source is null)
        {
            HidePreviewSubtitle();
            return;
        }

        UpdatePreviewSubtitle(PreviewPlayer.Position);
    }

    private void UpdatePreviewSubtitle(TimeSpan position)
    {
        var lines = CreatePreviewSubtitleLines(position);
        if (lines.Count == 0)
        {
            HidePreviewSubtitle();
            return;
        }

        _currentPreviewCue = lines[0].Cue;
        RenderOverlayPanels(
            PreviewAboveOverlayPanel,
            PreviewBelowOverlayPanel,
            CreateOverlayItems(position, lines),
            isFullPreview: false);
        PreviewSubtitleOverlay.Visibility = Visibility.Collapsed;
        PreviewLearningNoteOverlay.Visibility = Visibility.Collapsed;
    }

    private void HidePreviewSubtitle()
    {
        _currentPreviewCue = null;
        PreviewAboveOverlayPanel.Children.Clear();
        PreviewAboveOverlayPanel.Visibility = Visibility.Collapsed;
        PreviewBelowOverlayPanel.Children.Clear();
        PreviewBelowOverlayPanel.Visibility = Visibility.Collapsed;
        PreviewSubtitlePrimaryTextBlock.Text = string.Empty;
        PreviewSubtitleSecondaryTextBlock.Text = string.Empty;
        PreviewSubtitleSecondaryTextBlock.Visibility = Visibility.Collapsed;
        PreviewSubtitleOverlay.BorderThickness = new Thickness(0);
        PreviewSubtitleOverlay.Visibility = Visibility.Collapsed;
        HidePreviewLearningNoteOverlay();
    }

    private void UpdateFullPreviewSubtitle(TimeSpan position)
    {
        var lines = CreatePreviewSubtitleLines(position);
        if (lines.Count == 0)
        {
            HideFullPreviewSubtitle();
            return;
        }

        RenderOverlayPanels(
            FullPreviewAboveOverlayPanel,
            FullPreviewBelowOverlayPanel,
            CreateOverlayItems(position, lines),
            isFullPreview: true);
        FullPreviewSubtitleOverlay.Visibility = Visibility.Collapsed;
        FullPreviewLearningNoteOverlay.Visibility = Visibility.Collapsed;
    }

    private void HideFullPreviewSubtitle()
    {
        FullPreviewAboveOverlayPanel.Children.Clear();
        FullPreviewAboveOverlayPanel.Visibility = Visibility.Collapsed;
        FullPreviewBelowOverlayPanel.Children.Clear();
        FullPreviewBelowOverlayPanel.Visibility = Visibility.Collapsed;
        FullPreviewSubtitlePrimaryTextBlock.Text = string.Empty;
        FullPreviewSubtitleSecondaryTextBlock.Text = string.Empty;
        FullPreviewSubtitleSecondaryTextBlock.Visibility = Visibility.Collapsed;
        FullPreviewSubtitleOverlay.BorderThickness = new Thickness(0);
        FullPreviewSubtitleOverlay.Visibility = Visibility.Collapsed;
        HideFullPreviewLearningNoteOverlay();
    }

    private List<PreviewOverlayItem> CreateOverlayItems(TimeSpan position, IReadOnlyList<PreviewSubtitleLine> lines)
    {
        var items = new List<PreviewOverlayItem>();
        foreach (var line in lines)
        {
            var text = NormalizePreviewSubtitleText(line.Cue.Text);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var isJapanese = IsJapaneseSubtitleTrack(line.Track);
            items.Add(new PreviewOverlayItem(
                isJapanese ? PreviewOverlayKind.JapaneseSubtitle : PreviewOverlayKind.EnglishSubtitle,
                text,
                isJapanese ? _japaneseSubtitleOverlayPosition : _englishSubtitleOverlayPosition,
                HasSubtitleTags(FindCueLearningState(line.Track, line.Cue))));
        }

        if (_showLearningNotes && FindLearningNoteState(position, lines) is { } state)
        {
            if (NormalizeOptionalText(state.AiNote) is { } aiNote)
            {
                items.Add(new PreviewOverlayItem(
                    PreviewOverlayKind.AiNote,
                    "AI: " + aiNote,
                    _aiNoteOverlayPosition,
                    HasHighlight: false));
            }

            if (NormalizeOptionalText(state.Note) is { } note)
            {
                items.Add(new PreviewOverlayItem(
                    PreviewOverlayKind.UserNote,
                    "MEMO: " + note,
                    _userNoteOverlayPosition,
                    HasHighlight: false));
            }
        }

        return items;
    }

    private void RenderOverlayPanels(
        StackPanel abovePanel,
        StackPanel belowPanel,
        IReadOnlyList<PreviewOverlayItem> items,
        bool isFullPreview)
    {
        abovePanel.Children.Clear();
        belowPanel.Children.Clear();

        var aboveItems = items
            .Select(item => new PositionedOverlayItem(item, ParseOverlayPosition(item.Position)))
            .Where(item => item.Placement.Side == OverlaySide.Above)
            .OrderBy(item => item.Placement.Order)
            .ThenBy(item => item.Item.SortPriority);
        foreach (var item in aboveItems)
        {
            abovePanel.Children.Add(CreateOverlayCard(item.Item, isFullPreview));
        }

        var belowItems = items
            .Select(item => new PositionedOverlayItem(item, ParseOverlayPosition(item.Position)))
            .Where(item => item.Placement.Side == OverlaySide.Below)
            .OrderByDescending(item => item.Placement.Order)
            .ThenBy(item => item.Item.SortPriority);
        foreach (var item in belowItems)
        {
            belowPanel.Children.Add(CreateOverlayCard(item.Item, isFullPreview));
        }

        abovePanel.Visibility = abovePanel.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        belowPanel.Visibility = belowPanel.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private FrameworkElement CreateOverlayCard(PreviewOverlayItem item, bool isFullPreview)
    {
        var isSubtitle = item.Kind is PreviewOverlayKind.EnglishSubtitle or PreviewOverlayKind.JapaneseSubtitle;
        var isJapanese = item.Kind == PreviewOverlayKind.JapaneseSubtitle;
        var fontSize = item.Kind switch
        {
            PreviewOverlayKind.EnglishSubtitle => isFullPreview ? 26 : 18,
            PreviewOverlayKind.JapaneseSubtitle => isFullPreview ? 20 : 15,
            _ => isFullPreview ? 18 : 13
        };

        var border = new Border
        {
            Background = isSubtitle
                ? new SolidColorBrush(Color.FromArgb(0xB0, 0x00, 0x00, 0x00))
                : new SolidColorBrush(Color.FromArgb(0xC0, 0x0B, 0x11, 0x1A)),
            BorderBrush = item.HasHighlight
                ? CreateBrush(_subtitleTagHighlightColor)
                : isSubtitle ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(0x5D, 0xE0, 0xD0)),
            BorderThickness = item.HasHighlight || !isSubtitle ? new Thickness(1) : new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Padding = isFullPreview ? new Thickness(18, 10, 18, 10) : new Thickness(12, 7, 12, 7),
            Margin = new Thickness(0, 3, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new TextBlock
            {
                Text = item.Text,
                Foreground = isSubtitle
                    ? isJapanese ? new SolidColorBrush(Color.FromRgb(0xE0, 0xE7, 0xF0)) : Brushes.White
                    : new SolidColorBrush(Color.FromRgb(0xEA, 0xFB, 0xF8)),
                FontSize = fontSize,
                FontWeight = item.Kind == PreviewOverlayKind.EnglishSubtitle ? FontWeights.SemiBold : FontWeights.Normal,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            }
        };

        if (isSubtitle && !isFullPreview)
        {
            border.Cursor = Cursors.Hand;
            border.MouseLeftButtonUp += OnPreviewSubtitleClicked;
        }

        return border;
    }

    private void UpdatePreviewLearningNoteOverlay(TimeSpan position, IReadOnlyList<PreviewSubtitleLine> lines)
    {
        var noteText = CreateLearningNoteOverlayText(position, lines);
        if (noteText is null)
        {
            HidePreviewLearningNoteOverlay();
            return;
        }

        PreviewLearningNoteTextBlock.Text = noteText;
        PreviewLearningNoteOverlay.Visibility = Visibility.Visible;
    }

    private void HidePreviewLearningNoteOverlay()
    {
        PreviewLearningNoteTextBlock.Text = string.Empty;
        PreviewLearningNoteOverlay.Visibility = Visibility.Collapsed;
    }

    private void UpdateFullPreviewLearningNoteOverlay(TimeSpan position, IReadOnlyList<PreviewSubtitleLine> lines)
    {
        var noteText = CreateLearningNoteOverlayText(position, lines);
        if (noteText is null)
        {
            HideFullPreviewLearningNoteOverlay();
            return;
        }

        FullPreviewLearningNoteTextBlock.Text = noteText;
        FullPreviewLearningNoteOverlay.Visibility = Visibility.Visible;
    }

    private void HideFullPreviewLearningNoteOverlay()
    {
        FullPreviewLearningNoteTextBlock.Text = string.Empty;
        FullPreviewLearningNoteOverlay.Visibility = Visibility.Collapsed;
    }

    private string? CreateLearningNoteOverlayText(TimeSpan position, IReadOnlyList<PreviewSubtitleLine> lines)
    {
        if (!_showLearningNotes)
        {
            return null;
        }

        var state = FindLearningNoteState(position, lines);
        if (state is null)
        {
            return null;
        }

        var parts = new List<string>();
        if (NormalizeOptionalText(state.AiNote) is { } aiNote)
        {
            parts.Add("AI: " + aiNote);
        }

        if (NormalizeOptionalText(state.Note) is { } note)
        {
            parts.Add("MEMO: " + note);
        }

        return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
    }

    private SubtitleCueLearningState? FindLearningNoteState(TimeSpan position, IReadOnlyList<PreviewSubtitleLine> lines)
    {
        foreach (var line in lines)
        {
            if (IsEnglishSubtitleTrack(line.Track)
                && FindCueLearningState(line.Track, line.Cue) is { } state
                && HasLearningNote(state))
            {
                return state;
            }
        }

        if (_selectedMovie is not null)
        {
            var learningTrack = _previewSubtitleTrack is not null
                ? FindSubtitleTrackByRole(_selectedMovie, _previewSubtitleTrack, SubtitleTrackRole.LearningTarget)
                : _selectedMovie.SubtitleTracks.FirstOrDefault(IsEnglishSubtitleTrack);
            if (learningTrack is not null
                && FindActiveCue(learningTrack, position) is { } cue
                && FindCueLearningState(learningTrack, cue) is { } state
                && HasLearningNote(state))
            {
                return state;
            }
        }

        foreach (var line in lines)
        {
            if (FindCueLearningState(line.Track, line.Cue) is { } state && HasLearningNote(state))
            {
                return state;
            }
        }

        return null;
    }

    private static bool HasLearningNote(SubtitleCueLearningState state)
    {
        return !string.IsNullOrWhiteSpace(state.AiNote) || !string.IsNullOrWhiteSpace(state.Note);
    }

    private static bool IsJapaneseSubtitleTrack(SubtitleTrack track)
    {
        return track.Role == SubtitleTrackRole.Translation
            || string.Equals(track.Language, "ja", StringComparison.OrdinalIgnoreCase)
            || string.Equals(track.Language, "jp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(track.Language, "jpn", StringComparison.OrdinalIgnoreCase);
    }

    private static OverlayPlacement ParseOverlayPosition(string? position)
    {
        var normalized = NormalizeOverlayPosition(position, DefaultEnglishSubtitleOverlayPosition);
        var side = normalized.StartsWith("above", StringComparison.OrdinalIgnoreCase)
            ? OverlaySide.Above
            : OverlaySide.Below;
        var orderText = normalized[^1].ToString();
        var order = int.TryParse(orderText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedOrder)
            ? Math.Clamp(parsedOrder, 1, 4)
            : 1;
        return new OverlayPlacement(side, order);
    }

    private static string NormalizeOverlayPosition(string? position, string fallback)
    {
        var normalized = NormalizeOptionalText(position)?.ToLowerInvariant();
        if (normalized is "above1" or "above2" or "above3" or "above4"
            or "below1" or "below2" or "below3" or "below4")
        {
            return normalized;
        }

        return fallback;
    }

    private void SetDefaultOverlayPositions()
    {
        _englishSubtitleOverlayPosition = DefaultEnglishSubtitleOverlayPosition;
        _japaneseSubtitleOverlayPosition = DefaultJapaneseSubtitleOverlayPosition;
        _aiNoteOverlayPosition = DefaultAiNoteOverlayPosition;
        _userNoteOverlayPosition = DefaultUserNoteOverlayPosition;
    }

    private void ApplyOverlayPositionComboBoxes()
    {
        EnglishSubtitlePositionComboBox.SelectedValue = _englishSubtitleOverlayPosition;
        JapaneseSubtitlePositionComboBox.SelectedValue = _japaneseSubtitleOverlayPosition;
        AiNotePositionComboBox.SelectedValue = _aiNoteOverlayPosition;
        UserNotePositionComboBox.SelectedValue = _userNoteOverlayPosition;
    }

    private void ReadOverlayPositionComboBoxes()
    {
        _englishSubtitleOverlayPosition = NormalizeOverlayPosition(
            EnglishSubtitlePositionComboBox.SelectedValue as string,
            DefaultEnglishSubtitleOverlayPosition);
        _japaneseSubtitleOverlayPosition = NormalizeOverlayPosition(
            JapaneseSubtitlePositionComboBox.SelectedValue as string,
            DefaultJapaneseSubtitleOverlayPosition);
        _aiNoteOverlayPosition = NormalizeOverlayPosition(
            AiNotePositionComboBox.SelectedValue as string,
            DefaultAiNoteOverlayPosition);
        _userNoteOverlayPosition = NormalizeOverlayPosition(
            UserNotePositionComboBox.SelectedValue as string,
            DefaultUserNoteOverlayPosition);
    }

    private void UpdatePlaybackButtonContent()
    {
        PauseButton.Content = _isPreviewPlaying ? "一時停止" : "再開";
        FullPreviewPauseButton.Content = _isFullPreviewPlaying ? "一時停止" : "再開";
    }

    private static bool IsInteractiveInputFocused(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TextBoxBase
                or System.Windows.Controls.ComboBox
                or ButtonBase
                or System.Windows.Controls.Slider
                or System.Windows.Controls.DataGrid)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private List<PreviewSubtitleLine> CreatePreviewSubtitleLines(TimeSpan position)
    {
        if (_selectedMovie is null || _previewSubtitleTrack is null)
        {
            return [];
        }

        if (!_showDualSubtitles)
        {
            var cue = FindActiveCue(_previewSubtitleTrack, position);
            return cue is null ? [] : [new PreviewSubtitleLine(_previewSubtitleTrack, cue)];
        }

        var topTrack = FindSubtitleTrackByRole(_selectedMovie, _previewSubtitleTrack, SubtitleTrackRole.LearningTarget)
            ?? _previewSubtitleTrack;
        var bottomTrack = FindSubtitleTrackByRole(_selectedMovie, _previewSubtitleTrack, SubtitleTrackRole.Translation);

        if (bottomTrack is not null && string.Equals(bottomTrack.Id, topTrack.Id, StringComparison.Ordinal))
        {
            bottomTrack = null;
        }

        var lines = new List<PreviewSubtitleLine>();
        var topCue = FindActiveCue(topTrack, position);
        if (topCue is not null)
        {
            lines.Add(new PreviewSubtitleLine(topTrack, topCue));
        }

        if (bottomTrack is not null)
        {
            var bottomCue = FindActiveCue(bottomTrack, position);
            if (bottomCue is not null)
            {
                lines.Add(new PreviewSubtitleLine(bottomTrack, bottomCue));
            }
        }

        if (lines.Count == 0)
        {
            var cue = FindActiveCue(_previewSubtitleTrack, position);
            if (cue is not null)
            {
                lines.Add(new PreviewSubtitleLine(_previewSubtitleTrack, cue));
            }
        }

        return lines;
    }

    private static SubtitleCue? FindActiveCue(SubtitleTrack track, TimeSpan position)
    {
        return track.Cues.FirstOrDefault(candidate =>
            candidate.Start <= position
            && position < candidate.End
            && !string.IsNullOrWhiteSpace(candidate.Text));
    }

    private static SubtitleTrack? FindSubtitleTrackByRole(Movie movie, SubtitleTrack anchor, SubtitleTrackRole role)
    {
        if (anchor.Role == role)
        {
            return anchor;
        }

        var groupedTrack = movie.SubtitleTracks.FirstOrDefault(track =>
            track.Role == role
            && HasSameSubtitleGroup(anchor, track));
        if (groupedTrack is not null)
        {
            return groupedTrack;
        }

        return movie.SubtitleTracks.FirstOrDefault(track => track.Role == role);
    }

    private static bool HasSameSubtitleGroup(SubtitleTrack left, SubtitleTrack right)
    {
        return !string.IsNullOrWhiteSpace(left.GroupKey)
            && string.Equals(left.GroupKey, right.GroupKey, StringComparison.OrdinalIgnoreCase);
    }

    private void StartPreview(TimeSpan? startPosition = null)
    {
        if (_selectedMovie?.Video.CachePath is null || !File.Exists(_selectedMovie.Video.CachePath))
        {
            SetStatus("プレビューできる動画ファイルがありません。");
            return;
        }

        if (!EnsurePreviewSource(_selectedMovie, playWhenReady: true, startPosition))
        {
            SetStatus("プレビューを準備中です。");
            return;
        }

        if (startPosition is { } position)
        {
            SeekPreviewTo(position);
        }

        PreviewPlayer.Play();
        _isPreviewPlaying = true;
        _previewTimer.Start();
        UpdatePlaybackButtonContent();
        SetStatus("プレビュー再生中です。");
    }

    private void JumpPreviewTo(TimeSpan position)
    {
        _previewStopAt = null;
        if (_selectedMovie?.Video.CachePath is null || !File.Exists(_selectedMovie.Video.CachePath))
        {
            SetStatus("プレビューできる動画ファイルがありません。");
            return;
        }

        if (!EnsurePreviewSource(_selectedMovie, playWhenReady: true, position))
        {
            SetStatus("プレビューを準備中です。");
            return;
        }

        SeekPreviewTo(position);
        PreviewPlayer.Play();
        _isPreviewPlaying = true;
        _previewTimer.Start();
        UpdatePlaybackButtonContent();
    }

    private void StartFullPreview(TimeSpan? startPosition = null)
    {
        if (_selectedMovie?.Video.CachePath is null || !File.Exists(_selectedMovie.Video.CachePath))
        {
            SetStatus("フルプレビューできる動画ファイルがありません。");
            return;
        }

        if (!EnsureFullPreviewSource(_selectedMovie, playWhenReady: true, startPosition))
        {
            SetStatus("フルプレビューを準備中です。");
            return;
        }

        if (startPosition is { } position)
        {
            SeekFullPreviewTo(position);
        }

        FullPreviewPlayer.Play();
        _isFullPreviewPlaying = true;
        _previewTimer.Start();
        UpdatePlaybackButtonContent();
        SetStatus("フルプレビュー再生中です。");
    }

    private void TogglePreviewPlayback()
    {
        if (_isPreviewPlaying)
        {
            PreviewPlayer.Pause();
            _isPreviewPlaying = false;
            UpdatePreviewSeekFromPlayer();
            UpdatePlaybackButtonContent();
            SetStatus("プレビューを一時停止しました。");
            return;
        }

        StartPreview(PreviewPlayer.Source is null ? null : PreviewPlayer.Position);
    }

    private void ToggleFullPreviewPlayback()
    {
        if (_isFullPreviewPlaying)
        {
            FullPreviewPlayer.Pause();
            _isFullPreviewPlaying = false;
            UpdateFullPreviewSeekFromPlayer();
            UpdatePlaybackButtonContent();
            SetStatus("フルプレビューを一時停止しました。");
            return;
        }

        StartFullPreview(FullPreviewPlayer.Source is null ? null : FullPreviewPlayer.Position);
    }

    private void ResetPreviewIfMovieChanged(Movie? movie)
    {
        var currentPath = PreviewPlayer.Source?.LocalPath;
        var nextPath = movie?.Video.CachePath;
        if (string.Equals(currentPath, nextPath, StringComparison.OrdinalIgnoreCase)
            && (FullPreviewPlayer.Source is null
                || string.Equals(FullPreviewPlayer.Source.LocalPath, nextPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _previewTimer.Stop();
        PreviewPlayer.Stop();
        _playPreviewWhenMediaOpened = false;
        _isPreviewPlaying = false;
        _isPreviewMediaOpened = false;
        PreviewPlayer.Source = null;
        ResetPreviewSeek();
        FullPreviewPlayer.Stop();
        _playFullPreviewWhenMediaOpened = false;
        _isFullPreviewPlaying = false;
        _isFullPreviewMediaOpened = false;
        FullPreviewPlayer.Source = null;
        ResetFullPreviewSeek();
        _previewPopupWindow?.Clear();
        UpdatePlaybackButtonContent();
        if (!string.IsNullOrWhiteSpace(nextPath) && File.Exists(nextPath))
        {
            PreviewPlayer.Source = new Uri(nextPath);
        }
    }

    private bool EnsurePreviewSource(Movie movie, bool playWhenReady, TimeSpan? startPosition)
    {
        if (movie.Video.CachePath is null || !File.Exists(movie.Video.CachePath))
        {
            return false;
        }

        var source = new Uri(movie.Video.CachePath);
        var isSameSource = PreviewPlayer.Source is not null
            && string.Equals(PreviewPlayer.Source.LocalPath, source.LocalPath, StringComparison.OrdinalIgnoreCase);
        if (!isSameSource)
        {
            _previewTimer.Stop();
            PreviewPlayer.Stop();
            _isPreviewPlaying = false;
            _isPreviewMediaOpened = false;
            _playPreviewWhenMediaOpened = playWhenReady;
            ResetPreviewSeek();
            _pendingPreviewSeek = startPosition;
            PreviewPlayer.Source = source;
            return false;
        }

        if (startPosition is not null)
        {
            _pendingPreviewSeek = startPosition;
        }

        if (!_isPreviewMediaOpened || _previewDuration <= TimeSpan.Zero)
        {
            _playPreviewWhenMediaOpened = _playPreviewWhenMediaOpened || playWhenReady;
            return false;
        }

        _playPreviewWhenMediaOpened = false;
        return true;
    }

    private bool EnsureFullPreviewSource(Movie movie, bool playWhenReady, TimeSpan? startPosition)
    {
        if (movie.Video.CachePath is null || !File.Exists(movie.Video.CachePath))
        {
            return false;
        }

        var source = new Uri(movie.Video.CachePath);
        var isSameSource = FullPreviewPlayer.Source is not null
            && string.Equals(FullPreviewPlayer.Source.LocalPath, source.LocalPath, StringComparison.OrdinalIgnoreCase);
        if (!isSameSource)
        {
            FullPreviewPlayer.Stop();
            _isFullPreviewPlaying = false;
            _isFullPreviewMediaOpened = false;
            _playFullPreviewWhenMediaOpened = playWhenReady;
            ResetFullPreviewSeek();
            _pendingFullPreviewSeek = startPosition;
            FullPreviewPlayer.Source = source;
            return false;
        }

        if (startPosition is not null)
        {
            _pendingFullPreviewSeek = startPosition;
        }

        if (!_isFullPreviewMediaOpened || _fullPreviewDuration <= TimeSpan.Zero)
        {
            _playFullPreviewWhenMediaOpened = _playFullPreviewWhenMediaOpened || playWhenReady;
            return false;
        }

        _playFullPreviewWhenMediaOpened = false;
        return true;
    }

    private void ResetPreviewSeek()
    {
        _previewDuration = TimeSpan.Zero;
        _pendingPreviewSeek = null;
        _isPreviewMediaOpened = false;
        _isPreviewSeeking = false;

        _isUpdatingPreviewSlider = true;
        try
        {
            PreviewSeekSlider.Minimum = 0;
            PreviewSeekSlider.Maximum = 1;
            PreviewSeekSlider.Value = 0;
            PreviewSeekSlider.IsEnabled = false;
            PreviewPositionTextBlock.Text = FormatPlaybackPosition(TimeSpan.Zero, TimeSpan.Zero);
            HidePreviewSubtitle();
        }
        finally
        {
            _isUpdatingPreviewSlider = false;
        }
    }

    private void UpdatePreviewSeekFromPlayer()
    {
        if (_isPreviewSeeking || PreviewPlayer.Source is null)
        {
            return;
        }

        var position = PreviewPlayer.Position;
        if (_previewStopAt is { } stopAt && position >= stopAt)
        {
            _previewStopAt = null;
            PreviewPlayer.Pause();
            _isPreviewPlaying = false;
            UpdatePlaybackButtonContent();
            SetStatus("サムネイル位置の5秒再生を停止しました。");
        }

        SetPreviewSeek(position);
    }

    private void BeginPreviewSeek()
    {
        if (PreviewSeekSlider.IsEnabled)
        {
            _isPreviewSeeking = true;
        }
    }

    private void CompletePreviewSeek()
    {
        if (!PreviewSeekSlider.IsEnabled)
        {
            _isPreviewSeeking = false;
            return;
        }

        SeekPreviewToSliderValue();
        _isPreviewSeeking = false;
    }

    private void SetPreviewSeek(TimeSpan position)
    {
        var maxSeconds = Math.Max(0.0, PreviewSeekSlider.Maximum);
        var seconds = Math.Clamp(position.TotalSeconds, 0.0, maxSeconds);
        var displayPosition = TimeSpan.FromSeconds(seconds);

        _isUpdatingPreviewSlider = true;
        try
        {
            PreviewSeekSlider.Value = seconds;
            PreviewPositionTextBlock.Text = FormatPlaybackPosition(displayPosition, _previewDuration);
            UpdatePreviewSubtitle(displayPosition);
            SyncPreviewPopupFromActiveSurface(displayPosition);
        }
        finally
        {
            _isUpdatingPreviewSlider = false;
        }
    }

    private void SeekPreviewToSliderValue()
    {
        SeekPreviewTo(TimeSpan.FromSeconds(Math.Clamp(PreviewSeekSlider.Value, 0.0, PreviewSeekSlider.Maximum)));
    }

    private void SeekPreviewTo(TimeSpan position)
    {
        if (PreviewPlayer.Source is null || _previewDuration <= TimeSpan.Zero)
        {
            return;
        }

        position = ClampPreviewPosition(position);
        PreviewPlayer.Position = position;
        SetPreviewSeek(position);
    }

    private TimeSpan ClampPreviewPosition(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return position > _previewDuration ? _previewDuration : position;
    }

    private void ResetFullPreviewSeek()
    {
        _fullPreviewDuration = TimeSpan.Zero;
        _pendingFullPreviewSeek = null;
        _isFullPreviewMediaOpened = false;
        _isFullPreviewSeeking = false;

        _isUpdatingFullPreviewSlider = true;
        try
        {
            FullPreviewSeekSlider.Minimum = 0;
            FullPreviewSeekSlider.Maximum = 1;
            FullPreviewSeekSlider.Value = 0;
            FullPreviewSeekSlider.IsEnabled = false;
            FullPreviewPositionTextBlock.Text = FormatPlaybackPosition(TimeSpan.Zero, TimeSpan.Zero);
            HideFullPreviewSubtitle();
        }
        finally
        {
            _isUpdatingFullPreviewSlider = false;
        }
    }

    private void UpdateFullPreviewSeekFromPlayer()
    {
        if (_isFullPreviewSeeking || FullPreviewPlayer.Source is null)
        {
            return;
        }

        SetFullPreviewSeek(FullPreviewPlayer.Position);
    }

    private void BeginFullPreviewSeek()
    {
        if (FullPreviewSeekSlider.IsEnabled)
        {
            _isFullPreviewSeeking = true;
        }
    }

    private void CompleteFullPreviewSeek()
    {
        if (!FullPreviewSeekSlider.IsEnabled)
        {
            _isFullPreviewSeeking = false;
            return;
        }

        SeekFullPreviewToSliderValue();
        _isFullPreviewSeeking = false;
    }

    private void SetFullPreviewSeek(TimeSpan position)
    {
        var maxSeconds = Math.Max(0.0, FullPreviewSeekSlider.Maximum);
        var seconds = Math.Clamp(position.TotalSeconds, 0.0, maxSeconds);
        var displayPosition = TimeSpan.FromSeconds(seconds);

        _isUpdatingFullPreviewSlider = true;
        try
        {
            FullPreviewSeekSlider.Value = seconds;
            FullPreviewPositionTextBlock.Text = FormatPlaybackPosition(displayPosition, _fullPreviewDuration);
            UpdateFullPreviewSubtitle(displayPosition);
            SyncPreviewPopupFromActiveSurface(displayPosition);
        }
        finally
        {
            _isUpdatingFullPreviewSlider = false;
        }
    }

    private void SeekFullPreviewToSliderValue()
    {
        SeekFullPreviewTo(TimeSpan.FromSeconds(Math.Clamp(FullPreviewSeekSlider.Value, 0.0, FullPreviewSeekSlider.Maximum)));
    }

    private void SeekFullPreviewTo(TimeSpan position)
    {
        if (FullPreviewPlayer.Source is null || _fullPreviewDuration <= TimeSpan.Zero)
        {
            return;
        }

        position = ClampFullPreviewPosition(position);
        FullPreviewPlayer.Position = position;
        SetFullPreviewSeek(position);
    }

    private TimeSpan ClampFullPreviewPosition(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return position > _fullPreviewDuration ? _fullPreviewDuration : position;
    }

    private void SyncFullPreviewFromEdit(bool transferPlayback)
    {
        if (_selectedMovie is null)
        {
            return;
        }

        var position = GetPreviewTimelinePosition();
        var shouldPlay = transferPlayback && _isPreviewPlaying;
        if (_isPreviewPlaying)
        {
            PreviewPlayer.Pause();
            _isPreviewPlaying = false;
        }

        if (shouldPlay)
        {
            StartFullPreview(position);
        }
        else if (EnsureFullPreviewSource(_selectedMovie, playWhenReady: false, position))
        {
            SeekFullPreviewTo(position);
        }

        UpdateFullPreviewSubtitle(position);
        UpdatePlaybackButtonContent();
    }

    private void SyncEditPreviewFromFull(bool transferPlayback)
    {
        if (_selectedMovie is null)
        {
            return;
        }

        var position = GetFullPreviewTimelinePosition();
        var shouldPlay = transferPlayback && _isFullPreviewPlaying;
        if (_isFullPreviewPlaying)
        {
            FullPreviewPlayer.Pause();
            _isFullPreviewPlaying = false;
        }

        if (shouldPlay)
        {
            StartPreview(position);
        }
        else if (EnsurePreviewSource(_selectedMovie, playWhenReady: false, position))
        {
            SeekPreviewTo(position);
        }

        UpdatePreviewSubtitle(position);
        UpdatePlaybackButtonContent();
    }

    private TimeSpan GetPreviewTimelinePosition()
    {
        if (PreviewPlayer.Source is not null)
        {
            return ClampTimelinePosition(PreviewPlayer.Position, _previewDuration);
        }

        return TimeSpan.FromSeconds(Math.Clamp(PreviewSeekSlider.Value, 0.0, PreviewSeekSlider.Maximum));
    }

    private TimeSpan GetFullPreviewTimelinePosition()
    {
        if (FullPreviewPlayer.Source is not null)
        {
            return ClampTimelinePosition(FullPreviewPlayer.Position, _fullPreviewDuration);
        }

        return TimeSpan.FromSeconds(Math.Clamp(FullPreviewSeekSlider.Value, 0.0, FullPreviewSeekSlider.Maximum));
    }

    private static TimeSpan ClampTimelinePosition(TimeSpan position, TimeSpan duration)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return duration > TimeSpan.Zero && position > duration ? duration : position;
    }

    private void SyncPreviewPopupFromActiveSurface(TimeSpan? positionOverride = null, bool forceSeek = false)
    {
        if (_previewPopupWindow is null)
        {
            return;
        }

        var videoPath = _selectedMovie?.Video.CachePath;
        if (string.IsNullOrWhiteSpace(videoPath))
        {
            _previewPopupWindow.Clear();
            _previewPopupVideoPath = null;
            _previewPopupVideoAvailable = false;
            return;
        }

        if (!string.Equals(_previewPopupVideoPath, videoPath, StringComparison.OrdinalIgnoreCase))
        {
            _previewPopupVideoPath = videoPath;
            _previewPopupVideoAvailable = File.Exists(videoPath);
        }

        if (!_previewPopupVideoAvailable)
        {
            _previewPopupWindow.Clear();
            return;
        }

        var useFullPreview = FullPreviewTabItem.IsSelected;
        var position = positionOverride ?? (useFullPreview ? GetFullPreviewTimelinePosition() : GetPreviewTimelinePosition());
        var shouldPlay = useFullPreview ? _isFullPreviewPlaying : _isPreviewPlaying;
        _previewPopupWindow.Sync(videoPath, position, shouldPlay, forceSeek);
        RenderOverlayPanels(
            _previewPopupWindow.AboveOverlayPanel,
            _previewPopupWindow.BelowOverlayPanel,
            CreateOverlayItems(position, CreatePreviewSubtitleLines(position)),
            isFullPreview: true);
    }
}

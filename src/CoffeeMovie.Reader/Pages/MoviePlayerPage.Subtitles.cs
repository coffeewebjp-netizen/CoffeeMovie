using System.Text.Json;
using CoffeeMovie.Core.Models;
using Microsoft.Maui.Storage;

namespace CoffeeMovie.Reader.Pages;

public sealed partial class MoviePlayerPage
{
    private async Task ApplySubtitleSwitchesAsync(bool savePreferences = true)
    {
        if (savePreferences)
        {
            Preferences.Default.Set(ShowEnglishSubtitlesPreferenceKey, _englishSubtitleSwitch.IsToggled);
            Preferences.Default.Set(ShowJapaneseSubtitlesPreferenceKey, _japaneseSubtitleSwitch.IsToggled);
            Preferences.Default.Set(ShowMemoPreferenceKey, _memoSwitch.IsToggled);
        }

        try
        {
            var showEnglish = _englishSubtitleSwitch.IsToggled ? "true" : "false";
            var showJapanese = _japaneseSubtitleSwitch.IsToggled ? "true" : "false";
            var showMemo = _memoSwitch.IsToggled ? "true" : "false";
            await _webView.EvaluateJavaScriptAsync($"window.coffeeMovieSetSubtitleVisibility && window.coffeeMovieSetSubtitleVisibility({showEnglish}, {showJapanese}, {showMemo});");
        }
        catch
        {
            // The WebView may not have loaded the player script yet.
        }

        SetLearningControlsEnabled(_activeLearningState is not null);
    }

    private void SelectActiveCue(string cueId)
    {
        if (_movie is null)
        {
            ClearLearningTarget("動画が見つかりません。");
            return;
        }

        _activeEnglishTrack ??= FindEnglishTrack(_movie)
            ?? _movie.SubtitleTracks.LastOrDefault(track => track.Cues.Count > 0);
        if (_activeEnglishTrack is null)
        {
            ClearLearningTarget("英語字幕がありません。");
            return;
        }

        if (string.IsNullOrWhiteSpace(cueId))
        {
            ClearLearningTarget("英語字幕の外です。");
            return;
        }

        var cue = _activeEnglishTrack.Cues.FirstOrDefault(item =>
            string.Equals(item.Id, cueId, StringComparison.Ordinal));
        if (cue is null)
        {
            ClearLearningTarget("字幕キューが見つかりません。");
            return;
        }

        _activeEnglishCue = cue;
        _activeLearningState = GetOrCreateLearningState(_activeEnglishTrack, cue);
        BindLearningState();
    }

    private void BindLearningState()
    {
        if (_activeEnglishCue is null || _activeLearningState is null)
        {
            return;
        }

        _updatingLearningFields = true;
        _currentCueLabel.Text = CollapseWhitespace(_activeEnglishCue.Text);
        _currentCueMetaLabel.Text = $"{_activeEnglishCue.Index}  {FormatTimestamp(_activeEnglishCue.Start)}";
        _tagsEntry.Text = string.Join(", ", _activeLearningState.Tags);
        _noteEditor.Text = _activeLearningState.Note ?? string.Empty;
        _aiNoteLabel.Text = _activeLearningState.AiNote ?? string.Empty;
        _aiNoteLabel.IsVisible = !string.IsNullOrWhiteSpace(_activeLearningState.AiNote);
        _learningMessageLabel.Text = "現在の英語字幕";
        _learningMessageLabel.TextColor = Color.FromArgb("#A5B3C6");
        UpdateShadowingStatus();
        SetLearningControlsEnabled(true);
        _updatingLearningFields = false;
    }

    private async Task SaveLearningAsync()
    {
        if (_updatingLearningFields || _movie is null || _activeLearningState is null)
        {
            return;
        }

        _activeLearningState.Tags = ParseTags(_tagsEntry.Text);
        _activeLearningState.Note = string.IsNullOrWhiteSpace(_noteEditor.Text)
            ? null
            : _noteEditor.Text.Trim();
        _activeLearningState.UpdatedAt = DateTimeOffset.UtcNow;

        await _libraryService.SaveMovieAsync(_movie);
        _learningMessageLabel.Text = "保存しました";
        _learningMessageLabel.TextColor = Color.FromArgb("#5DE0D0");
    }

    private async Task CycleSubtitlePositionAsync()
    {
        var currentIndex = Array.IndexOf(SubtitlePositions, NormalizeSubtitlePosition(_subtitlePosition));
        var nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % SubtitlePositions.Length;
        _subtitlePosition = SubtitlePositions[nextIndex];
        Preferences.Default.Set(SubtitlePositionPreferenceKey, _subtitlePosition);
        UpdateSubtitlePositionButtons();
        await ApplySubtitlePositionAsync();
    }

    private async Task CycleSubtitleAlignmentAsync()
    {
        var currentIndex = Array.IndexOf(SubtitleAlignments, NormalizeSubtitleAlignment(_subtitleAlignment));
        var nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % SubtitleAlignments.Length;
        _subtitleAlignment = SubtitleAlignments[nextIndex];
        Preferences.Default.Set(SubtitleAlignmentPreferenceKey, _subtitleAlignment);
        UpdateSubtitleAlignmentButtons();
        await ApplySubtitleAlignmentAsync();
    }

    private async Task ApplySubtitlePositionAsync()
    {
        try
        {
            var position = JsonSerializer.Serialize(NormalizeSubtitlePosition(_subtitlePosition));
            await _webView.EvaluateJavaScriptAsync(
                $"window.coffeeMovieSetSubtitlePosition && window.coffeeMovieSetSubtitlePosition({position});");
        }
        catch
        {
            // The WebView may not have loaded the player script yet.
        }
    }

    private async Task ApplySubtitleAlignmentAsync()
    {
        try
        {
            var alignment = JsonSerializer.Serialize(NormalizeSubtitleAlignment(_subtitleAlignment));
            await _webView.EvaluateJavaScriptAsync(
                $"window.coffeeMovieSetSubtitleAlignment && window.coffeeMovieSetSubtitleAlignment({alignment});");
        }
        catch
        {
            // The WebView may not have loaded the player script yet.
        }
    }

    private void ClearLearningTarget(string message)
    {
        _updatingLearningFields = true;
        _activeEnglishCue = null;
        _activeLearningState = null;
        _currentCueLabel.Text = message;
        _currentCueMetaLabel.Text = string.Empty;
        _tagsEntry.Text = string.Empty;
        _noteEditor.Text = string.Empty;
        _aiNoteLabel.Text = string.Empty;
        _aiNoteLabel.IsVisible = false;
        _learningMessageLabel.Text = "対象字幕なし";
        _learningMessageLabel.TextColor = Color.FromArgb("#A5B3C6");
        _shadowingStatusLabel.Text = "OK 0 / NG 0";
        SetPlayerMessage(null);
        SetLearningControlsEnabled(false);
        _updatingLearningFields = false;
    }

    private void SetLearningControlsEnabled(bool enabled)
    {
        var shadowingEnabled = enabled && _englishSubtitleSwitch.IsToggled;
        _tagsEntry.IsEnabled = enabled;
        _noteEditor.IsEnabled = enabled;
        _saveLearningButton.IsEnabled = enabled;
        UpdateCoffeeLearningRegisterButtons(enabled);
        _shadowingButton.IsEnabled = shadowingEnabled;
        _shadowOkButton.IsEnabled = shadowingEnabled;
        _shadowNgButton.IsEnabled = shadowingEnabled;
        _fullscreenShadowingButton.IsEnabled = shadowingEnabled;

        var opacity = enabled ? 1d : 0.45d;
        var shadowOpacity = shadowingEnabled ? 1d : 0.45d;
        _tagsEntry.Opacity = opacity;
        _noteEditor.Opacity = opacity;
        _saveLearningButton.Opacity = opacity;
        _shadowingButton.Opacity = shadowOpacity;
        _shadowOkButton.Opacity = shadowOpacity;
        _shadowNgButton.Opacity = shadowOpacity;
        _fullscreenShadowingButton.Opacity = shadowOpacity;
        UpdateFullscreenOverlayControls();
    }

    private static SubtitleCueLearningState GetOrCreateLearningState(SubtitleTrack track, SubtitleCue cue)
    {
        var state = FindLearningState(track, cue);
        if (state is not null)
        {
            state.CueId = cue.Id;
            state.CueIndex = cue.Index;
            state.Tags ??= [];
            state.Listening ??= new CuePracticeMetric();
            state.Shadowing ??= new CuePracticeMetric();
            return state;
        }

        state = new SubtitleCueLearningState
        {
            CueId = cue.Id,
            CueIndex = cue.Index
        };
        track.CueLearningStates.Add(state);
        return state;
    }

    private static SubtitleCueLearningState? FindLearningState(SubtitleTrack track, SubtitleCue cue)
    {
        return track.CueLearningStates.FirstOrDefault(item =>
            string.Equals(item.CueId, cue.Id, StringComparison.Ordinal)
            || (item.CueIndex > 0 && item.CueIndex == cue.Index));
    }

    private static string BuildDisplayMemo(SubtitleCueLearningState? state)
    {
        if (state is null)
        {
            return string.Empty;
        }

        var parts = new[] { state.AiNote, state.Note }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => CollapseWhitespace(part!))
            .ToArray();
        return string.Join("\n", parts);
    }

    private static SubtitleTrack? FindEnglishTrack(Movie movie)
    {
        return movie.SubtitleTracks.FirstOrDefault(IsEnglishTrack);
    }

    private static SubtitleTrack? FindJapaneseTrack(Movie movie)
    {
        return movie.SubtitleTracks.FirstOrDefault(track => track.Role == SubtitleTrackRole.Translation && track.Cues.Count > 0)
            ?? movie.SubtitleTracks.FirstOrDefault(IsJapaneseTrack);
    }

    private static bool IsEnglishTrack(SubtitleTrack track)
    {
        var language = track.Language?.Trim().ToLowerInvariant();
        if (language is "en" or "eng" or "en-us" or "en-gb")
        {
            return true;
        }

        var fileName = track.SourceFileName.ToLowerInvariant();
        return fileName.EndsWith(".en.srt", StringComparison.Ordinal)
            || fileName.EndsWith(".en.vtt", StringComparison.Ordinal)
            || fileName.Contains(".en.", StringComparison.Ordinal);
    }

    private static bool IsJapaneseTrack(SubtitleTrack track)
    {
        var language = track.Language?.Trim().ToLowerInvariant();
        if (language is "ja" or "jpn" or "jp")
        {
            return true;
        }

        var fileName = track.SourceFileName.ToLowerInvariant();
        return fileName.EndsWith(".ja.srt", StringComparison.Ordinal)
            || fileName.EndsWith(".ja.vtt", StringComparison.Ordinal)
            || fileName.Contains(".ja.", StringComparison.Ordinal);
    }

    private static List<string> ParseTags(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Split([',', '、', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeSubtitlePosition(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return SubtitlePositions.Contains(normalized) ? normalized! : "bottom";
    }

    private static string NormalizeSubtitleAlignment(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return SubtitleAlignments.Contains(normalized) ? normalized! : "center";
    }

    private void UpdateSubtitlePositionButtons()
    {
        var label = NormalizeSubtitlePosition(_subtitlePosition) switch
        {
            "top" => "上",
            "middle" => "中央",
            _ => "下"
        };
        _headerSubtitlePositionButton.Text = $"字幕位置:{label}";
        _fullscreenSubtitlePositionButton.Text = $"字幕位置:{label}";
    }

    private void UpdateSubtitleAlignmentButtons()
    {
        var label = NormalizeSubtitleAlignment(_subtitleAlignment) switch
        {
            "left" => "左",
            "right" => "右",
            _ => "中央"
        };
        _headerSubtitleAlignmentButton.Text = $"字幕寄せ:{label}";
        _fullscreenSubtitleAlignmentButton.Text = $"字幕寄せ:{label}";
    }
}

using System.Globalization;
using CoffeeMovie.Core.Models;
using CoffeeMovie.Reader.Models;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Storage;

namespace CoffeeMovie.Reader.Pages;

public sealed partial class MoviePlayerPage
{
    private void SetFullscreen(bool fullscreen)
    {
        if (_isFullscreen == fullscreen)
        {
            return;
        }

        _isFullscreen = fullscreen;
        NavigationPage.SetHasNavigationBar(this, !fullscreen);
        if (_headerRow is not null)
        {
            _headerRow.Height = fullscreen ? new GridLength(0) : GridLength.Auto;
        }

        if (_playerRow is not null)
        {
            _playerRow.Height = fullscreen ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        }

        if (_learningRow is not null)
        {
            _learningRow.Height = fullscreen ? new GridLength(0) : GridLength.Auto;
        }

        if (_sceneRow is not null)
        {
            _sceneRow.Height = fullscreen ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        }

        if (_headerView is not null)
        {
            _headerView.IsVisible = !fullscreen;
        }

        if (_learningView is not null)
        {
            _learningView.IsVisible = !fullscreen;
        }

        if (_sceneView is not null)
        {
            _sceneView.IsVisible = !fullscreen;
        }

        if (_playerFrame is not null)
        {
            _playerFrame.Margin = fullscreen ? new Thickness(0) : new Thickness(16, 0, 16, 10);
            _playerFrame.StrokeThickness = fullscreen ? 0 : 1;
            _playerFrame.StrokeShape = fullscreen ? null : new RoundRectangle { CornerRadius = 8 };
        }

        _exitFullscreenButton.IsVisible = fullscreen;
        UpdateFullscreenOverlayControls();
        SetSystemFullscreen(fullscreen);
        UpdatePlayerHeight();
        _ = ApplyFullscreenModeToPlayerAsync(fullscreen);
    }

    private async void OnSceneSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SceneJumpItem item)
        {
            return;
        }

        _scenesView.SelectedItem = null;
        var seconds = item.StartSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        await _webView.EvaluateJavaScriptAsync($"window.coffeeMovieJumpTo({seconds});");
    }

    private void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, BridgeScheme, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        if (string.Equals(uri.Host, "playstate", StringComparison.OrdinalIgnoreCase))
        {
            var stateQuery = ParseQuery(uri.Query);
            stateQuery.TryGetValue("state", out var state);
            MainThread.BeginInvokeOnMainThread(() => UpdatePlayerState(state));
            return;
        }

        if (string.Equals(uri.Host, "position", StringComparison.OrdinalIgnoreCase))
        {
            var positionQuery = ParseQuery(uri.Query);
            MainThread.BeginInvokeOnMainThread(() => _ = SavePlaybackPositionAsync(positionQuery));
            return;
        }

        if (!string.Equals(uri.Host, "cue", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var query = ParseQuery(uri.Query);
        query.TryGetValue("cueId", out var cueId);
        MainThread.BeginInvokeOnMainThread(() => SelectActiveCue(cueId ?? string.Empty));
    }

    private async Task SavePlaybackPositionAsync(IReadOnlyDictionary<string, string> query)
    {
        if (!TryReadQueryDouble(query, "position", out var position))
        {
            return;
        }

        TryReadQueryDouble(query, "duration", out var duration);
        var ended = query.TryGetValue("ended", out var endedValue)
            && string.Equals(endedValue, "1", StringComparison.Ordinal);
        var force = query.TryGetValue("force", out var forceValue)
            && string.Equals(forceValue, "1", StringComparison.Ordinal);

        await SavePlaybackPositionAsync(position, duration, ended, force);
    }

    private async Task SavePlaybackPositionAsync(double position, double duration, bool ended, bool force)
    {
        if (_movie is null || !double.IsFinite(position))
        {
            return;
        }

        var playback = _movie.Playback ??= new PlaybackState();
        var safeDuration = double.IsFinite(duration) && duration > 0
            ? duration
            : playback.DurationSeconds;
        var safePosition = Math.Max(0d, position);
        if (ended || (safeDuration > 0 && safePosition >= Math.Max(0d, safeDuration - 5d)))
        {
            safePosition = 0d;
        }

        var now = DateTimeOffset.UtcNow;
        if (!force
            && _lastPlaybackPositionSavedAt != default
            && now - _lastPlaybackPositionSavedAt < TimeSpan.FromSeconds(5)
            && Math.Abs(safePosition - _lastPlaybackPositionSavedSeconds) < 4d)
        {
            return;
        }

        playback.PositionSeconds = safePosition;
        if (safeDuration > 0)
        {
            playback.DurationSeconds = safeDuration;
        }

        playback.LastWatchedAt = now;
        _lastPlaybackPositionSavedAt = now;
        _lastPlaybackPositionSavedSeconds = safePosition;

        try
        {
            await _libraryService.SaveMovieAsync(_movie);
        }
        catch
        {
            // Playback position is best-effort and should not interrupt viewing.
        }
    }

    private async Task NotifyPlayerPositionAsync()
    {
        try
        {
            await _webView.EvaluateJavaScriptAsync(
                "window.coffeeMovieNotifyPosition && window.coffeeMovieNotifyPosition(true);");
        }
        catch
        {
            // The WebView may already be unloading.
        }
    }

    private static bool TryReadQueryDouble(
        IReadOnlyDictionary<string, string> query,
        string key,
        out double value)
    {
        value = 0d;
        return query.TryGetValue(key, out var raw)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private void UpdatePlayerState(string? state)
    {
        if (string.Equals(state, "paused", StringComparison.OrdinalIgnoreCase))
        {
            _isPlayerPaused = true;
        }
        else if (string.Equals(state, "playing", StringComparison.OrdinalIgnoreCase))
        {
            _isPlayerPaused = false;
            UpdatePlayerThumbnail(_movie, show: false);
        }

        UpdatePlayPauseButton();
    }

    private void UpdatePlayerThumbnail(Movie? movie, bool show)
    {
        var path = movie?.Video.ThumbnailPath;
        var hasThumbnail = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        if (hasThumbnail)
        {
            _playerThumbnailImage.Source = ImageSource.FromFile(path!);
        }

        _playerThumbnailImage.IsVisible = show && hasThumbnail;
        _playerThumbnailLayer.IsVisible = show && hasThumbnail;
    }

    private void SetPlayerMessage(string? message, Color? color = null, bool showSpeakOriginalButton = false)
    {
        _playerMessageLabel.Text = message ?? string.Empty;
        _playerMessageLabel.TextColor = color ?? Colors.White;
        _showSpeakOriginalButton = showSpeakOriginalButton;
        UpdateFullscreenOverlayControls();
    }

    private async Task ApplyFullscreenModeToPlayerAsync(bool fullscreen)
    {
        try
        {
            await _webView.EvaluateJavaScriptAsync(
                $"window.coffeeMovieSetAppFullscreen && window.coffeeMovieSetAppFullscreen({(fullscreen ? "true" : "false")});");
        }
        catch
        {
            // The WebView may not have loaded the player script yet.
        }
    }

    private async Task TogglePlayPauseAsync()
    {
        try
        {
            var state = await _webView.EvaluateJavaScriptAsync(
                "window.coffeeMovieTogglePlayPause && window.coffeeMovieTogglePlayPause();");
            UpdatePlayerState(CleanJavaScriptString(state));
        }
        catch
        {
            // The WebView may not have loaded the player script yet.
        }
    }

    private async Task SeekRelativeAsync(int seconds)
    {
        var safeSeconds = Math.Clamp(seconds, -9999, 9999);
        if (safeSeconds == 0)
        {
            return;
        }

        try
        {
            await _webView.EvaluateJavaScriptAsync(
                $"window.coffeeMovieSeekRelative && window.coffeeMovieSeekRelative({safeSeconds.ToString(CultureInfo.InvariantCulture)});");
        }
        catch
        {
            // The WebView may not have loaded the player script yet.
        }
    }

    private async Task EditCustomRewindSecondsAsync()
    {
        var result = await DisplayPromptAsync(
            "カスタム秒数",
            "戻し／早送りに使う秒数を 1-9999 秒で指定します。",
            "保存",
            "キャンセル",
            "3",
            maxLength: 4,
            keyboard: Keyboard.Numeric,
            initialValue: _customRewindSeconds.ToString(CultureInfo.InvariantCulture));
        if (string.IsNullOrWhiteSpace(result))
        {
            return;
        }

        if (!int.TryParse(result.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            await DisplayAlertAsync("カスタム秒数", "1～9999の数字で入力してください。", "閉じる");
            return;
        }

        _customRewindSeconds = Math.Clamp(seconds, 1, 9999);
        Preferences.Default.Set(CustomRewindSecondsPreferenceKey, _customRewindSeconds);
        UpdateRewindButtonLabels();
    }

    private void UpdatePlayPauseButton()
    {
        _playPauseButton.Text = _isPlayerPaused ? "再開" : "一時停止";
    }

    private void UpdateRewindButtonLabels()
    {
        _rewindCustomButton.Text = $"-{_customRewindSeconds}秒";
        _forwardCustomButton.Text = $"+{_customRewindSeconds}秒";
    }

    private void UpdateFullscreenOverlayControls()
    {
        var hasMessage = !string.IsNullOrWhiteSpace(_playerMessageLabel.Text);
        _playerMessageLabel.IsVisible = _isFullscreen && hasMessage;
        _playPauseButton.IsVisible = _isFullscreen;
        _rewindOneButton.IsVisible = _isFullscreen;
        _rewindFiveButton.IsVisible = _isFullscreen;
        _rewindCustomButton.IsVisible = _isFullscreen;
        _forwardOneButton.IsVisible = _isFullscreen;
        _forwardFiveButton.IsVisible = _isFullscreen;
        _forwardCustomButton.IsVisible = _isFullscreen;
        _rewindSettingsButton.IsVisible = _isFullscreen;
        _fullscreenSubtitlePositionButton.IsVisible = _isFullscreen;
        _fullscreenSubtitleAlignmentButton.IsVisible = _isFullscreen;
        _fullscreenRegisterCoffeeLearningButton.IsVisible = _isFullscreen
            && _activeEnglishCue is not null;
        _fullscreenShadowingButton.IsVisible = _isFullscreen
            && _englishSubtitleSwitch.IsToggled
            && _activeEnglishCue is not null;
        _speakOriginalButton.IsVisible = _isFullscreen
            && _showSpeakOriginalButton
            && _activeEnglishCue is not null;
    }

    private static void SetSystemFullscreen(bool fullscreen)
    {
#if ANDROID
        var window = Platform.CurrentActivity?.Window;
        if (window?.DecorView is null)
        {
            return;
        }

#pragma warning disable CA1422
        window.DecorView.SystemUiFlags = fullscreen
            ? Android.Views.SystemUiFlags.Fullscreen
                | Android.Views.SystemUiFlags.HideNavigation
                | Android.Views.SystemUiFlags.ImmersiveSticky
                | Android.Views.SystemUiFlags.LayoutFullscreen
                | Android.Views.SystemUiFlags.LayoutHideNavigation
                | Android.Views.SystemUiFlags.LayoutStable
            : Android.Views.SystemUiFlags.Visible;
#pragma warning restore CA1422
#endif
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = query.TrimStart('?');
        if (trimmed.Length == 0)
        {
            return result;
        }

        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace("+", " "));
            var value = parts.Length > 1
                ? Uri.UnescapeDataString(parts[1].Replace("+", " "))
                : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private static string CleanJavaScriptString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().Trim('"', '\'');
    }
}

using System.IO;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
    private static readonly TimeSpan SeekRecoveryDelay = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan SeekFailureTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PlaybackStallTimeout = TimeSpan.FromSeconds(8);

    private int _previewSeekRecoveryCount;
    private int _fullPreviewSeekRecoveryCount;
    private bool _isRecoveringPreviewMedia;
    private bool _isRecoveringFullPreviewMedia;
    private bool _previewResumeAfterSeek;
    private bool _fullPreviewResumeAfterSeek;
    private TimeSpan _lastObservedPreviewPosition;
    private TimeSpan _lastObservedFullPreviewPosition;
    private DateTimeOffset _lastPreviewProgressAt;
    private DateTimeOffset _lastFullPreviewProgressAt;
    private int _previewStallRecoveryCount;
    private int _fullPreviewStallRecoveryCount;
    private bool _isPreviewAudioSuppressed;
    private bool _isFullPreviewAudioSuppressed;
    private int _previewSeekSequence;
    private int _fullPreviewSeekSequence;

    private async void ResumePreviewAfterSeekAsync(int sequence)
    {
        await Task.Delay(200);
        if (sequence != _previewSeekSequence
            || !_isPreviewPlaying
            || PreviewPlayer.Source is null
            || !_isPreviewMediaOpened)
        {
            return;
        }

        PreviewPlayer.Play();
    }

    private async void ResumeFullPreviewAfterSeekAsync(int sequence)
    {
        await Task.Delay(200);
        if (sequence != _fullPreviewSeekSequence
            || !_isFullPreviewPlaying
            || FullPreviewPlayer.Source is null
            || !_isFullPreviewMediaOpened)
        {
            return;
        }

        FullPreviewPlayer.Play();
    }

    private void CancelPendingPreviewSeekPlayback()
    {
        _previewSeekSequence++;
    }

    private void CancelPendingFullPreviewSeekPlayback()
    {
        _fullPreviewSeekSequence++;
    }

    private void ResetPreviewPlaybackHealth(TimeSpan position)
    {
        _lastObservedPreviewPosition = position;
        _lastPreviewProgressAt = DateTimeOffset.UtcNow;
    }

    private void ResetFullPreviewPlaybackHealth(TimeSpan position)
    {
        _lastObservedFullPreviewPosition = position;
        _lastFullPreviewProgressAt = DateTimeOffset.UtcNow;
    }

    private bool RecoverPreviewIfStalled()
    {
        if (!_isPreviewPlaying
            || !_isPreviewMediaOpened
            || PreviewPlayer.Source is null
            || _previewSeekConfirmationTarget is not null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var position = PreviewPlayer.Position;
        if (_lastPreviewProgressAt == default
            || (position - _lastObservedPreviewPosition).Duration() >= TimeSpan.FromMilliseconds(150))
        {
            _lastObservedPreviewPosition = position;
            _lastPreviewProgressAt = now;
            if (position >= TimeSpan.FromSeconds(2))
            {
                _previewStallRecoveryCount = 0;
            }

            return false;
        }

        if (now - _lastPreviewProgressAt < PlaybackStallTimeout || _previewStallRecoveryCount > 0)
        {
            return false;
        }

        _previewStallRecoveryCount++;
        var recoveryPosition = position > TimeSpan.FromSeconds(1)
            ? position
            : GetResumePosition(_selectedMovie);
        ReloadPreviewMedia(recoveryPosition, shouldPlay: true, "再生エンジンを再初期化しています...");
        return true;
    }

    private bool RecoverFullPreviewIfStalled()
    {
        if (!_isFullPreviewPlaying
            || !_isFullPreviewMediaOpened
            || FullPreviewPlayer.Source is null
            || _fullPreviewSeekConfirmationTarget is not null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var position = FullPreviewPlayer.Position;
        if (_lastFullPreviewProgressAt == default
            || (position - _lastObservedFullPreviewPosition).Duration() >= TimeSpan.FromMilliseconds(150))
        {
            _lastObservedFullPreviewPosition = position;
            _lastFullPreviewProgressAt = now;
            if (position >= TimeSpan.FromSeconds(2))
            {
                _fullPreviewStallRecoveryCount = 0;
            }

            return false;
        }

        if (now - _lastFullPreviewProgressAt < PlaybackStallTimeout || _fullPreviewStallRecoveryCount > 0)
        {
            return false;
        }

        _fullPreviewStallRecoveryCount++;
        var recoveryPosition = position > TimeSpan.FromSeconds(1)
            ? position
            : GetResumePosition(_selectedMovie);
        ReloadFullPreviewMedia(recoveryPosition, shouldPlay: true, "フルプレビューを再初期化しています...");
        return true;
    }

    private void ReloadPreviewMedia(TimeSpan position, bool shouldPlay, string status)
    {
        if (PreviewPlayer.Source is not { } source)
        {
            if (_selectedMovie?.Video.CachePath is not { } path || !File.Exists(path))
            {
                return;
            }

            source = new Uri(path);
        }

        position = ClampTimelinePosition(position, _previewDuration);
        CancelPendingPreviewSeekPlayback();
        _isRecoveringPreviewMedia = true;
        _isPreviewAudioSuppressed = shouldPlay && position > TimeSpan.FromSeconds(1);
        UpdatePreviewAudioRouting();
        _previewResumeAfterSeek = shouldPlay;
        _previewSeekConfirmationTarget = position;
        _previewSeekRequestedAt = DateTimeOffset.UtcNow;
        _pendingPreviewSeek = position;
        _playPreviewWhenMediaOpened = shouldPlay;
        _isPreviewPlaying = shouldPlay;
        _isPreviewMediaOpened = false;
        PreviewPlayer.Stop();
        PreviewPlayer.Source = null;
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => PreviewPlayer.Source = source));
        SetPreviewSeek(position);
        ResetPreviewPlaybackHealth(position);
        UpdatePlaybackButtonContent();
        SetStatus(status);
    }

    private void ReloadFullPreviewMedia(TimeSpan position, bool shouldPlay, string status)
    {
        if (FullPreviewPlayer.Source is not { } source)
        {
            if (_selectedMovie?.Video.CachePath is not { } path || !File.Exists(path))
            {
                return;
            }

            source = new Uri(path);
        }

        position = ClampTimelinePosition(position, _fullPreviewDuration);
        CancelPendingFullPreviewSeekPlayback();
        _isRecoveringFullPreviewMedia = true;
        _isFullPreviewAudioSuppressed = shouldPlay && position > TimeSpan.FromSeconds(1);
        UpdatePreviewAudioRouting();
        _fullPreviewResumeAfterSeek = shouldPlay;
        _fullPreviewSeekConfirmationTarget = position;
        _fullPreviewSeekRequestedAt = DateTimeOffset.UtcNow;
        _pendingFullPreviewSeek = position;
        _playFullPreviewWhenMediaOpened = shouldPlay;
        _isFullPreviewPlaying = shouldPlay;
        _isFullPreviewMediaOpened = false;
        FullPreviewPlayer.Stop();
        FullPreviewPlayer.Source = null;
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => FullPreviewPlayer.Source = source));
        SetFullPreviewSeek(position);
        ResetFullPreviewPlaybackHealth(position);
        UpdatePlaybackButtonContent();
        SetStatus(status);
    }

    private void ResumePreviewPlayback()
    {
        var target = GetPreviewResumeTarget();
        if (_isPreviewMediaOpened
            && PreviewPlayer.Source is not null
            && (PreviewPlayer.Position - target).Duration() <= SeekConfirmationTolerance)
        {
            _previewStallRecoveryCount = 0;
            CancelPendingPreviewSeekPlayback();
            _isPreviewAudioSuppressed = false;
            _isPreviewPlaying = true;
            UpdatePreviewAudioRouting();
            PreviewPlayer.Play();
            _previewTimer.Start();
            ResetPreviewPlaybackHealth(PreviewPlayer.Position);
            UpdatePlaybackButtonContent();
            SetStatus("再開しました。");
            return;
        }

        _previewStallRecoveryCount = 0;
        ReloadPreviewMedia(target, shouldPlay: true, "保存位置から再開します...");
    }

    private void ResumeFullPreviewPlayback()
    {
        var target = GetFullPreviewResumeTarget();
        if (_isFullPreviewMediaOpened
            && FullPreviewPlayer.Source is not null
            && (FullPreviewPlayer.Position - target).Duration() <= SeekConfirmationTolerance)
        {
            _fullPreviewStallRecoveryCount = 0;
            CancelPendingFullPreviewSeekPlayback();
            _isFullPreviewAudioSuppressed = false;
            _isFullPreviewPlaying = true;
            UpdatePreviewAudioRouting();
            FullPreviewPlayer.Play();
            _previewTimer.Start();
            ResetFullPreviewPlaybackHealth(FullPreviewPlayer.Position);
            UpdatePlaybackButtonContent();
            SetStatus("再開しました。");
            return;
        }

        _fullPreviewStallRecoveryCount = 0;
        ReloadFullPreviewMedia(target, shouldPlay: true, "保存位置から再開します...");
    }

    private TimeSpan GetPreviewResumeTarget()
    {
        var position = PreviewPlayer.Source is null ? TimeSpan.Zero : PreviewPlayer.Position;
        if (position > TimeSpan.FromSeconds(1))
        {
            return ClampTimelinePosition(position, _previewDuration);
        }

        return GetResumePosition(_selectedMovie);
    }

    private TimeSpan GetFullPreviewResumeTarget()
    {
        var position = FullPreviewPlayer.Source is null ? TimeSpan.Zero : FullPreviewPlayer.Position;
        if (position > TimeSpan.FromSeconds(1))
        {
            return ClampTimelinePosition(position, _fullPreviewDuration);
        }

        return GetResumePosition(_selectedMovie);
    }
}

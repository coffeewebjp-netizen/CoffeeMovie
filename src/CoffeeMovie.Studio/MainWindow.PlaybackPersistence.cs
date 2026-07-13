using CoffeeMovie.Core.Models;

namespace CoffeeMovie.Studio;

public partial class MainWindow
{
    private static readonly TimeSpan PlaybackSaveInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SeekConfirmationTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SeekConfirmationTolerance = TimeSpan.FromSeconds(2);

    private TimeSpan? _previewSeekConfirmationTarget;
    private DateTimeOffset _previewSeekRequestedAt;
    private TimeSpan? _fullPreviewSeekConfirmationTarget;
    private DateTimeOffset _fullPreviewSeekRequestedAt;
    private DateTimeOffset _lastPlaybackSavedAt;
    private string? _lastPlaybackSavedMovieId;
    private double _lastPlaybackSavedPosition = -1d;
    private bool _isSavingPlaybackPosition;

    private TimeSpan GetResumePosition(Movie? movie)
    {
        if (movie?.Playback is not { } playback
            || !double.IsFinite(playback.PositionSeconds)
            || playback.PositionSeconds <= 1d)
        {
            return TimeSpan.Zero;
        }

        var duration = double.IsFinite(playback.DurationSeconds)
            ? Math.Max(0d, playback.DurationSeconds)
            : 0d;
        if (duration > 0d && playback.PositionSeconds >= Math.Max(0d, duration - 5d))
        {
            return TimeSpan.Zero;
        }

        var position = duration > 0d
            ? Math.Min(playback.PositionSeconds, Math.Max(0d, duration - 2d))
            : playback.PositionSeconds;
        return TimeSpan.FromSeconds(Math.Max(0d, position));
    }

    private void CaptureActivePlaybackPosition(bool force = false, bool ended = false)
    {
        if (_selectedMovie is null)
        {
            return;
        }

        var useFullPreview = FullPreviewTabItem.IsSelected && FullPreviewPlayer.Source is not null;
        if (!ended && (useFullPreview ? _fullPreviewSeekConfirmationTarget : _previewSeekConfirmationTarget) is not null)
        {
            return;
        }

        var position = useFullPreview ? FullPreviewPlayer.Position : PreviewPlayer.Position;
        var duration = useFullPreview ? _fullPreviewDuration : _previewDuration;
        _ = SavePlaybackPositionAsync(_selectedMovie, position, duration, force, ended);
    }

    private void CapturePlaybackPositionForSource(string? sourcePath, TimeSpan position, TimeSpan duration, bool force)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        var movie = _currentLibrary.Movies.FirstOrDefault(candidate =>
            string.Equals(candidate.Video.CachePath, sourcePath, StringComparison.OrdinalIgnoreCase));
        if (movie is not null)
        {
            _ = SavePlaybackPositionAsync(movie, position, duration, force, ended: false);
        }
    }

    private async Task SavePlaybackPositionAsync(
        Movie movie,
        TimeSpan position,
        TimeSpan duration,
        bool force,
        bool ended)
    {
        var safeDuration = Math.Max(0d, duration.TotalSeconds);
        var safePosition = Math.Max(0d, position.TotalSeconds);
        if (ended || (safeDuration > 0d && safePosition >= Math.Max(0d, safeDuration - 5d)))
        {
            safePosition = 0d;
        }

        var now = DateTimeOffset.UtcNow;
        movie.Playback ??= new PlaybackState();
        if (!ended
            && safePosition <= 1d
            && movie.Playback.PositionSeconds > 1d
            && (_isPreviewPlaying || _isFullPreviewPlaying || _isRecoveringPreviewMedia || _isRecoveringFullPreviewMedia))
        {
            return;
        }

        movie.Playback.PositionSeconds = safePosition;
        if (safeDuration > 0d)
        {
            movie.Playback.DurationSeconds = safeDuration;
        }

        movie.Playback.LastWatchedAt = now;
        if (_isSavingPlaybackPosition)
        {
            return;
        }

        if (!force && !string.Equals(_lastPlaybackSavedMovieId, movie.Id, StringComparison.Ordinal))
        {
            _lastPlaybackSavedAt = now;
            _lastPlaybackSavedMovieId = movie.Id;
            _lastPlaybackSavedPosition = safePosition;
            return;
        }

        if (!force
            && string.Equals(_lastPlaybackSavedMovieId, movie.Id, StringComparison.Ordinal)
            && now - _lastPlaybackSavedAt < PlaybackSaveInterval
            && Math.Abs(safePosition - _lastPlaybackSavedPosition) < 4d)
        {
            return;
        }

        _isSavingPlaybackPosition = true;
        try
        {
            await _libraryStore.SaveAsync(_currentLibrary);
            _lastPlaybackSavedAt = now;
            _lastPlaybackSavedMovieId = movie.Id;
            _lastPlaybackSavedPosition = safePosition;
        }
        catch
        {
            // Playback progress is best-effort and must not interrupt preview playback.
        }
        finally
        {
            _isSavingPlaybackPosition = false;
        }
    }

    private bool HoldPreviewSeekTargetUntilApplied()
    {
        if (_previewSeekConfirmationTarget is not { } target || PreviewPlayer.Source is null)
        {
            return false;
        }

        if (!_isPreviewMediaOpened)
        {
            SetPreviewSeek(target);
            return true;
        }

        var difference = (PreviewPlayer.Position - target).Duration();
        if (difference <= SeekConfirmationTolerance)
        {
            _previewSeekConfirmationTarget = null;
            _isRecoveringPreviewMedia = false;
            _isPreviewAudioSuppressed = false;
            UpdatePreviewAudioRouting();
            ResetPreviewPlaybackHealth(PreviewPlayer.Position);
            return false;
        }

        var elapsed = DateTimeOffset.UtcNow - _previewSeekRequestedAt;
        if (elapsed < SeekRecoveryDelay)
        {
            SetPreviewSeek(target);
            return true;
        }

        if (_previewSeekRecoveryCount == 0)
        {
            _previewSeekRecoveryCount++;
            ReloadPreviewMedia(target, _previewResumeAfterSeek, "シークを再初期化しています...");
            return true;
        }

        if (elapsed < SeekFailureTimeout)
        {
            SetPreviewSeek(target);
            return true;
        }

        _previewSeekConfirmationTarget = null;
        _isRecoveringPreviewMedia = false;
        _isPreviewAudioSuppressed = false;
        UpdatePreviewAudioRouting();
        _isPreviewPlaying = false;
        PreviewPlayer.Pause();
        _previewTimer.Stop();
        UpdatePlaybackButtonContent();
        SetStatus("シークに失敗しました。再生エンジンを停止しました。");
        return false;
    }

    private bool HoldFullPreviewSeekTargetUntilApplied()
    {
        if (_fullPreviewSeekConfirmationTarget is not { } target || FullPreviewPlayer.Source is null)
        {
            return false;
        }

        if (!_isFullPreviewMediaOpened)
        {
            SetFullPreviewSeek(target);
            return true;
        }

        var difference = (FullPreviewPlayer.Position - target).Duration();
        if (difference <= SeekConfirmationTolerance)
        {
            _fullPreviewSeekConfirmationTarget = null;
            _isRecoveringFullPreviewMedia = false;
            _isFullPreviewAudioSuppressed = false;
            UpdatePreviewAudioRouting();
            ResetFullPreviewPlaybackHealth(FullPreviewPlayer.Position);
            return false;
        }

        var elapsed = DateTimeOffset.UtcNow - _fullPreviewSeekRequestedAt;
        if (elapsed < SeekRecoveryDelay)
        {
            SetFullPreviewSeek(target);
            return true;
        }

        if (_fullPreviewSeekRecoveryCount == 0)
        {
            _fullPreviewSeekRecoveryCount++;
            ReloadFullPreviewMedia(target, _fullPreviewResumeAfterSeek, "フルプレビューのシークを再初期化しています...");
            return true;
        }

        if (elapsed < SeekFailureTimeout)
        {
            SetFullPreviewSeek(target);
            return true;
        }

        _fullPreviewSeekConfirmationTarget = null;
        _isRecoveringFullPreviewMedia = false;
        _isFullPreviewAudioSuppressed = false;
        UpdatePreviewAudioRouting();
        _isFullPreviewPlaying = false;
        FullPreviewPlayer.Pause();
        UpdatePlaybackButtonContent();
        SetStatus("フルプレビューのシークに失敗しました。");
        return false;
    }

    private void UpdatePreviewAudioRouting()
    {
        var popupOwnsAudio = _previewPopupWindow is not null;
        PreviewPlayer.IsMuted = popupOwnsAudio || _isPreviewAudioSuppressed;
        FullPreviewPlayer.IsMuted = popupOwnsAudio || _isFullPreviewAudioSuppressed;
        PreviewPlayer.Volume = 1d;
        FullPreviewPlayer.Volume = 1d;

        var activeSurfaceSuppressed = FullPreviewTabItem.IsSelected
            ? _isFullPreviewAudioSuppressed
            : _isPreviewAudioSuppressed;
        _previewPopupWindow?.SetMuted(activeSurfaceSuppressed);
    }
}

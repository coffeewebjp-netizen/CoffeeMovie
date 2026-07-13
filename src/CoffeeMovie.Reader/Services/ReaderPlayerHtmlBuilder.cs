using System.Globalization;
using System.Net;
using System.Text.Json;
using CoffeeMovie.Core.Models;

namespace CoffeeMovie.Reader.Services;

public static class ReaderPlayerHtmlBuilder
{
    public static string Build(
        Movie movie,
        bool showEnglishSubtitles,
        bool showJapaneseSubtitles,
        bool showMemo,
        string subtitlePosition,
        string subtitleAlignment)
    {
        var videoUri = ToFileUri(movie.Video.CachePath);
        var safeSubtitlePosition = NormalizeSubtitlePosition(subtitlePosition);
        var safeSubtitleAlignment = NormalizeSubtitleAlignment(subtitleAlignment);
        var resumePositionSeconds = GetSafeResumePositionSeconds(movie.Playback);
        var cueTrack = FindEnglishTrack(movie)
            ?? movie.SubtitleTracks.LastOrDefault(subtitle => subtitle.Cues.Count > 0);
        var japaneseTrack = FindJapaneseTrack(movie);
        var bridgeCuesJson = JsonSerializer.Serialize((cueTrack?.Cues ?? [])
            .Select(cue =>
            {
                var learningState = cueTrack is null ? null : FindLearningState(cueTrack, cue);
                return new
                {
                    cueId = cue.Id,
                    index = cue.Index,
                    start = cue.Start.TotalSeconds,
                    end = cue.End.TotalSeconds,
                    text = cue.Text,
                    memo = BuildDisplayMemo(learningState),
                    registered = IsCoffeeLearningRegistered(learningState)
                };
            }));
        var japaneseCuesJson = JsonSerializer.Serialize((japaneseTrack?.Cues ?? [])
            .Select(cue => new
            {
                index = cue.Index,
                start = cue.Start.TotalSeconds,
                end = cue.End.TotalSeconds,
                text = cue.Text
            }));

        return $$"""
<!doctype html>
<html>
<head>
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>
html, body {
  width: 100%;
  height: 100%;
  margin: 0;
  background: #000;
  overflow: hidden;
}
.stage {
  position: relative;
  width: 100%;
  height: 100%;
  background: #000;
}
video {
  width: 100%;
  height: 100%;
  background: #000;
}
.subtitleOverlay {
  position: absolute;
  left: 50%;
  width: min(94vw, 1080px);
  transform: translateX(-50%);
  display: flex;
  flex-direction: column;
  gap: 5px;
  align-items: center;
  pointer-events: none;
  z-index: 10;
}
.subtitleOverlay.position-bottom {
  top: auto;
  bottom: calc(48px + env(safe-area-inset-bottom));
  transform: translateX(-50%);
}
.subtitleOverlay.position-middle {
  top: 50%;
  bottom: auto;
  transform: translate(-50%, -50%);
}
.subtitleOverlay.position-top {
  top: calc(48px + env(safe-area-inset-top));
  bottom: auto;
  transform: translateX(-50%);
}
.subtitleOverlay.align-left {
  align-items: flex-start;
}
.subtitleOverlay.align-center {
  align-items: center;
}
.subtitleOverlay.align-right {
  align-items: flex-end;
}
.subtitleLine {
  display: none;
  width: fit-content;
  max-width: 100%;
  box-sizing: border-box;
  padding: 3px 9px;
  border-radius: 6px;
  background: rgba(0, 0, 0, 0.58);
  color: #fff;
  font-family: system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
  font-weight: 700;
  line-height: 1.32;
  text-align: center;
  text-shadow: 0 1px 2px #000, 0 0 3px #000;
  white-space: pre-wrap;
  overflow-wrap: normal;
  word-break: normal;
  text-wrap: wrap;
}
.subtitleLine.active {
  display: block;
}
#subtitleEn {
  font-size: clamp(15px, 3.8vw, 26px);
  overflow-wrap: break-word;
}
#subtitleEn.shadowing {
  outline: 2px solid #5DE0D0;
  background: rgba(9, 45, 42, 0.82);
  box-shadow: 0 0 18px rgba(93, 224, 208, 0.45);
}
#subtitleEn.registered {
  outline: 2px solid #8CE7B2;
  background: rgba(8, 43, 27, 0.86);
}
#subtitleJa {
  font-size: clamp(14px, 3.4vw, 24px);
  color: #F6D365;
  line-break: strict;
  overflow-wrap: normal;
  word-break: normal;
}
#subtitleMemo {
  font-size: clamp(12px, 3vw, 20px);
  color: #BDEFCF;
  font-weight: 650;
  background: rgba(3, 22, 17, 0.72);
}
@media (orientation: landscape) {
  .subtitleOverlay {
    width: min(92vw, 1120px);
    gap: 4px;
  }
  .subtitleOverlay.position-bottom {
    bottom: calc(38px + env(safe-area-inset-bottom));
  }
  .subtitleOverlay.position-top {
    top: calc(38px + env(safe-area-inset-top));
  }
  #subtitleEn {
    font-size: clamp(13px, 2.35vw, 22px);
  }
  #subtitleJa {
    font-size: clamp(12px, 1.9vw, 19px);
  }
  #subtitleMemo {
    font-size: clamp(11px, 1.85vw, 18px);
  }
}
video::-webkit-media-controls-fullscreen-button {
  display: none;
}
</style>
</head>
<body>
<div class="stage">
  <video id="player" controls playsinline webkit-playsinline preload="metadata" controlsList="nofullscreen" disablepictureinpicture>
    <source src="{{Html(videoUri)}}" type="{{Html(movie.Video.ContentType ?? "video/mp4")}}">
  </video>
  <div class="subtitleOverlay position-{{Html(safeSubtitlePosition)}} align-{{Html(safeSubtitleAlignment)}}" aria-live="off">
    <div id="subtitleMemo" class="subtitleLine"></div>
    <div id="subtitleEn" class="subtitleLine"></div>
    <div id="subtitleJa" class="subtitleLine"></div>
  </div>
</div>
<script>
const coffeeMovieCues = {{bridgeCuesJson}};
const coffeeMovieJapaneseCues = {{japaneseCuesJson}};
const coffeeMovieResumePosition = {{resumePositionSeconds.ToString("0.###", CultureInfo.InvariantCulture)}};
const player = document.getElementById('player');
const subtitleOverlay = document.querySelector('.subtitleOverlay');
const subtitleMemo = document.getElementById('subtitleMemo');
const subtitleEn = document.getElementById('subtitleEn');
const subtitleJa = document.getElementById('subtitleJa');
let showEnglishSubtitles = {{showEnglishSubtitles.ToString().ToLowerInvariant()}};
let showJapaneseSubtitles = {{showJapaneseSubtitles.ToString().ToLowerInvariant()}};
let showMemo = {{showMemo.ToString().ToLowerInvariant()}};
let lastCoffeeMovieCueId = null;
let coffeeMovieResumeApplied = false;
let lastCoffeeMoviePositionNotifyAt = 0;
let shadowingActive = false;
let coffeeMovieAppFullscreen = false;
let coffeeMovieSubtitlePosition = '{{Html(safeSubtitlePosition)}}';
let coffeeMovieSubtitleAlignment = '{{Html(safeSubtitleAlignment)}}';

function coffeeMovieFindCue(cues) {
  const time = player.currentTime || 0;
  return cues.find(cue => time >= cue.start && time <= cue.end) || null;
}

function coffeeMovieCurrentCue() {
  return coffeeMovieFindCue(coffeeMovieCues);
}

function coffeeMovieSetLine(element, text) {
  if (!text) {
    element.textContent = '';
    element.classList.remove('active');
    return;
  }

  element.textContent = text;
  element.classList.add('active');
}

function coffeeMovieJoinSubtitleLines(text, compact) {
  if (!text) {
    return '';
  }

  const lines = String(text)
    .replace(/\r\n/g, '\n')
    .split('\n')
    .map(line => line.trim())
    .filter(line => line.length > 0);
  return lines.join(compact ? '' : ' ');
}

function coffeeMovieFormatEnglishCue(cue) {
  const text = coffeeMovieJoinSubtitleLines(cue.text, false);
  return cue.registered ? '✓ ' + text : text;
}

function coffeeMovieRenderSubtitles() {
  const enCue = coffeeMovieCurrentCue();
  const jaCue = coffeeMovieFindCue(coffeeMovieJapaneseCues);
  coffeeMovieSetLine(subtitleMemo, showMemo && enCue ? enCue.memo : '');
  coffeeMovieSetLine(subtitleEn, showEnglishSubtitles && enCue ? coffeeMovieFormatEnglishCue(enCue) : '');
  coffeeMovieSetLine(subtitleJa, showJapaneseSubtitles && jaCue ? coffeeMovieJoinSubtitleLines(jaCue.text, true) : '');
  subtitleEn.classList.toggle('shadowing', shadowingActive && showEnglishSubtitles && !!enCue);
  subtitleEn.classList.toggle('registered', showEnglishSubtitles && !!enCue && !!enCue.registered);
}

window.coffeeMovieSetSubtitleVisibility = function(showEnglish, showJapanese, showMemoLine) {
  showEnglishSubtitles = !!showEnglish;
  showJapaneseSubtitles = !!showJapanese;
  showMemo = !!showMemoLine;
  coffeeMovieRenderSubtitles();
};

window.coffeeMovieSetShadowingActive = function(active) {
  shadowingActive = !!active;
  coffeeMovieRenderSubtitles();
};

window.coffeeMovieMarkCueRegistered = function(cueId) {
  const cue = coffeeMovieCues.find(item => item.cueId === cueId);
  if (!cue) {
    return;
  }

  cue.registered = true;
  coffeeMovieRenderSubtitles();
};

window.coffeeMovieSetAppFullscreen = function(fullscreen) {
  coffeeMovieAppFullscreen = !!fullscreen;
};

window.coffeeMovieSetSubtitlePosition = function(position) {
  const normalized = ['top', 'middle', 'bottom'].includes(position) ? position : 'bottom';
  coffeeMovieSubtitlePosition = normalized;
  subtitleOverlay.classList.remove('position-top', 'position-middle', 'position-bottom');
  subtitleOverlay.classList.add('position-' + normalized);
};

window.coffeeMovieSetSubtitleAlignment = function(alignment) {
  const normalized = ['left', 'center', 'right'].includes(alignment) ? alignment : 'center';
  coffeeMovieSubtitleAlignment = normalized;
  subtitleOverlay.classList.remove('align-left', 'align-center', 'align-right');
  subtitleOverlay.classList.add('align-' + normalized);
};

function coffeeMovieNotifyCue(force) {
  const cue = coffeeMovieCurrentCue();
  const cueId = cue ? cue.cueId : '';
  coffeeMovieRenderSubtitles();
  if (!force && cueId === lastCoffeeMovieCueId) {
    return;
  }
  lastCoffeeMovieCueId = cueId;
  location.href = 'coffeemovie://cue?cueId=' + encodeURIComponent(cueId);
}

function coffeeMovieNotifyPlayState() {
  location.href = 'coffeemovie://playstate?state=' + (player.paused ? 'paused' : 'playing');
}

function coffeeMovieApplyResumePosition() {
  if (coffeeMovieResumeApplied) {
    return;
  }

  coffeeMovieResumeApplied = true;
  const resume = Number(coffeeMovieResumePosition) || 0;
  const duration = Number.isFinite(player.duration) ? player.duration : 0;
  if (resume <= 1 || (duration > 0 && resume >= duration - 5)) {
    return;
  }

  try {
    player.currentTime = duration > 0 ? Math.min(resume, Math.max(0, duration - 2)) : resume;
  } catch {
  }
}

window.coffeeMovieNotifyPosition = function(force) {
  const now = Date.now();
  if (!force && now - lastCoffeeMoviePositionNotifyAt < 5000) {
    return;
  }

  const position = Number.isFinite(player.currentTime) ? player.currentTime : 0;
  const duration = Number.isFinite(player.duration) ? player.duration : 0;
  lastCoffeeMoviePositionNotifyAt = now;
  location.href = 'coffeemovie://position?position=' + encodeURIComponent(position.toFixed(3))
    + '&duration=' + encodeURIComponent(duration.toFixed(3))
    + '&ended=' + (player.ended ? '1' : '0')
    + '&force=' + (force ? '1' : '0');
};

window.coffeeMovieJumpTo = function(seconds) {
  player.currentTime = seconds;
  player.play();
  setTimeout(() => {
    coffeeMovieNotifyCue(true);
    window.coffeeMovieNotifyPosition(true);
  }, 80);
};

window.coffeeMovieTogglePlayPause = function() {
  if (player.paused) {
    player.play();
    coffeeMovieNotifyPlayState();
    return 'playing';
  }

  player.pause();
  coffeeMovieNotifyPlayState();
  return 'paused';
};

window.coffeeMovieSeekRelative = function(seconds) {
  const amount = Number(seconds) || 0;
  const target = Math.max(0, (player.currentTime || 0) + amount);
  player.currentTime = Number.isFinite(player.duration)
    ? Math.min(target, player.duration)
    : target;
  setTimeout(() => {
    coffeeMovieNotifyCue(true);
    window.coffeeMovieNotifyPosition(true);
  }, 50);
  return player.currentTime;
};

window.coffeeMovieRewind = function(seconds) {
  return window.coffeeMovieSeekRelative(-Math.max(0, Number(seconds) || 0));
};

player.addEventListener('loadedmetadata', () => {
  coffeeMovieApplyResumePosition();
  setTimeout(() => {
    coffeeMovieNotifyCue(true);
    coffeeMovieNotifyPlayState();
  }, 40);
});
player.addEventListener('timeupdate', () => {
  coffeeMovieNotifyCue(false);
  window.coffeeMovieNotifyPosition(false);
});
player.addEventListener('seeked', () => {
  coffeeMovieNotifyCue(true);
  window.coffeeMovieNotifyPosition(true);
});
player.addEventListener('pause', () => {
  coffeeMovieNotifyCue(true);
  window.coffeeMovieNotifyPosition(true);
  setTimeout(coffeeMovieNotifyPlayState, 20);
});
player.addEventListener('play', () => {
  coffeeMovieNotifyCue(true);
  window.coffeeMovieNotifyPosition(true);
  setTimeout(coffeeMovieNotifyPlayState, 20);
});
player.addEventListener('ended', () => {
  coffeeMovieNotifyCue(true);
  window.coffeeMovieNotifyPosition(true);
});
player.addEventListener('click', event => {
  if (!coffeeMovieAppFullscreen || player.paused) {
    return;
  }

  event.preventDefault();
  event.stopPropagation();
  player.pause();
}, true);
</script>
</body>
</html>
""";
    }

    private static string ToFileUri(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return new Uri(path).AbsoluteUri;
    }

    private static string NormalizeSubtitlePosition(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "top" or "middle" or "bottom" ? normalized : "bottom";
    }

    private static string NormalizeSubtitleAlignment(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "left" or "center" or "right" ? normalized : "center";
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

    private static SubtitleCueLearningState? FindLearningState(SubtitleTrack track, SubtitleCue cue)
    {
        return track.CueLearningStates.FirstOrDefault(item =>
            string.Equals(item.CueId, cue.Id, StringComparison.Ordinal)
            || (item.CueIndex > 0 && item.CueIndex == cue.Index));
    }

    private static double GetSafeResumePositionSeconds(PlaybackState? playback)
    {
        if (playback is null || !double.IsFinite(playback.PositionSeconds))
        {
            return 0d;
        }

        var position = Math.Max(0d, playback.PositionSeconds);
        var duration = double.IsFinite(playback.DurationSeconds) ? playback.DurationSeconds : 0d;
        if (position <= 1d || (duration > 0 && position >= Math.Max(0d, duration - 5d)))
        {
            return 0d;
        }

        return duration > 0 ? Math.Min(position, Math.Max(0d, duration - 2d)) : position;
    }

    private static bool IsCoffeeLearningRegistered(SubtitleCueLearningState? state)
    {
        return state?.CoffeeLearningRegisteredAt is not null
            || !string.IsNullOrWhiteSpace(state?.CoffeeLearningWordId);
    }

    private static string BuildDisplayMemo(SubtitleCueLearningState? state)
    {
        if (state is null)
        {
            return string.Empty;
        }

        var parts = new[]
            {
                state.AiNote,
                state.Note,
                IsCoffeeLearningRegistered(state) ? "✓ CoffeeLearning登録済" : null
            }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => CollapseWhitespace(part!))
            .ToArray();
        return string.Join("\n", parts);
    }

    private static string CollapseWhitespace(string value)
    {
        return string.Join(' ', value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Html(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}

# Architecture

## Projects

```text
src/
  CoffeeMovie.Core/          Shared models
  CoffeeMovie.Storage/       JSON stores, subtitle parsing, cache index
  CoffeeMovie.Studio/        Windows WPF app for preparing movies and sidecars
  CoffeeMovie.Reader/        .NET MAUI Android app
  CoffeeMovie.Verification/  No-package verification console
```

## Reader Flow

1. User imports a video with the Android file picker.
2. The reader copies the video into app data and adds a movie record.
3. User opens the movie.
4. User imports an SRT or WebVTT subtitle.
5. The subtitle is parsed, converted to WebVTT if needed, and saved.
6. The player loads local video and local WebVTT through a WebView-backed HTML5 video surface.
7. Subtitle cues are shown as jump targets.

## Studio Flow

Studio prepares videos and subtitles before they are watched on mobile:

1. Import videos and subtitles through buttons or drag/drop.
2. Infer subtitle metadata from file names such as `.en.srt` and `.ja.srt`.
3. Keep English learning-target and Japanese translation tracks separate while linking them with `groupKey`.
4. Preview the video with a seek bar and subtitle overlay.
5. Show English above Japanese when paired subtitle display is enabled.
6. Edit cue start/end timing, nudge selected cues, and optionally sync timing to the paired track.
7. Store cue tags, notes, flag state, listening metrics, and shadowing metrics on the subtitle track.
8. Export sidecar metadata for future Drive-first sync.

The full-size preview tab reuses the same subtitle-line selection logic as the edit preview, but has its own `MediaElement` and seek state. Both previews are driven by the shared preview timer so subtitle overlays continue to follow playback after media-open and seek operations.

## Drive Flow

Drive sync is planned as a Reader service because Android owns the cache and authentication state. Storage remains platform-neutral and only exposes cache index and JSON helpers.

Studio can export `.coffeemovie.json` sidecars beside a video or into a Drive sync folder. Reader-side Drive sync should prefer those sidecars before downloading large video files.

## Why WebVTT

Android video playback surfaces can attach WebVTT tracks directly through HTML5 video. SRT is common but not native to HTML5 video, so CoffeeMovie normalizes imported subtitles into WebVTT once and keeps parsed cues in library metadata for UI navigation.

## Studio Cue Editing Flow

Studio is the place for precise subtitle repair. A timing edit changes the selected cue's `start` and `end`, then rewrites:

1. `library.json`
2. the app-local subtitle file under `subtitles/<movieId>/`
3. the generated WebVTT cache
4. the original local subtitle file, only when its source path is known and write-back is enabled

Paired English/Japanese tracks are linked by `groupKey`. When sync is enabled, editing cue `index = N` on one track copies the same start/end timestamps to cue `N` on the paired track. Text, tags, notes, and practice metrics remain track-specific.

## Subtitle Generation Jobs

Subtitle generation should be modeled as Studio jobs rather than as core storage behavior:

```text
Imported video
  -> WhisperXRunner produces .en.srt
  -> TranslationRunner produces .ja.srt
  -> Studio imports both tracks with the same groupKey
```

The first practical runner can call the existing WhisperX Python environment (`py -3.10 -m whisperx`). Japanese translation should use a provider-neutral adapter so an AI agent, API, or manual script can be swapped without changing the CoffeeMovie library format.

Studio now implements the first slice of this job model for English subtitle extraction. The `字幕生成` tab runs WhisperX as an external process for the selected movie, normalizes the output to `.en.srt`, and imports that file through the same subtitle import path used by manual drag/drop. Translation remains a separate runner boundary.

Implemented generation settings are intentionally stored as Studio preferences instead of core movie data:

- output directory
- Python command
- Python launcher arguments
- WhisperX model
- source language
- device, usually `cuda` or `cpu`
- compute type, such as `float16`, `float32`, or `int8`

This keeps the library portable while still allowing each workstation to point Studio at its own WhisperX environment.

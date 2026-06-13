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

## Drive Flow

Drive sync is planned as a Reader service because Android owns the cache and authentication state. Storage remains platform-neutral and only exposes cache index and JSON helpers.

Studio can export `.coffeemovie.json` sidecars beside a video or into a Drive sync folder. Reader-side Drive sync should prefer those sidecars before downloading large video files.

## Why WebVTT

Android video playback surfaces can attach WebVTT tracks directly through HTML5 video. SRT is common but not native to HTML5 video, so CoffeeMovie normalizes imported subtitles into WebVTT once and keeps parsed cues in library metadata for UI navigation.

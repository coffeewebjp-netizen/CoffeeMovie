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

## Current Code Structure

CoffeeMovie is now organized so future work can start from the feature area instead of from one large UI file. The WPF and MAUI pages still own UI composition, but long-running work, parsing, sync, and import/export rules live in services.

Shared code:

- `CoffeeMovie.Core/Models`: portable movie, video, subtitle, tag, playback, and cue learning-state models.
- `CoffeeMovie.Core/Services/MovieMetadataInferenceService.cs`: shared filename-to-series/season/episode inference and season/episode display formatting.
- `CoffeeMovie.Storage/Services`: JSON library storage, cache storage, subtitle parsing, sidecar/package creation, and content fingerprint calculation.

Studio code:

- `MainWindow.xaml.cs`: WPF shell, shared state, selected movie wiring, and small UI helpers.
- `MainWindow.Constants.cs`: built-in prompt text, default command templates, overlay defaults, and file-extension constants.
- `MainWindow.Shelf.cs`: movie shelf grouping, import/remove actions, and shelf refresh.
- `MainWindow.Selection.cs`: drag/drop, movie selection, metadata editing, and subtitle selection.
- `MainWindow.Preview.cs`: edit/full preview playback, seek state, subtitle overlay rendering, and overlay placement.
- `MainWindow.PreviewHandlers.cs`: preview buttons, cue jumping, fullscreen preview actions, and timing-control event handlers.
- `MainWindow.SceneRows.cs`: subtitle row rendering, row tags/notes persistence, cue timing edits, and paired-track timing sync.
- `MainWindow.Tags.cs`: tag management, tag picker UI, tag filtering, and highlight color behavior.
- `MainWindow.SubtitleGeneration.cs`: Studio UI coordination for WhisperX, Japanese translation, and AI memo jobs.
- `MainWindow.AiNotes.cs`: AI note generation trigger, sparse-note import, focus relocation, and quality validation.
- `MainWindow.DriveExport.cs`: Drive-folder selection and Reader package export.
- `MainWindow.Thumbnails.cs`: thumbnail capture button flow.
- `MainWindow.Types.cs`: WPF row/view helper types used by the partial files.
- `CoffeeMovie.Studio/Services`: testable services for learning-note import, external process execution, subtitle-generation jobs, tag filtering, and thumbnail capture.

Reader code:

- `MovieShelfPage.cs`: MAUI shelf layout, startup icon overlay animation, series/season tree shell, card UI, movie opening, and cache-state display.
- `MovieShelfPage.Tree.cs`: series -> season -> episode row construction and collapse state.
- `MovieShelfPage.Sync.cs`: Google Drive sync UI flow, sidecar/package download decisions, and progress text.
- `MovieShelfPage.Backup.cs`: local/shareable learning-state backup export/import UI.
- `MoviePlayerPage.cs`: MAUI player layout, learning panel layout, scene list, and page-level state.
- `MoviePlayerPage.Html.cs`: bridge URL parsing, JavaScript string cleanup, and player HTML dispatch helpers.
- `MoviePlayerPage.Subtitles.cs`: subtitle/memo switches, active cue binding, cue learning-state editing, and overlay visibility.
- `MoviePlayerPage.Shadowing.cs`: speech recognition flow, shadowing metrics, feedback, and text-to-speech replay.
- `MoviePlayerPage.Controls.cs`: fullscreen, transport controls, rewind controls, and subtitle position/alignment controls.
- `ReaderPlayerHtmlBuilder.cs`: HTML5 video surface, subtitle/memo overlay JavaScript, and WebView bridge script generation.
- `ReaderShadowingScorer.cs`: tokenization and edit-distance scoring for shadowing.
- `ReaderLibraryService.cs`: app-local movie library access and manual video/subtitle import.
- `ReaderLibraryService.Package.cs`: Drive package/sidecar import, package extraction, local state merge, and thumbnail payload handling.
- `ReaderLibraryService.LearningBackup.cs`: lightweight mobile learning-state backup export/import and merge rules.
- `GoogleDriveSyncService.cs`: small facade over auth, package listing, and download services for shelf UI callers.
- `GoogleDriveAuthService.cs`: OAuth configuration, PKCE browser auth, refresh-token storage, access-token refresh, and Drive folder ID parsing.
- `GoogleDrivePackageListingService.cs`: Drive file listing and package/sidecar pairing.
- `DrivePackageDownloadService.cs`: package/sidecar download, partial-file resume, retry handling, and cache-state reporting.

Development rule of thumb:

1. Start from the feature partial or service above.
2. Keep UI-only state in the page partial.
3. Move parsing, merge, command execution, Drive transfer, or validation behavior into a service when it can be tested without a UI surface.
4. Preserve package format, Android signing identity, Google OAuth client assumptions, and `.part` resume behavior unless the change is explicitly a migration.

## Reader Flow

1. User configures Google Drive or imports local video/subtitle files for testing.
2. Drive sync lists `.coffeemovie` packages and paired `.coffeemovie.json` sidecars from the configured folder.
3. Reader downloads sidecars first and compares `contentFingerprint` with local `SourceContentFingerprint`.
4. Unchanged sidecars are counted as unchanged; changed sidecars update the local movie shell.
5. Large package bytes are downloaded only when the local video cache is missing or an updated package is explicitly needed.
6. The player loads local video through a WebView-backed HTML5 video surface.
7. The custom overlay renders English subtitles, Japanese subtitles, AI/user memo lines, and shadowing feedback independently.

## Studio Flow

Studio prepares videos and subtitles before they are watched on mobile:

1. Import videos and subtitles through buttons or drag/drop.
2. Infer subtitle metadata from file names such as `.en.srt` and `.ja.srt`.
3. Keep English learning-target and Japanese translation tracks separate while linking them with `groupKey`.
4. Preview the video with a seek bar and subtitle overlay.
5. Show English above Japanese when paired subtitle display is enabled.
6. Edit cue start/end timing, nudge selected cues, and optionally sync timing to the paired track.
7. Store cue tags, notes, flag state, listening metrics, and shadowing metrics on the subtitle track.
8. Use the subtitle tag filter to inspect tagged cue rows inside the selected subtitle track, or across matching movies when a shelf subtitle-tag filter is active.
9. Export sidecar metadata for future Drive-first sync.

The full-size preview tab reuses the same subtitle-line selection logic as the edit preview, but has its own `MediaElement` and seek state. Both previews are driven by the shared preview timer so subtitle overlays continue to follow playback after media-open and seek operations.

## Drive Flow

Drive sync is implemented as a Reader service because Android owns the cache and authentication state. Storage remains platform-neutral and exposes package, sidecar, cache-index, and JSON helpers.

Studio exports a Drive-ready pair:

1. `.coffeemovie`: ZIP package containing `manifest.json`, video bytes, and subtitle files.
2. `.coffeemovie.json`: small sidecar containing the same comparison metadata, subtitle cues, and learning states.

Studio computes a `contentFingerprint` before export. If the existing sidecar in the configured Drive sync folder has the same fingerprint and the package file exists, Studio skips the write. If the fingerprint differs, Studio rewrites both package and sidecar and updates `exportedAt`.

Reader refreshes sidecars during sync, compares the incoming fingerprint to the local `SourceContentFingerprint`, and separates results into added/updated and unchanged. Video cache is kept only when the incoming video metadata describes the same video asset.

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

The first practical runner can call the existing WhisperX Python environment (`py -3.10 -m whisperx`). Japanese translation uses a provider-neutral external command adapter so an AI agent, API, or manual script can be swapped without changing the CoffeeMovie library format.

Studio implements both runner boundaries in the `字幕生成` tab:

1. WhisperX runs as an external process for the selected movie, normalizes the output to `.en.srt`, and imports that file through the same subtitle import path used by manual drag/drop.
2. Translation runs as a configurable external AI-AGENT command, receives an English SRT path, writes a Japanese SRT path, and then imports that `.ja.srt` through the same subtitle import path.

The translation adapter expands these placeholders before launching the external command:

- `{input}`: English SRT path
- `{output}`: Japanese SRT path that the agent must create
- `{inputDir}`: English SRT directory
- `{outputDir}`: Japanese SRT output directory
- `{promptFile}`: generated prompt file path
- `{prompt}`: prompt text, mainly for agents that accept inline prompts
- `{source}`: source language, normally `en`
- `{target}`: target language, normally `ja`
- `{movie}`: selected video path
- `{title}`: CoffeeMovie movie title

Before launching the command, Studio writes the current translation prompt to `{promptFile}`. The built-in base prompt follows the successful anime subtitle skill routine: keep timestamps stable, translate only subtitle text, avoid direct machine translation, infer the work's world and character voices, and output valid SRT only. Users can edit this prompt and reset it back to the base version. The default `codex-spark` command is a Studio preset: if no standalone `codex-spark` executable is configured, Studio resolves it to the local Codex CLI and runs `codex exec` as the translation agent. After the command exits successfully, Studio verifies that `{output}` exists and can be parsed as a subtitle file. CoffeeMovie does not store provider credentials or agent-specific state in the library.

Implemented generation settings are intentionally stored as Studio preferences instead of core movie data:

- output directory
- Python command
- Python launcher arguments
- WhisperX model
- source language
- device, usually `cuda` or `cpu`
- compute type, such as `float16`, `float32`, or `int8`
- translation command
- translation argument template
- translation source and target languages
- translation prompt override

This keeps the library portable while still allowing each workstation to point Studio at its own WhisperX and AI translation environments.

## Thumbnail Flow

Studio can capture a thumbnail from the current preview position. The current implementation shells out to `ffmpeg`, saves a JPEG under `thumbnail-cache`, stores the path and timestamp on `VideoAsset`, and shows the image in the Studio movie shelf. Studio can replay the saved thumbnail timestamp for five seconds to verify the chosen scene.

Studio embeds the thumbnail in both the `.coffeemovie` package and the lightweight `.coffeemovie.json` sidecar when a thumbnail exists. Android Reader writes the sidecar thumbnail to its app-local thumbnail cache during Drive sync, so the movie shelf can show cover images before the large video package is downloaded.

Reader also reuses the same thumbnail on the player surface while the WebView and video are loading. The overlay is hidden as soon as the embedded player reports playback.

## Series Metadata Flow

`Movie` stores optional `seriesTitle`, `seasonNumber`, and `episodeNumber` fields. Studio lets the user edit these fields directly and writes them into the package sidecar. Reader imports the same fields and renders the shelf as a collapsible series -> season -> episode tree.

## Reader Icon And Startup Flow

Reader uses PNG assets for the Android launcher icon and startup experience:

- `Resources/AppIcon/appicon.png`
- `Resources/Images/startup_icon.png`
- `Resources/Splash/splash_composite.png`

The project file registers `appicon.png` as the MAUI icon and the Android manifest explicitly maps the app to `@mipmap/appicon` and `@mipmap/appicon_round`. The startup overlay in `MovieShelfPage.cs` uses `startup_icon.png` on the same dark shelf background, then scales and fades the icon away once per page lifetime.

These images should stay alpha-enabled PNGs. If the source image has a white or checkerboard background baked in, regenerate the app assets before building so the Android splash and in-app startup overlay blend into `#05070B`.

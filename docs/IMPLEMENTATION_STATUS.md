# Implementation Status

This note captures the current CoffeeMovie implementation state after the PC Studio, Android Reader, Drive sync, subtitle-learning, and shadowing work.

## Windows Studio

- Imports videos by button or drag/drop.
- Imports SRT/WebVTT subtitles manually or by drag/drop.
- Infers `.en.srt` as the English learning-target track and `.ja.srt` as the Japanese translation track.
- Keeps English and Japanese tracks separate, linked by `groupKey`.
- Shows edit preview with seek bar, subtitle overlay, and full-size preview tab.
- Carries the edit-preview timeline position into the full-preview tab and can mirror the preview into an audio-enabled popup window while editing subtitles; the popup becomes the sole preview audio source while it is open.
- Provides 1-second, 5-second, and saved custom-second rewind/fast-forward controls in both preview surfaces; the custom interval accepts and displays 1–9999 seconds, Left/Right uses it, Shift selects 5 seconds, and Ctrl selects 1 second.
- Persists each movie playback position from Studio and resumes at that position on the next preview load. Preview seeking is transactional: thumb drags seek on completion, track clicks calculate the requested time directly, healthy paused media resumes without rebuilding, restored and active-playback seeks remain muted and delay `Play` until the asynchronous position request has entered the media pipeline, progress is monitored, and a stalled WPF media pipeline is automatically rebuilt with the requested position and play/pause intent. Periodic progress persistence is deferred and limited to 30-second intervals, while pause/stop/movie-switch actions still save immediately.
- Supports paired English/Japanese display and independent overlay placement for English, Japanese, AI note, and user note lines.
- Allows cue click/double-click jumping.
- Allows cue start/end editing, current-position boundary assignment, and millisecond nudges.
- Can sync timing edits across paired English/Japanese tracks while keeping text and learning metadata separate.
- Writes timing edits back to the app-local subtitle copy, generated WebVTT cache, and optionally the original subtitle file.
- Supports movie-level and subtitle-level tags.
- Provides direct editing for movie series title, season number, episode number, and movie tags.
- Groups the movie shelf by series title and season.
- Filters the movie shelf by text, movie tags, and subtitle tags.
- Lets movie and subtitle tag fields open a multi-select picker from registered tag definitions.
- Labels the shelf filters as movie search, movie tag filtering, and subtitle tag filtering.
- Filters subtitle cue rows by subtitle tags in addition to the flag-only filter.
- Can search tagged subtitle cue rows across matching movies when a subtitle tag filter is active.
- Lets cue tags be changed through the same multi-select picker used by movie and subtitle tags.
- Highlights tagged subtitle rows with a configurable color.
- Stores cue-level free-form notes, AI notes, listening metrics, shadowing metrics, and CoffeeLearning registration state.
- Generates English subtitles through a configurable WhisperX command.
- Offers normal English subtitle generation and an experimental review generation mode that runs WhisperX three times, compares text variance, and can merge missing/variant cues. It is not expected to repair global timing drift.
- Generates Japanese subtitles through a provider-neutral external command. The `codex-spark` preset resolves to the local Codex CLI.
- Generates AI learning notes through a configurable external command and prompt.
- Lets users edit translation and AI-note prompts and restore built-in base prompts.
- Registers the active English subtitle cue into CoffeeLearning from Studio, using matched Japanese subtitle text, AI/user notes, CEFR/point scoring, and combined movie/cue labels.
- Acquires CoffeeLearning Studio auth through a normal-browser localhost handoff, with manual Bearer header entry as a fallback.
- Supports CoffeeLearning scoring through AIAGENT, AI provider settings, CoffeeLearning server analysis, or local fallback estimates.
- Creates thumbnails from the current preview frame through `ffmpeg`.
- Saves thumbnail path and thumbnail timestamp on the video asset.
- Can replay the thumbnail timestamp for five seconds.
- Exports Drive-ready `.coffeemovie` packages and `.coffeemovie.json` sidecars.
- Can reflect every movie to the configured Drive folder in one action; unchanged fingerprints are skipped, metadata-only changes rewrite only the lightweight sidecar, and the large package is created or rewritten only when missing or when the video identity changed.
- Exports and imports a human-editable roundtrip folder for the selected movie, containing `manifest.json`, SRT subtitle files, and `notes.csv` for AI notes, user notes, subtitle tags, flags, and CoffeeLearning registration state.
- Embeds thumbnail images in reader packages and sidecars when a thumbnail exists.
- Skips package export when the current content fingerprint and thumbnail payload match the existing sidecar in the Drive sync folder.
- Keeps the loaded library as an in-memory working set so movie selection, filtering, and preview mirroring do not re-read `library.json` on normal UI paths.
- Stops preview timers and releases media sources on Studio shutdown.
- Uses the CoffeeMovie icon as the Windows application icon.
- Can be packaged as a per-user WiX MSI with Start Menu and desktop shortcuts.

## Android Reader

- Uses the stable Android identity `net.coffeewebjp.coffeemovie.reader`.
- Uses a transparent PNG app icon, Android launcher icon mapping, and a matching startup icon/splash asset.
- Shows a CoffeeBook-style startup overlay that briefly displays the app icon, zooms it, and fades into the shelf.
- Supports Google Drive OAuth and folder configuration.
- Lists `.coffeemovie` packages and paired `.coffeemovie.json` sidecars from the configured Drive folder.
- Refreshes sidecar metadata before downloading large package bytes.
- Shows sidecar thumbnail images on the movie shelf before the large package is downloaded.
- Shows the thumbnail on the player surface while the WebView/video is loading, then hides it when playback starts.
- Shows the shelf as a collapsible series -> season -> episode tree using series title, season number, and episode number when available.
- Keeps the loaded shelf library in memory and rebuilds shelf rows from that working set instead of re-reading `library.json` when returning from playback or expanding/collapsing groups.
- Compares sidecar `contentFingerprint` with the local `SourceContentFingerprint`.
- Reports Drive sync results as added/updated, unchanged, sidecar-missing, and failed.
- Keeps existing video cache only when the incoming package describes the same video asset.
- Supports resumable package download with partial file reuse and restart choice.
- Plays video in a WebView-backed HTML5 player.
- Shows English subtitles, Japanese subtitles, and memo overlay lines independently.
- Supports memo overlay from AI note plus user note.
- Collapses subtitle-file line breaks for video overlay display so short Japanese lines do not waste vertical space.
- Supports subtitle vertical position: bottom, middle, top.
- Supports subtitle horizontal alignment: left, center, right.
- Persists the last playback position and resumes near that point instead of restarting from the beginning.
- Provides fullscreen controls for pause/resume and symmetric 1-second, 5-second, and custom rewind/fast-forward; the shared custom interval accepts and displays 1–9999 seconds.
- Provides fullscreen shadowing when English subtitles are enabled and an active English cue exists.
- Highlights the target English subtitle during shadowing recognition.
- Shows recognized speech text and OK/NG accuracy feedback.
- Stores shadowing OK/NG counts, last transcript, and accuracy in cue learning state.
- Uses device text-to-speech to replay the original English subtitle after a failed shadowing attempt.
- Registers the active cue into CoffeeLearning from the learning panel or fullscreen controls, marks cues as registered only after a successful server response, and shows that state in the player.
- Shows CoffeeLearning登録済 beside the user memo and in the memo overlay, uploads the phone registration snapshot after successful registration, and merges every device snapshot during Drive sync.
- Provides CoffeeLearning login/configuration/scoring settings from the shelf Other menu, including AI provider scoring options for GPT/OpenAI, Gemini, DeepSeek, and local OpenAI-compatible LLMs.
- Can export and import a lightweight learning-state backup JSON from the shelf `Backup` button.
- The learning-state backup stores movie tags, playback state, subtitle cue tags, notes, AI notes, listening metrics, and shadowing metrics without duplicating video bytes.

## Sync Contract

Studio writes two files to the Drive sync folder:

- `<title>_<shortMovieId>.coffeemovie`
- `<title>_<shortMovieId>.coffeemovie.json`

The sidecar is the lightweight comparison file. It contains:

- `sourceMovieId`
- `contentFingerprint`
- `exportedAt`
- package file name and package size
- series title, season number, and episode number
- video metadata
- subtitle cues
- cue learning states, including CoffeeLearning registration state
- optional thumbnail image payload

The current diff rule is:

1. Studio computes a `contentFingerprint` and compares the actual packaged video identity.
2. If the existing sidecar has the same fingerprint and thumbnail payload, Studio skips export.
3. If metadata changed but the packaged video is identical, Studio rewrites only the sidecar.
4. If the package is missing or the video identity changed, Studio writes both package and sidecar.
5. Reader downloads sidecars first.
6. If the incoming fingerprint matches local `SourceContentFingerprint`, Reader records it as unchanged.
7. If the fingerprint differs, Reader updates the local shell; a later package download extracts video bytes without replacing newer sidecar metadata.

The fingerprint includes movie identity, title, series title, season number, episode number, description, video identity fields, thumbnail timestamp, thumbnail image hash when present, movie tags, subtitle track metadata, cue timing/text, cue tags, notes, AI notes, CoffeeLearning registration state, and listening/shadowing metrics.

File size is used for download progress and cache integrity checks. It is not the primary content-difference signal.

## Known Gaps

- Shadowing input audio playback is not implemented because Android `SpeechRecognizer` returns recognition text but not a reusable audio file.
- Drive sync still downloads sidecars on each sync. This is intentional because sidecars are small and are the authoritative comparison surface; package bytes are downloaded only when a package is missing or the sidecar fingerprint differs from the local cache.
- If Google Drive contains duplicate package names, the current list logic may pick the first matching sidecar. The intended workflow is overwrite-in-place from the desktop Drive folder.
- CoffeeLearning registration state now has automatic Drive sharing. The broader learning-state backup (notes, tags, playback, and practice metrics) is still a shareable/importable JSON file; automatic Drive upload and restore for those remaining fields is the next data-safety step.
- English review generation is kept as an experimental quality aid for missing/variant subtitle text; global timing drift still needs a separate diagnosis/alignment feature.

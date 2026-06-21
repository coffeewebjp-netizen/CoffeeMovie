# Implementation Status

This note captures the current CoffeeMovie implementation state after the PC Studio, Android Reader, Drive sync, subtitle-learning, and shadowing work.

## Windows Studio

- Imports videos by button or drag/drop.
- Imports SRT/WebVTT subtitles manually or by drag/drop.
- Infers `.en.srt` as the English learning-target track and `.ja.srt` as the Japanese translation track.
- Keeps English and Japanese tracks separate, linked by `groupKey`.
- Shows edit preview with seek bar, subtitle overlay, and full-size preview tab.
- Carries the edit-preview timeline position into the full-preview tab and can mirror the preview into a muted popup window while editing subtitles.
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
- Stores cue-level free-form notes, AI notes, listening metrics, and shadowing metrics.
- Generates English subtitles through a configurable WhisperX command.
- Offers normal English subtitle generation and review generation that runs WhisperX three times, merges missing cues, and stabilizes cue timing by consensus.
- Generates Japanese subtitles through a provider-neutral external command. The `codex-spark` preset resolves to the local Codex CLI.
- Generates AI learning notes through a configurable external command and prompt.
- Lets users edit translation and AI-note prompts and restore built-in base prompts.
- Creates thumbnails from the current preview frame through `ffmpeg`.
- Saves thumbnail path and thumbnail timestamp on the video asset.
- Can replay the thumbnail timestamp for five seconds.
- Exports Drive-ready `.coffeemovie` packages and `.coffeemovie.json` sidecars.
- Embeds thumbnail images in reader packages and sidecars when a thumbnail exists.
- Skips package export when the current content fingerprint and thumbnail payload match the existing sidecar in the Drive sync folder.
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
- Provides fullscreen controls for pause/resume, one-second rewind, five-second rewind, custom rewind, and custom rewind setting.
- Provides fullscreen shadowing when English subtitles are enabled and an active English cue exists.
- Highlights the target English subtitle during shadowing recognition.
- Shows recognized speech text and OK/NG accuracy feedback.
- Stores shadowing OK/NG counts, last transcript, and accuracy in cue learning state.
- Uses device text-to-speech to replay the original English subtitle after a failed shadowing attempt.
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
- cue learning states
- optional thumbnail image payload

The current diff rule is:

1. Studio computes a package `contentFingerprint`.
2. If the existing sidecar has the same fingerprint, package file, and thumbnail payload, Studio skips export.
3. If the fingerprint differs, Studio rewrites the package and sidecar and updates `exportedAt`.
4. Reader downloads sidecars first.
5. If the incoming fingerprint matches local `SourceContentFingerprint`, Reader records it as unchanged.
6. If the fingerprint differs, Reader updates the local shell and marks the package as updated.

The fingerprint includes movie identity, title, series title, season number, episode number, description, video identity fields, thumbnail timestamp, thumbnail image hash when present, movie tags, subtitle track metadata, cue timing/text, cue tags, notes, AI notes, and listening/shadowing metrics.

File size is used for download progress and cache integrity checks. It is not the primary content-difference signal.

## Known Gaps

- Shadowing input audio playback is not implemented because Android `SpeechRecognizer` returns recognition text but not a reusable audio file.
- Drive sync still downloads sidecars on each sync. This is intentional because sidecars are small and are the authoritative comparison surface; package bytes are downloaded only when a package is missing or the sidecar fingerprint differs from the local cache.
- If Google Drive contains duplicate package names, the current list logic may pick the first matching sidecar. The intended workflow is overwrite-in-place from the desktop Drive folder.
- Learning-state backup is currently a shareable/importable JSON file. Drive-native automatic upload and restore is the next data-safety step.

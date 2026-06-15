# CoffeeMovie Design

## Product Shape

CoffeeMovie is the video sibling of CoffeeBook. CoffeeBook treats books as packages and page images as cacheable reading assets. CoffeeMovie should treat video files as large source assets, and subtitles/scene metadata as small sidecars that can be synced and inspected before the video is downloaded.

The mobile reader should prioritize:

- fast opening of already cached videos
- subtitle-backed scene jumping
- simple local import for early testing
- Google Drive sync that avoids duplicate downloads

The Windows Studio should prioritize:

- importing large local video files into the CoffeeMovie library
- importing SRT / WebVTT subtitles and validating cue parsing
- showing subtitle-backed scene markers before mobile sync
- exporting `.coffeemovie` reader packages plus `.coffeemovie.json` sidecars for Drive-first metadata checks

Current Studio implementation covers the first useful learning-workbench loop: import a video, import or generate subtitles, inspect cues, tag difficult lines, repair cue timing, preview both English and Japanese subtitles, create a thumbnail from the current frame, and write Drive-ready package metadata. Japanese subtitle generation is implemented as an external TranslationRunner boundary so the app can call an AI-AGENT without taking a hard dependency on one provider.

Current Reader implementation covers the first mobile watcher loop: Drive sync configuration, sidecar-first metadata refresh, resumable package download, video playback with independent subtitle/memo overlays, cue notes/tags, and speech-recognition based shadowing practice.

## Android App Identity

The Reader `ApplicationId` is fixed to `net.coffeewebjp.coffeemovie.reader`. Do not change it casually; Google Cloud Android OAuth clients bind this package name together with the signing certificate SHA-1.

Release APK signing uses `.tools/android-signing/CoffeeMovie.Reader.Signing.props` when that file exists. The canonical CoffeeMovie Reader signing SHA-1 is `B2:1B:F2:42:DC:4F:FC:E7:F9:A5:CE:85:F4:5D:0C:A3:81:ED:29:66`, matching the existing Pixel install and the Google Cloud Android OAuth client. The keystore is `.tools/android-signing/coffeemovie-reader-release.jks`. These files are intentionally outside Git and must be backed up separately. Restoring the same two files on another PC keeps the SHA-1 stable, so Android updates and Google Cloud OAuth continue to see the app as the same identity.

Debug APKs signed with a machine-local debug keystore will have a different SHA-1. For installing over an app from another PC, use the fixed release keystore build.

## CoffeeBook-Compatible Mobile Build Principle

CoffeeMovie Reader should keep the same mobile-app philosophy as CoffeeBook Reader:

- Studio prepares and exports durable metadata; Reader focuses on watching, sync, cache, lightweight cue tagging, practice, and playback.
- Reader must not depend on a single developer PC's global Android SDK, JDK, debug keystore, or absolute user profile paths.
- Local build tools may live under `.tools/` and are intentionally outside Git. If CoffeeMovie `.tools/` is absent, Reader build settings may reuse the sibling CoffeeBook `COFFEEBOOK/.tools` SDK/JDK as a local fallback.
- Release APK identity must remain stable through `ApplicationId` plus the backed-up release keystore and signing props.
- Recreating the release keystore is a breaking identity change and should only happen intentionally.
- Build notes and scripts should prefer repo-relative paths and generated props over hard-coded machine-specific paths.

## Data Ownership

The Android reader owns the local library state:

- library metadata
- imported subtitle cues
- playback position
- local cache index
- Drive file identity and cache freshness metadata

Google Drive is the remote source of truth for shared files, but local app storage is the source of truth for what has already been cached on the device.

## Subtitle Handling

Supported import formats:

- `.srt`
- `.vtt`

SRT is parsed and converted to WebVTT at import time. The original subtitle text is useful for debugging, but playback uses the generated `.vtt` file. Parsed cues are stored in the library so the reader can show a scene list without reparsing the file every time.

Scene jump candidates are generated from subtitle cues. This is intentionally simple for the first version: if a cue has text, it can be used as a jump target.

Subtitle files with language suffixes are imported as separate tracks in the same group. For example:

- `episode-title.en.srt` becomes an English `learningTarget` track.
- `episode-title.ja.srt` becomes a Japanese `translation` track.
- both tracks share `groupKey = "episode-title"`.

Do not merge English and Japanese text into one cue list. Keep them as separate tracks and add cue-linking or paired display on top of those tracks. This keeps shadowing, translation hints, and future subtitle-only views cleanly separated.

## Cue Timeline Editing

Timing drift should be corrected at the cue level, not as a global subtitle delay. Global delay is useful as an emergency playback aid, but it is a poor editing model when only a few spoken lines are early or late.

Studio should expose each cue as an editable timeline unit:

- edit `start` and `end` directly in the scene grid
- set `start` or `end` from the current preview position
- keep small `+/- milliseconds` nudges as a fast adjustment tool
- optionally synchronize the same cue index across paired tracks with the same `groupKey`
- rewrite the app-local subtitle file and generated WebVTT cache after timing edits
- write back to the original subtitle source only when Studio knows the original local path and the user explicitly allows it

When English and Japanese tracks share a `groupKey`, paired timing edits should default to synchronizing the same cue index. The text remains separate; only timing is copied. This keeps translation and shadowing data aligned without merging the cue lists.

Cue IDs should remain stable during manual timing edits so tags, notes, and practice history do not disappear. The track-local cue `index` remains the fallback link for older data and paired-track synchronization.

## Subtitle Generation Pipeline

Studio should eventually own the end-to-end subtitle workflow:

1. run WhisperX against the imported video to generate `[video].en.srt`
2. translate the English SRT into `[video].ja.srt`
3. import both files as grouped subtitle tracks
4. show both tracks in the preview and allow cue-level timing cleanup

The app should not hard-code one translation provider into the core data model. Treat subtitle generation as a job with pluggable runners:

- `WhisperXRunner`: external Python/WhisperX process, usually GPU-backed
- `TranslationRunner`: provider-neutral adapter that can be implemented by an AI agent, local script, or API
- `ManualImportRunner`: fallback path for users who generate subtitles outside CoffeeMovie

For the current workstation, the known external workflow lives under `D:\英語\subtitile` and uses `py -3.10 -m whisperx` to generate English SRT files. CoffeeMovie should make this configurable rather than baking that path into source.

Japanese translation is best handled by an AI-capable runner rather than a simple dictionary translator because anime subtitles need context, character voice, and natural phrasing. An AI-Agent / codex-spark style adapter is a reasonable runner, but CoffeeMovie should persist only ordinary `.srt` / `.vtt` outputs so the app remains independent from that agent.

Implemented status:

- WhisperX English generation runs from the Studio `字幕生成` tab.
- The output directory, Python command, launcher arguments, model, language, device, and compute type are saved as Studio preferences.
- Generated English SRT files are normalized to `.en.srt` and imported through the normal subtitle importer.
- Japanese translation generation runs behind a configurable external `TranslationRunner` command and must produce `.ja.srt` before import.
- The translation command receives placeholder-expanded paths such as `{input}` for the English SRT and `{output}` for the Japanese SRT.
- The default translation prompt is based on the successful anime subtitle Skills routine: preserve SRT timing, translate only dialogue text, infer worldbuilding and character voice, avoid direct machine translation, and output valid SRT only.
- Users can edit the translation prompt and restore the base prompt from Studio.
- The default runner name is `codex-spark`. In Studio this is a preset that resolves to the local Codex CLI and runs `codex exec` non-interactively for subtitle translation.

## Cue Learning Model

CoffeeMovie should treat each subtitle cue as a practice unit, not just as timed text. This is the core difference between ordinary subtitle playback and a learning-oriented watcher.

Cue-level learning data belongs to the subtitle track, separate from movie-level tags and scene markers:

- movie tags describe the whole title
- subtitle tags describe specific spoken lines and learning states
- scene markers describe video moments

Each subtitle cue should have a stable `id` and a track-local `index`. Learning state references that cue and can store:

- free-form tags such as `flag`, `hard`, `listening`, `shadowing`, `idiom`, `retry`
- notes
- listening practice accuracy
- shadowing practice accuracy
- attempt counts and last-practiced timestamps
- last recognized transcript, when speech recognition is used

`flag` is a normal subtitle tag, not a separate concept. UI can expose it as a quick checkbox, but persistence and filtering should treat it as a subtitle tag so future tag views can show video tags and subtitle tags consistently.

Accuracy values are normalized as `0.0` to `1.0`. UI can display them as percentages.

The PC implementation can edit and inspect this metadata. The Android watcher uses the same model for quick cue practice and shadowing, including OK/NG counts, last transcript, and accuracy metrics on the active English cue.

## Shadowing / Watcher Plan

The Android watcher should offer cue-based practice modes:

1. Repeat-after: play the cue, then record the user speaking it.
2. Shadowing: play the cue while the user speaks along, preferably with headphones.
3. Review queue: play only flagged or tagged cues.

The implemented first scoring layer compares recognized text with the subtitle text. This answers "could I say the line accurately enough to be recognized?" before attempting deeper phoneme-level pronunciation scoring.

Later scoring can add:

- timing delta from cue duration
- missing or substituted words
- slow/late-start tags
- pronunciation-specific scoring through a dedicated model or service

## Cache Strategy

Cache identity uses stable remote metadata where available:

- Drive file ID
- Drive modified time
- size
- content fingerprint from the sidecar

The reader refreshes sidecars often because they are small. It skips semantic updates when the incoming `contentFingerprint` matches local `SourceContentFingerprint`. It keeps an existing video cache only when the incoming package describes the same video asset.

Suggested local paths:

```text
FileSystem.AppDataDirectory/
  library.json
  cache-index.json
  videos/<movieId>/<video file>
  subtitles/<movieId>/<subtitle file>
  subtitles/<movieId>/<subtitle file>.vtt
```

Temporary and resumable Drive downloads live under the Reader app data/cache drive-import paths. Partial files are reused when the user resumes a failed download.

## Google Drive Sync Plan

CoffeeMovie follows the CoffeeBook handoff shape: Studio writes durable packages to a Google Drive for desktop sync folder, Google Drive moves them through the cloud, and Reader treats Drive as a distribution source rather than as its local database.

The Drive sync service now:

1. List files in the configured folder with `pageSize=1000`.
2. Prefer `.coffeemovie.json` sidecars for metadata checks.
3. Compare sidecar `contentFingerprint` against the local movie shell.
4. Count unchanged packages separately from added/updated packages.
5. Download package bytes only when the user needs a missing or updated local cache.
6. Resume partial package downloads when possible.

The `.coffeemovie` package is a ZIP container with the video, subtitles, and `manifest.json`. Video entries are stored without recompression, so packaging is mostly a copy operation rather than a CPU-heavy video encode. The paired `.coffeemovie.json` sidecar contains the same small learning metadata needed to create or compare a library shell without downloading the large package.

Studio computes the same `contentFingerprint` before export. If the existing sidecar in the Drive sync folder has the same fingerprint and the package file exists, Studio skips export so the desktop Drive client has nothing new to upload. If the fingerprint differs, Studio rewrites both files and updates `exportedAt`.

Reader should treat local cue tags, user notes, shadowing counts, and playback state as device-owned learning progress. Package updates may refresh video/subtitle/AI-note metadata, but future merge logic should preserve newer local practice data unless the user explicitly resets or overwrites it.

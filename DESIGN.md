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
- exporting `.coffeemovie.json` sidecars for Drive-first metadata checks

## Android App Identity

The Reader `ApplicationId` is fixed to `net.coffeewebjp.coffeemovie.reader`. Do not change it casually; Google Cloud Android OAuth clients bind this package name together with the signing certificate SHA-1.

Release APK signing uses `.tools/android-signing/CoffeeMovie.Reader.Signing.props` when that file exists. The generated keystore is `.tools/android-signing/coffeemovie-reader-release.jks`. These files are intentionally outside Git and must be backed up separately. Restoring the same two files on another PC keeps the SHA-1 stable, so Google Cloud does not need a new Android OAuth client.

Debug APKs signed with a machine-local debug keystore will have a different SHA-1. For Google Drive verification, use the fixed release keystore build unless a shared debug signing setup is intentionally added later.

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

## Cache Strategy

Cache identity should use stable remote metadata where available:

- Drive file ID
- Drive modified time
- size
- optional content fingerprint when available

The reader should skip video download when the cache entry exists, the local file exists, and the Drive metadata still matches. Subtitle sidecars are small enough to refresh often.

Suggested local paths:

```text
FileSystem.AppDataDirectory/
  library.json
  cache-index.json
  videos/<movieId>/<video file>
  subtitles/<movieId>/<subtitle file>
  subtitles/<movieId>/<subtitle file>.vtt
```

Temporary Drive downloads should live under `FileSystem.CacheDirectory/drive-imports`.

## Google Drive Sync Plan

The Drive sync service should:

1. List files in the configured folder with `pageSize=1000`.
2. Prefer `.coffeemovie.json` sidecars for metadata checks.
3. Compare Drive file ID, modified time, and size against `cache-index.json`.
4. Download video bytes only when no matching local file exists.
5. Download or refresh subtitles before opening the player.

This mirrors CoffeeBook's sidecar-first sync idea, but avoids packaging videos into a ZIP because video files are too large for frequent repackaging.

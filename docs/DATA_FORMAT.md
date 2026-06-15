# Data Format

## Library

`library.json` stores movie records:

```json
{
  "studio": {
    "subtitleTagHighlightColor": "#F6C945",
    "showDualSubtitles": true,
    "whisperOutputDirectory": "D:/英語/subtitile",
    "whisperPythonCommand": "py",
    "whisperPythonArguments": "-3.10 -m whisperx",
    "whisperModel": "medium",
    "whisperLanguage": "en",
    "whisperDevice": "cuda",
    "whisperComputeType": "float16",
    "translationCommand": "codex-spark",
    "translationArguments": "exec --full-auto -C \"{outputDir}\" --add-dir \"{inputDir}\" --skip-git-repo-check \"You are codex-spark for CoffeeMovie. Read the prompt file at {promptFile}, translate {input}, and write the Japanese SRT to {output}.\"",
    "translationSourceLanguage": "en",
    "translationTargetLanguage": "ja",
    "translationPrompt": "custom prompt text, or null to use the Studio default"
  },
  "tagDefinitions": [
    {
      "name": "favorite",
      "scope": "Movie",
      "sortOrder": 0
    },
    {
      "name": "flag",
      "scope": "Subtitle",
      "sortOrder": 0
    },
    {
      "name": "shadowing",
      "scope": "Subtitle",
      "sortOrder": 1
    }
  ],
  "movies": [
    {
      "id": "movie-abc123",
      "title": "Sample",
      "video": {
        "sourceKind": "LocalFile",
        "sourceUri": "content://...",
        "cachePath": ".../videos/movie-abc123/sample.mp4",
        "fileName": "sample.mp4",
        "contentType": "video/mp4",
        "sizeBytes": 123456,
        "thumbnailPath": ".../thumbnail-cache/movie-abc123.jpg",
        "thumbnailTimestampSeconds": 123.45
      },
      "subtitleTracks": [
        {
          "id": "sub-ja",
          "label": "Japanese",
          "language": "ja",
          "role": "translation",
          "groupKey": "sample",
          "format": "Srt",
          "sourceUri": "D:/subtitles/sample.ja.srt",
          "localPath": ".../subtitles/movie-abc123/sample.srt",
          "vttCachePath": ".../subtitles/movie-abc123/sample.vtt",
          "cueCount": 240,
          "cues": [
            {
              "id": "cue-2f5c4d7a9b1e0330",
              "index": 1,
              "start": "00:00:01",
              "end": "00:00:03.2500000",
              "text": "I can't believe you did that."
            }
          ],
          "cueLearningStates": [
            {
              "cueId": "cue-2f5c4d7a9b1e0330",
              "cueIndex": 1,
              "isFlagged": true,
              "tags": ["flag", "hard", "shadowing"],
              "note": "Tense mistake: did/do",
              "listening": {
                "attemptCount": 3,
                "lastAccuracy": 0.8,
                "bestAccuracy": 1.0,
                "lastPracticedAt": "2026-06-13T08:30:00Z"
              },
              "shadowing": {
                "attemptCount": 5,
                "lastAccuracy": 0.6,
                "bestAccuracy": 0.9,
                "lastTranscript": "I can't believe you do that.",
                "lastPracticedAt": "2026-06-13T08:35:00Z"
              }
            }
          ]
        }
      ],
      "sceneMarkers": []
    }
  ]
}
```

Accuracy values are stored from `0.0` to `1.0` and displayed as percentages in UI. `cueLearningStates` are separate from movie-level tags so a whole video can be categorized independently from specific lines that need listening or shadowing practice. `isFlagged` is kept as a compatibility/quick-check field, but `flag` is also stored in `tags` and should be treated as the canonical tag concept.

`studio` stores Windows Studio preferences only. Reader may ignore these values. Subtitle tag color, dual subtitle display, and overlay layout affect the Studio preview UI. Whisper, translation, and AI-note settings describe local external generation environments and should not be treated as portable project requirements.

Translation settings are an external-command contract. Studio expands placeholders in `translationArguments` before launch:

- `{input}`: English SRT path
- `{output}`: Japanese SRT path
- `{inputDir}`: English SRT directory
- `{outputDir}`: Japanese SRT output directory
- `{promptFile}`: generated prompt file path
- `{prompt}`: prompt text
- `{source}`: source language
- `{target}`: target language
- `{movie}`: video path
- `{title}`: movie title

The command must write a valid Japanese SRT to `{output}`. Studio then imports it as an ordinary translation track. `codex-spark` is a Studio preset that resolves to the local Codex CLI and runs it as the translation agent. `translationPrompt` is optional; when absent, Studio uses the built-in anime subtitle translation prompt derived from the successful Skills routine. Users can edit the prompt and reset it to that base version from Studio.

`sourceUri` on subtitle tracks is optional. Studio sets it for local files imported through the Windows file picker or drag/drop. If present and write-back is enabled, cue timing edits can be written back to the original subtitle file; otherwise only the app-local copy and WebVTT cache are updated.

`groupKey` links separate subtitle tracks for the same timeline. English and Japanese subtitles should remain separate tracks, while cue timing edits can synchronize matching `index` values across tracks with the same `groupKey`.

`role` describes how a subtitle track is used:

- `LearningTarget`: usually English, used as the main listening/shadowing line
- `Translation`: usually Japanese, shown as a paired helper line
- `Transcript`: same-language transcript or future generated transcript track
- `Unknown`: manually imported or unclassified subtitle

Studio infers `.en.srt` as `LearningTarget`, `.ja.srt` / `.jp.srt` / `.jpn.srt` as `Translation`, and removes that language suffix to create a shared `groupKey`.

`cueLearningStates` are keyed by stable cue `id`, with `cueIndex` kept as a fallback for older data and paired-track timing sync. Timing edits should preserve cue IDs so tags and practice metrics survive subtitle repair.

## Cache Index

`cache-index.json` stores cache freshness:

```json
{
  "entries": [
    {
      "sourceKey": "gdrive://files/abc",
      "movieId": "movie-abc123",
      "localPath": ".../videos/movie-abc123/sample.mp4",
      "sizeBytes": 123456,
      "sourceModifiedAt": "2026-06-07T12:00:00Z"
    }
  ]
}
```

## Reader Package And Sidecar

Studio exports Drive-ready reader packages as a pair:

- `.coffeemovie`: ZIP package with `manifest.json`, the video file, subtitle files, and the optional thumbnail image.
- `.coffeemovie.json`: small sidecar used by Reader for sync checks and shelf shells before downloading the video package.

Both manifest and sidecar use schema version 1. The sidecar includes subtitle cues, learning states, and a small Base64 thumbnail payload when available, so tags, AI notes, user notes, shadowing OK/NG counts, and shelf thumbnails can travel with a video.

```json
{
  "schemaVersion": 1,
  "packageType": "reader-sidecar",
  "sourceMovieId": "movie-abc123",
  "contentFingerprint": "sha256...",
  "packageFileName": "Sample_movie-ab.coffeemovie",
  "packageSizeBytes": 1234567890,
  "exportedAt": "2026-06-14T00:00:00Z",
  "movie": {
    "id": "movie-abc123",
    "title": "Sample",
    "seriesTitle": "Frieren: Beyond Journey's End",
    "seasonNumber": 1,
    "episodeNumber": 2,
    "durationSeconds": 3600,
    "tags": ["anime", "shadowing"],
    "createdAt": "2026-06-14T00:00:00Z",
    "updatedAt": "2026-06-14T00:00:00Z"
  },
  "video": {
    "fileName": "sample.mp4",
    "packagePath": "video/sample.mp4",
    "sizeBytes": 123456,
    "modifiedAt": "2026-06-07T12:00:00Z",
    "thumbnailFileName": "movie-abc123.jpg",
    "thumbnailPackagePath": "thumbnails/movie-abc123.jpg",
    "thumbnailContentType": "image/jpeg",
    "thumbnailDataBase64": "...",
    "thumbnailTimestampSeconds": 123.45
  },
  "subtitles": [
    {
      "id": "sub-en",
      "fileName": "sample.ja.srt",
      "sourceFileName": "sample.ja.srt",
      "language": "ja",
      "role": "Translation",
      "label": "Japanese",
      "packagePath": "subtitles/sub-ja/sample.ja.srt",
      "vttPackagePath": "subtitles/sub-ja/track.vtt",
      "cueCount": 240,
      "cues": [
        {
          "id": "cue-1",
          "index": 1,
          "startSeconds": 1.0,
          "endSeconds": 3.25,
          "text": "Hello."
        }
      ],
      "learningStates": [
        {
          "cueId": "cue-1",
          "cueIndex": 1,
          "tags": ["flag"],
          "note": "personal note",
          "aiNote": "CEFR A2: basic greeting.",
          "shadowing": {
            "attemptCount": 3,
            "okCount": 2,
            "ngCount": 1
          }
        }
      ]
    }
  ]
}
```

The `.coffeemovie` manifest has `packageType: "reader"` and the same metadata, plus package-relative paths for the embedded files. Video bytes are stored without recompression. Subtitle text files and thumbnail images are compressed normally because they are small.

`contentFingerprint` is the primary sync-difference signal. It includes movie identity, title, series title, season number, episode number, description, video identity fields, thumbnail timestamp, thumbnail image hash when present, movie tags, subtitle track metadata, cue timing/text, cue tags, user notes, AI notes, and listening/shadowing metrics. File size and Drive modified time are used for download progress, resume validation, and cache integrity checks, not as the primary semantic-difference signal.

Studio export behavior:

1. Compute the new `contentFingerprint`.
2. Read the existing sidecar from the configured Drive sync folder when it exists.
3. If the existing sidecar fingerprint, thumbnail payload, and package file match, skip writing both files.
4. If the fingerprint differs, write a new package and sidecar and update `exportedAt`.

Reader sync behavior:

1. List packages and sidecars in the configured Drive folder.
2. Download sidecars first.
3. If sidecar `contentFingerprint` equals local `SourceContentFingerprint`, count it as unchanged.
4. If it differs, update the local movie shell and mark it as added/updated.
5. Keep the existing video cache only when the incoming video metadata describes the same video asset.

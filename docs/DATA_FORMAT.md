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
    "whisperComputeType": "float16"
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
        "sizeBytes": 123456
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

`studio` stores Windows Studio preferences only. Reader may ignore these values. Subtitle tag color and dual subtitle display affect the Studio preview UI. Whisper settings describe the local external generation environment and should not be treated as portable project requirements.

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

## Sidecar

Future Drive sidecars should use `.coffeemovie.json`:

```json
{
  "schemaVersion": 1,
  "movie": {
    "id": "movie-abc123",
    "title": "Sample",
    "durationSeconds": 3600
  },
  "video": {
    "fileName": "sample.mp4",
    "driveFileId": "abc",
    "sizeBytes": 123456,
    "modifiedAt": "2026-06-07T12:00:00Z"
  },
  "subtitles": [
    {
      "fileName": "sample.ja.srt",
      "language": "ja",
      "label": "Japanese"
    }
  ]
}
```

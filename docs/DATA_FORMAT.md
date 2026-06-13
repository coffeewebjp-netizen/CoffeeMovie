# Data Format

## Library

`library.json` stores movie records:

```json
{
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
          "format": "Srt",
          "localPath": ".../subtitles/movie-abc123/sample.srt",
          "vttCachePath": ".../subtitles/movie-abc123/sample.vtt",
          "cueCount": 240
        }
      ],
      "sceneMarkers": []
    }
  ]
}
```

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

# CoffeeMovie

CoffeeMovie is a prototype video reader for Android, following the CoffeeBook split:

- `CoffeeMovie.Core`: movie, subtitle, scene, and cache models
- `CoffeeMovie.Storage`: local library JSON, subtitle parsing, WebVTT conversion, and cache index storage
- `CoffeeMovie.Studio`: Windows WPF studio for importing videos/subtitles and writing sidecars
- `CoffeeMovie.Reader`: .NET MAUI Android reader for importing local videos/subtitles and jumping by subtitle cue
- `CoffeeMovie.Verification`: small no-package verification app for parser and storage behavior

## What This Targets

- スマホで動画を開き、見たいシーンへ素早く移動する。
- SRT / WebVTT 字幕を取り込み、字幕キューをシーンジャンプ候補として使う。
- Google Drive 連携時は、Drive メタデータとローカルキャッシュを照合し、同じ動画を毎回ダウンロードしない。

## Current Prototype

The first reader implementation supports local video import and subtitle import. Imported SRT subtitles are converted to WebVTT so the Android reader can attach them to the HTML5 video player surface. Subtitle cues are also exposed as jump targets under the player.

Windows Studio has grown into the preparation workspace for subtitle-learning workflows. It now supports drag/drop import, preview playback with a seek bar and subtitle overlay, full-size preview playback, paired English/Japanese subtitle display, cue click/double-click jumping, cue-level timing edits, tag/highlight metadata, and `.coffeemovie.json` sidecar export.

Google Drive integration is represented in the data and cache design first. The next implementation step is a Drive sync service that lists configured folder files, downloads sidecar metadata first, and downloads video bytes only when the local cache is missing or stale.

## Run Windows Studio

```powershell
cd C:\work\CoffeeMovie
dotnet run --project src\CoffeeMovie.Studio\CoffeeMovie.Studio.csproj
```

Build output:

```text
src\CoffeeMovie.Studio\bin\Debug\net10.0-windows\CoffeeMovie.Studio.exe
```

Studio currently supports local video import, subtitle import, subtitle scene inspection, preview playback, full-size preview playback, cue-level subtitle timing edits, WhisperX English subtitle generation, and `.coffeemovie.json` sidecar export.

The Studio `字幕生成` tab can call a local WhisperX Python environment to create `[video].en.srt` for the selected movie, then automatically import the generated English subtitle track. The default command is:

```text
py -3.10 -m whisperx <video> --model medium --language en --output_format srt --output_dir <folder> --device cuda --compute_type float16
```

If an existing `.en.srt` is present, Studio reuses it unless `既存.en.srtを退避して再生成` is checked. Japanese subtitle generation is planned as a separate translation runner that consumes the generated English SRT.

Subtitle generation settings are stored in `library.json` under `studio`. The output folder can be selected with `参照` and saved with `既定に設定`. `Pythonコマンド` chooses the launcher executable, and `実行引数` chooses the arguments before the video path, for example `-3.10 -m whisperx`. `cuda` selects GPU execution when the local WhisperX/PyTorch environment supports CUDA; `compute_type` controls precision such as `float16`, `float32`, or `int8`.

## Build Verification

```powershell
cd C:\work\CoffeeMovie
dotnet build src\CoffeeMovie.Verification\CoffeeMovie.Verification.csproj --no-restore
dotnet run --project src\CoffeeMovie.Verification\CoffeeMovie.Verification.csproj --no-build
```

## Android Reader Build

```powershell
cd C:\work\CoffeeMovie
dotnet build src\CoffeeMovie.Reader\CoffeeMovie.Reader.csproj -f net10.0-android
```

Reader builds prefer repo-local tools over machine-global settings. If present, `src\CoffeeMovie.Reader\Directory.Build.props` automatically uses:

```text
.tools\android-sdk\
.tools\jdk-17\jdk-17.0.19+10\
```

If CoffeeMovie does not have those tools restored, it can also reuse the sibling CoffeeBook checkout tools under:

```text
C:\work\CoffeeBook\COFFEEBOOK\.tools\
```

If a local JDK is not restored yet, pass `JavaSdkDirectory` explicitly for this machine, but do not bake that user-specific path into project files:

```powershell
dotnet build src\CoffeeMovie.Reader\CoffeeMovie.Reader.csproj -f net10.0-android -p:AndroidSdkDirectory=C:\work\CoffeeBook\COFFEEBOOK\.tools\android-sdk -p:JavaSdkDirectory=<local-jdk-path>
```

APK output is under:

```text
src\CoffeeMovie.Reader\bin\Debug\net10.0-android\
```

## Stable Android Identity

The Android package name is fixed:

```text
net.coffeewebjp.coffeemovie.reader
```

Google Cloud Android OAuth clients are tied to both package name and signing certificate SHA-1. To avoid re-registering Google Cloud settings when changing PCs, use one shared release keystore for CoffeeMovie Reader.

Create it once:

```powershell
cd C:\work\CoffeeMovie
.\scripts\android\New-CoffeeMovieReaderKeystore.ps1
```

This creates:

```text
.tools\android-signing\coffeemovie-reader-release.jks
.tools\android-signing\CoffeeMovie.Reader.Signing.props
```

Register the printed SHA-1 in Google Cloud for package `net.coffeewebjp.coffeemovie.reader`. On another development PC, restore those same two files to the same paths before building Release. Do not recreate them unless you intentionally want a new SHA-1.

Release build:

```powershell
dotnet build src\CoffeeMovie.Reader\CoffeeMovie.Reader.csproj -c Release -f net10.0-android
```

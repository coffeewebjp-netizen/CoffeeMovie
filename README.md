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

Studio currently supports local video import, subtitle import, subtitle scene inspection, simple preview playback, and `.coffeemovie.json` sidecar export.

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

If this machine does not have a global Android SDK/JDK configured, reuse the CoffeeBook tools:

```powershell
dotnet build src\CoffeeMovie.Reader\CoffeeMovie.Reader.csproj -f net10.0-android -p:AndroidSdkDirectory=C:\work\CoffeeBook\.tools\android-sdk -p:JavaSdkDirectory=C:\work\CoffeeBook\.tools\jdk-17\jdk-17.0.19+10
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

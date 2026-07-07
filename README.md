# CoffeeMovie

CoffeeMovie is a prototype video reader for Android, following the CoffeeBook split:

- `CoffeeMovie.Core`: movie, subtitle, scene, and cache models
- `CoffeeMovie.Storage`: local library JSON, subtitle parsing, WebVTT conversion, and cache index storage
- `CoffeeMovie.Studio`: Windows WPF studio for importing videos/subtitles and writing sidecars
- `CoffeeMovie.Reader`: .NET MAUI Android reader for Drive sync, local caching, subtitle playback, cue notes, and shadowing practice
- `CoffeeMovie.Verification`: small no-package verification app for parser and storage behavior

## What This Targets

- スマホで動画を開き、見たいシーンへ素早く移動する。
- SRT / WebVTT 字幕を取り込み、字幕キューをシーンジャンプ候補として使う。
- Google Drive 連携時は、sidecar fingerprint とローカルキャッシュを照合し、同じ動画や同じメタデータを毎回ダウンロードしない。

## Current Prototype

The Android reader supports Drive-first sync, sidecar thumbnail display on the movie shelf, local video cache import, WebView-backed playback, independent English/Japanese/memo overlays, cue-level notes/tags, and speech-recognition based shadowing practice. SRT subtitles are parsed into cue metadata and converted to WebVTT where a native track file is useful, while the current player renders its own subtitle overlay so English, Japanese, and memo lines can be positioned independently.

Windows Studio has grown into the preparation workspace for subtitle-learning workflows. It now supports drag/drop import, preview playback with a seek bar and subtitle overlay, full-size preview playback, paired English/Japanese subtitle display, cue click/double-click jumping, cue-level timing edits, tag/highlight metadata, WhisperX English subtitle generation, external Japanese subtitle translation, AI learning-note generation, thumbnail creation from the current preview frame, and Drive-ready package export.

Google Drive integration is implemented around `.coffeemovie` packages and lightweight `.coffeemovie.json` sidecars. Studio skips export when the current content fingerprint matches the existing sidecar. Reader downloads sidecars first, compares `contentFingerprint` with local `SourceContentFingerprint`, reports unchanged packages separately, and downloads large package bytes only when the user needs a missing or updated cache.

Reader and Studio keep the loaded library as an in-memory working set for normal navigation, selection, and filtering paths. Explicit refresh, import, sync, and export operations remain the boundaries where disk and Drive work are expected.

CoffeeLearning registration from Reader is documented in [docs/COFFEELEARNING_INTEGRATION.md](docs/COFFEELEARNING_INTEGRATION.md). It covers Bearer-token setup, subtitle-to-word field mapping, label transfer from movie and cue tags, registered cue state, and Android install checks.

## Run Windows Studio

```powershell
cd C:\work\CoffeeMovie
dotnet run --project src\CoffeeMovie.Studio\CoffeeMovie.Studio.csproj
```

Build output:

```text
src\CoffeeMovie.Studio\bin\Debug\net10.0-windows\CoffeeMovie.Studio.exe
```

Studio currently supports local video import, subtitle import, subtitle scene inspection, preview playback, full-size preview playback, cue-level subtitle timing edits, thumbnail creation, WhisperX English subtitle generation, external Japanese subtitle translation, AI learning-note generation, and Drive-ready `.coffeemovie` / `.coffeemovie.json` export.

The Studio `字幕生成` tab can call a local WhisperX Python environment to create `[video].en.srt` for the selected movie, then automatically import the generated English subtitle track. The default command is:

```text
py -3.10 -m whisperx <video> --model medium --language en --output_format srt --output_dir <folder> --device cuda --compute_type float16
```

If an existing `.en.srt` is present, Studio reuses it unless `既存.en.srtを退避して再生成` is checked.

The same tab can call an external AI translation command to create `[video].ja.srt` from an English SRT. CoffeeMovie does not embed a translation provider; it passes paths to a command configured in `翻訳コマンド` and `翻訳引数`. The default argument template is:

```text
codex-spark
exec --full-auto -C "{outputDir}" --add-dir "{inputDir}" --skip-git-repo-check "You are codex-spark for CoffeeMovie. Read the prompt file at {promptFile}, translate {input}, and write the Japanese SRT to {output}."
```

`codex-spark` is a Studio preset that resolves to the local Codex CLI and runs it as the translation agent. Supported placeholders are `{input}`, `{output}`, `{inputDir}`, `{outputDir}`, `{promptFile}`, `{prompt}`, `{source}`, `{target}`, `{movie}`, and `{title}`. The external AI-AGENT must write a valid Japanese SRT to `{output}`. Studio verifies that file and then imports it as a normal `.ja.srt` translation track. The `翻訳プロンプト` field starts from the anime subtitle skill routine and can be edited per workstation; `ベースに戻す` restores that default prompt.

Subtitle generation settings are stored in `library.json` under `studio`. The output folder can be selected with `参照` and saved with `既定に設定`. `Pythonコマンド` chooses the launcher executable, and `実行引数` chooses the arguments before the video path, for example `-3.10 -m whisperx`. `cuda` selects GPU execution when the local WhisperX/PyTorch environment supports CUDA; `compute_type` controls precision such as `float16`, `float32`, or `int8`.

## Build Verification

```powershell
cd C:\work\CoffeeMovie
dotnet build src\CoffeeMovie.Verification\CoffeeMovie.Verification.csproj --no-restore
dotnet run --project src\CoffeeMovie.Verification\CoffeeMovie.Verification.csproj --no-build
```

## Windows Studio Release

CoffeeMovie Studio can be packaged as a per-user MSI installer with the Windows icon generated from `icon.png`.

```powershell
cd C:\work\CoffeeMovie
.\scripts\windows\New-CoffeeMovieStudioMsi.ps1
```

The MSI output is written to:

```text
dist\CoffeeMovie.Studio-win-msi-<timestamp>.msi
```

The build can reuse the sibling CoffeeBook WiX 5 toolchain when present. Full release steps are documented in [docs/WINDOWS_RELEASE.md](docs/WINDOWS_RELEASE.md).

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

## Implementation Status

See [docs/IMPLEMENTATION_STATUS.md](docs/IMPLEMENTATION_STATUS.md) for the current PC Studio, Android Reader, Drive sync, subtitle-learning, thumbnail, and shadowing implementation summary.

## Stable Android Identity

Canonical Android signing and cross-PC update rules are documented in [docs/ANDROID_SIGNING.md](docs/ANDROID_SIGNING.md). That document takes precedence if there is any confusion.

The Android package name is fixed:

```text
net.coffeewebjp.coffeemovie.reader
```

Google Cloud Android OAuth clients are tied to both package name and signing certificate SHA-1. To avoid a different development PC producing an APK that Android treats as a different app identity, use one shared release keystore for CoffeeMovie Reader.

Current CoffeeMovie Reader installs use this SHA-1:

```text
B2:1B:F2:42:DC:4F:FC:E7:F9:A5:CE:85:F4:5D:0C:A3:81:ED:29:66
```

The matching signing files are local-only and must be backed up:

```text
.tools\android-signing\coffeemovie-reader-release.jks
.tools\android-signing\CoffeeMovie.Reader.Signing.props
```

On another development PC, restore those same two files to the same paths before building Release. Do not recreate them unless you intentionally want a new SHA-1, a new Google Cloud Android OAuth registration, and an Android app identity that cannot update the existing install.

Only create a new signing identity for a fresh app install:

```powershell
cd C:\work\CoffeeMovie
.\scripts\android\New-CoffeeMovieReaderKeystore.ps1
```

Release build:

```powershell
dotnet build src\CoffeeMovie.Reader\CoffeeMovie.Reader.csproj -c Release -f net10.0-android
```

Use the Release APK when overwriting an app installed from another PC. Debug APKs are signed by the local debug keystore and may fail with `INSTALL_FAILED_UPDATE_INCOMPATIBLE`.

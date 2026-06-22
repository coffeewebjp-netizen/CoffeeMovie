# Windows Release

This note captures the repeatable Windows Studio release flow for CoffeeMovie.

## Outputs

- App icon: `src\CoffeeMovie.Studio\Resources\AppIcon\CoffeeMovie.ico`
- MSI installer: `dist\CoffeeMovie.Studio-win-msi-<timestamp>.msi`
- Legacy self-extracting setup EXE, when needed: `artifacts\CoffeeMovie.Studio-Setup.exe`

The MSI installs CoffeeMovie Studio for the current Windows user under:

```text
%LOCALAPPDATA%\Programs\CoffeeMovie Studio
```

It also creates a Start Menu entry under `CoffeeMovie` and a desktop shortcut.

## Icon

Use the source PNG supplied for the app icon when refreshing the Windows icon. This workspace used a local root-level `icon.png`, but the source PNG itself does not have to be committed:

```powershell
cd C:\work\CoffeeMovie
.\scripts\windows\New-IcoFromPng.ps1 `
  -SourcePng .\icon.png `
  -OutputIco .\src\CoffeeMovie.Studio\Resources\AppIcon\CoffeeMovie.ico
```

The Studio project embeds that ICO through `ApplicationIcon`.

## MSI Build

CoffeeMovie can reuse the WiX 5 toolchain from CoffeeBook when it exists:

```text
C:\work\CoffeeBook\COFFEEBOOK\.tools\wix\wix.exe
C:\work\CoffeeBook\COFFEEBOOK\.wix\extensions\WixToolset.UI.wixext\5.0.2\wixext5\WixToolset.UI.wixext.dll
```

If those files are not available, install WiX locally in this repository:

```powershell
cd C:\work\CoffeeMovie
dotnet tool install --tool-path .tools\wix wix --version 5.*
.\.tools\wix\wix.exe extension add WixToolset.UI.wixext/5.0.2
```

Build the MSI:

```powershell
cd C:\work\CoffeeMovie
.\scripts\windows\New-CoffeeMovieStudioMsi.ps1
```

If the in-script build is blocked by an existing build process or a locked intermediate file, build the app to a temporary folder first and pass it to the MSI script:

```powershell
cd C:\work\CoffeeMovie
dotnet build src\CoffeeMovie.Studio\CoffeeMovie.Studio.csproj `
  -c Release `
  -o C:\tmp\CoffeeMovie.Studio-msi-app `
  -nr:false `
  -p:UseSharedCompilation=false

.\scripts\windows\New-CoffeeMovieStudioMsi.ps1 `
  -SourceRoot C:\tmp\CoffeeMovie.Studio-msi-app
```

## Validation

Before publishing a release commit, run at least:

```powershell
dotnet build src\CoffeeMovie.Studio\CoffeeMovie.Studio.csproj -nr:false -p:UseSharedCompilation=false
dotnet build src\CoffeeMovie.Verification\CoffeeMovie.Verification.csproj --no-restore
```

When Reader behavior changed, also run:

```powershell
dotnet build src\CoffeeMovie.Reader\CoffeeMovie.Reader.csproj -f net10.0-android
```

Do not commit generated MSI files, local sample videos, local subtitle samples, or one-off screenshots.

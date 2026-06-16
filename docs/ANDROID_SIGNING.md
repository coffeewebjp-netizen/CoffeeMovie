# Android Signing Runbook

This document is the source of truth for CoffeeMovie Reader Android signing.

## Goal

Keep the Android app identity stable across development PCs.

If the signing identity changes, Android may refuse to update the installed app. The only workaround can be uninstalling the app, which deletes app-local data such as Drive settings, local movie cache, reader library state, playback state, mobile-side notes, tags, and shadowing results.

## Canonical Identity

Package name:

```text
net.coffeewebjp.coffeemovie.reader
```

Canonical signing SHA-1:

```text
B2:1B:F2:42:DC:4F:FC:E7:F9:A5:CE:85:F4:5D:0C:A3:81:ED:29:66
```

This SHA-1 matches the existing Pixel install and the Google Cloud Android OAuth client. Do not replace it with a newly generated SHA-1.

## Files To Back Up And Share

These two files are required on every development PC that builds an APK intended to update the existing app:

```text
C:\work\CoffeeMovie\.tools\android-signing\coffeemovie-reader-release.jks
C:\work\CoffeeMovie\.tools\android-signing\CoffeeMovie.Reader.Signing.props
```

The `.tools` directory is intentionally outside Git, so these files must be backed up separately.

If the repository is placed somewhere other than `C:\work\CoffeeMovie`, update only the `AndroidSigningKeyStore` path in `CoffeeMovie.Reader.Signing.props` to the local absolute path of `coffeemovie-reader-release.jks`. Do not recreate the keystore.

## Do Not Do This

Do not run this script on another PC for the existing CoffeeMovie Reader app:

```powershell
.\scripts\android\New-CoffeeMovieReaderKeystore.ps1
```

That creates a new signing identity. A new identity means:

- different SHA-1
- Google Cloud Android OAuth client must be registered again
- existing Android install cannot be updated with the new APK
- uninstall/reinstall may be required
- app-local data can be lost

Only run the script if you intentionally want a brand-new app identity.

## Build Rule

Use Release APKs for cross-PC updates:

```powershell
dotnet build src\CoffeeMovie.Reader\CoffeeMovie.Reader.csproj -c Release -f net10.0-android
```

Do not use Debug APKs as the long-term cross-PC update channel. Debug signing can depend on machine-local debug keystores.

## Verification

Check the keystore SHA-1:

```powershell
.\.tools\jdk-17\current\bin\keytool.exe -list -v `
  -keystore .\.tools\android-signing\coffeemovie-reader-release.jks `
  -storepass android `
  -alias androiddebugkey
```

Check the generated APK SHA-1:

```powershell
.\.tools\jdk-17\current\bin\keytool.exe -printcert `
  -jarfile .\src\CoffeeMovie.Reader\bin\Release\net10.0-android\net.coffeewebjp.coffeemovie.reader-Signed.apk
```

Both must show:

```text
SHA1: B2:1B:F2:42:DC:4F:FC:E7:F9:A5:CE:85:F4:5D:0C:A3:81:ED:29:66
```

## Install Rule

Install over the existing app without uninstalling:

```powershell
adb install -r -d .\src\CoffeeMovie.Reader\bin\Release\net10.0-android\net.coffeewebjp.coffeemovie.reader-Signed.apk
```

If installation fails with `INSTALL_FAILED_UPDATE_INCOMPATIBLE`, stop and inspect the APK signature. Do not uninstall first unless data loss is acceptable.

When testing a replacement APK on an already installed device, keep `ApplicationVersion` moving forward so Android sees the APK as an update. `ApplicationVersion` becomes Android `versionCode`; increasing it allows `adb install -r` to preserve app-local cache and settings while replacing resources such as launcher icons and splash/startup images.

## Data Safety Note

Drive packages can restore shared movie/subtitle metadata and video bytes, but the Android app-local store also contains device-side state. Before uninstalling or migrating devices, use the Reader shelf `Backup` action to export a learning-state JSON that contains mobile tags, notes, playback state, and shadowing metrics. Drive-native automatic backup/restore is still a future step.

## Recovery Workflow

Before uninstalling, changing devices, or replacing a development PC signing setup:

1. Open CoffeeMovie Reader.
2. Tap `Backup`.
3. Choose `Export`.
4. Save or share the generated learning-state JSON somewhere outside the app.

After reinstalling or moving to a new device:

1. Install a Release APK signed with the canonical CoffeeMovie Reader keystore.
2. Configure Google Drive in Reader if needed.
3. Run Drive sync to restore the movie shells, subtitle metadata, thumbnails, and package references.
4. Download video packages when you need local playback.
5. Tap `Backup`, choose `Import`, and select the exported learning-state JSON.

The import matches movies by movie ID first, then `contentFingerprint`, then Drive package URI. It merges tags and cue states instead of replacing the full movie, so an older backup is less likely to erase newer local practice data.

param(
    [string]$Configuration = "Release",
    [string]$OutputRoot,
    [string]$SourceRoot
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot "..\..")).Path

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "dist"
}

$outputRootPath = [System.IO.Path]::GetFullPath($OutputRoot)
$repoRootPath = [System.IO.Path]::GetFullPath($repoRoot)

if (-not $outputRootPath.StartsWith($repoRootPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputRoot must be inside the repository: $outputRootPath"
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$packageName = "CoffeeMovie.Studio-win-msi-$timestamp"
$workRoot = Join-Path $outputRootPath $packageName
$buildRoot = Join-Path ([System.IO.Path]::GetTempPath()) $packageName
if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $sourceRoot = Join-Path $buildRoot "app"
    $shouldBuild = $true
}
else {
    $sourceRoot = [System.IO.Path]::GetFullPath($SourceRoot)
    $shouldBuild = $false
}
$appExePath = Join-Path $sourceRoot "CoffeeMovie.Studio.exe"
$appIconPath = Join-Path $repoRoot "src\CoffeeMovie.Studio\Resources\AppIcon\CoffeeMovie.ico"
$wxsPath = Join-Path $workRoot "CoffeeMovie.Studio.wxs"
$licensePath = Join-Path $workRoot "License.rtf"
$msiPath = Join-Path $outputRootPath "$packageName.msi"
$projectRelativePath = "src\CoffeeMovie.Studio\CoffeeMovie.Studio.csproj"
$localWixExe = Join-Path $repoRoot ".tools\wix\wix.exe"
$coffeeBookWixExe = "C:\work\CoffeeBook\COFFEEBOOK\.tools\wix\wix.exe"
$localWixUiExtension = Join-Path $repoRoot ".wix\extensions\WixToolset.UI.wixext\5.0.2\wixext5\WixToolset.UI.wixext.dll"
$coffeeBookWixUiExtension = "C:\work\CoffeeBook\COFFEEBOOK\.wix\extensions\WixToolset.UI.wixext\5.0.2\wixext5\WixToolset.UI.wixext.dll"

foreach ($pathToClean in @($workRoot, $(if ($shouldBuild) { $buildRoot } else { $null }))) {
    if ([string]::IsNullOrWhiteSpace($pathToClean)) {
        continue
    }

    if (Test-Path -LiteralPath $pathToClean) {
        Remove-Item -LiteralPath $pathToClean -Recurse -Force
    }
}

if ($shouldBuild) {
    New-Item -ItemType Directory -Force -Path $sourceRoot | Out-Null
}
New-Item -ItemType Directory -Force -Path $outputRootPath | Out-Null
New-Item -ItemType Directory -Force -Path $workRoot | Out-Null

if ($shouldBuild) {
    Push-Location $repoRoot
    try {
        dotnet build $projectRelativePath -c $Configuration -v:minimal -m:1 -nr:false -p:UseSharedCompilation=false -o $sourceRoot
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
}

if (-not (Test-Path -LiteralPath $appExePath)) {
    throw "Build output was not found: $appExePath"
}

if (-not (Test-Path -LiteralPath $appIconPath)) {
    throw "App icon was not found: $appIconPath"
}

$productVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($appExePath).FileVersion
if ([string]::IsNullOrWhiteSpace($productVersion)) {
    $productVersion = "0.1.1.0"
}

if (Test-Path -LiteralPath $localWixExe) {
    $wixExe = $localWixExe
}
elseif (Test-Path -LiteralPath $coffeeBookWixExe) {
    $wixExe = $coffeeBookWixExe
}
else {
    throw "WiX was not found. Install with: dotnet tool install --tool-path .tools\wix wix --version 5.*"
}

if (Test-Path -LiteralPath $localWixUiExtension) {
    $wixUiExtension = $localWixUiExtension
}
elseif (Test-Path -LiteralPath $coffeeBookWixUiExtension) {
    $wixUiExtension = $coffeeBookWixUiExtension
}
else {
    throw "WiX UI extension was not found. Run: .\.tools\wix\wix.exe extension add WixToolset.UI.wixext/5.0.2"
}

Set-Content -LiteralPath $licensePath -Encoding ASCII -Value "{\rtf1\ansi CoffeeMovie Studio\par\par This installer installs CoffeeMovie Studio for the current user.\par}"

$files = Get-ChildItem -LiteralPath $sourceRoot -File |
    Where-Object { $_.Extension -ne ".pdb" } |
    Sort-Object Name

$fileComponents = New-Object System.Collections.Generic.List[string]
$componentRefs = New-Object System.Collections.Generic.List[string]
$index = 1
$hasAppExecutable = $false

foreach ($file in $files) {
    $isAppExecutable = [string]::Equals($file.Name, "CoffeeMovie.Studio.exe", [StringComparison]::OrdinalIgnoreCase)
    $componentId = if ($isAppExecutable) { "AppExecutableComponent" } else { "AppFileComponent$index" }
    $fileId = if ($isAppExecutable) { "AppExecutableFile" } else { "AppFile$index" }
    if ($isAppExecutable) {
        $hasAppExecutable = $true
    }
    $source = $file.FullName.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace('"', "&quot;")
    $name = $file.Name.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace('"', "&quot;")
    $fileComponents.Add("        <Component Id=""$componentId"" Guid=""*"">`r`n          <File Id=""$fileId"" Source=""$source"" Name=""$name"" KeyPath=""yes"" />`r`n        </Component>")
    $componentRefs.Add("      <ComponentRef Id=""$componentId"" />")
    $index++
}

if (-not $hasAppExecutable) {
    throw "App executable was not included in the MSI source files: $appExePath"
}

$fileComponentXml = [string]::Join("`r`n", $fileComponents)
$componentRefXml = [string]::Join("`r`n", $componentRefs)

$licensePathXml = $licensePath.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace('"', "&quot;")
$appIconPathXml = $appIconPath.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace('"', "&quot;")

$wxs = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"
     xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui">
  <Package
      Name="CoffeeMovie Studio"
      Manufacturer="CoffeeMovie"
      Version="$productVersion"
      UpgradeCode="d06d6dfb-bfb7-4c4d-8212-c4f90aa068a6"
      Scope="perUser">
    <MajorUpgrade AllowSameVersionUpgrades="yes" DowngradeErrorMessage="A newer version of CoffeeMovie Studio is already installed." />
    <MediaTemplate EmbedCab="yes" />
    <ui:WixUI Id="WixUI_InstallDir" InstallDirectory="INSTALLFOLDER" />
    <WixVariable Id="WixUILicenseRtf" Value="$licensePathXml" />
    <Icon Id="CoffeeMovieStudioIcon" SourceFile="$appIconPathXml" />
    <Property Id="ARPPRODUCTICON" Value="CoffeeMovieStudioIcon" />

    <StandardDirectory Id="LocalAppDataFolder">
      <Directory Id="ProgramsLocalFolder" Name="Programs">
        <Directory Id="INSTALLFOLDER" Name="CoffeeMovie Studio">
$fileComponentXml
        </Directory>
      </Directory>
    </StandardDirectory>

    <StandardDirectory Id="ProgramMenuFolder">
      <Directory Id="ApplicationProgramsFolder" Name="CoffeeMovie" />
    </StandardDirectory>

    <StandardDirectory Id="DesktopFolder" />

    <ComponentGroup Id="ShortcutComponents">
      <Component Id="StartMenuShortcutComponent" Directory="ApplicationProgramsFolder" Guid="*">
        <Shortcut Id="StartMenuShortcut"
                  Name="CoffeeMovie Studio"
                  Target="[#AppExecutableFile]"
                  WorkingDirectory="INSTALLFOLDER"
                  Icon="CoffeeMovieStudioIcon"
                  IconIndex="0" />
        <RemoveFolder Id="RemoveApplicationProgramsFolder" On="uninstall" />
        <RegistryValue Root="HKCU"
                       Key="Software\CoffeeMovie\CoffeeMovie Studio"
                       Name="StartMenuShortcut"
                       Type="integer"
                       Value="1"
                       KeyPath="yes" />
      </Component>
      <Component Id="DesktopShortcutComponent" Directory="DesktopFolder" Guid="*">
        <Shortcut Id="DesktopShortcut"
                  Name="CoffeeMovie Studio"
                  Target="[#AppExecutableFile]"
                  WorkingDirectory="INSTALLFOLDER"
                  Icon="CoffeeMovieStudioIcon"
                  IconIndex="0" />
        <RegistryValue Root="HKCU"
                       Key="Software\CoffeeMovie\CoffeeMovie Studio"
                       Name="DesktopShortcut"
                       Type="integer"
                       Value="1"
                       KeyPath="yes" />
      </Component>
    </ComponentGroup>

    <Feature Id="MainFeature" Title="CoffeeMovie Studio" Level="1">
$componentRefXml
      <ComponentGroupRef Id="ShortcutComponents" />
    </Feature>
  </Package>
</Wix>
"@

Set-Content -LiteralPath $wxsPath -Value $wxs -Encoding UTF8

& $wixExe build $wxsPath -ext $wixUiExtension -o $msiPath
if ($LASTEXITCODE -ne 0) {
    throw "wix build failed with exit code $LASTEXITCODE"
}

Write-Host "MSI created:"
Write-Host $msiPath
Write-Host "Packaged from:"
Write-Host $sourceRoot

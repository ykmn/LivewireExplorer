<#
.SYNOPSIS
    Publishes a self-contained build of LivewireBrowser into ./release.
#>

$root = $PSScriptRoot
$releaseDir = Join-Path $root "release"
$project = Join-Path $root "src\LivewireBrowser.App\LivewireBrowser.App.csproj"

if (Test-Path $releaseDir) {
    Remove-Item $releaseDir -Recurse -Force
}

# AppVersion.cs is the single source of truth for the app's version (see CLAUDE.md
# "Версионность") — read it here instead of hardcoding the version a second time, so the
# exe's file properties (Explorer's "Подробно" tab) always match what the splash screen and
# Settings window show, with no risk of the two drifting apart.
$appVersionFile = Join-Path $root "src\LivewireBrowser.Core\AppVersion.cs"
$appVersionContent = Get-Content $appVersionFile -Raw

if ($appVersionContent -notmatch 'Version\s*=\s*"([\d.]+)"') {
    Write-Error "Could not find Version in $appVersionFile"
    exit 1
}
$version = $Matches[1]

if ($appVersionContent -notmatch 'ReleaseDate\s*=\s*"(\d{4})-\d{2}-\d{2}"') {
    Write-Error "Could not find ReleaseDate in $appVersionFile"
    exit 1
}
$releaseYear = $Matches[1]

# FileVersion/AssemblyVersion must be a strictly numeric Major.Minor.Build.Revision (4
# parts) — AppVersion.Version's "major.minor.debug" scheme (e.g. "0.01.039") only has 3 and
# isn't necessarily already numeric-safe, so derive a padded 4-part version from it rather
# than passing it through as-is. The original "0.01.039" string is kept verbatim for
# Version/InformationalVersion (ProductVersion), which Explorer shows as free text.
$versionParts = $version.Split('.') | ForEach-Object { [int]$_ }
while ($versionParts.Count -lt 4) { $versionParts += 0 }
$fileVersion = ($versionParts -join '.')

$copyright = "© $releaseYear Roman Ermakov"

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:Version=$version `
    -p:FileVersion=$fileVersion `
    -p:AssemblyVersion=$fileVersion `
    -p:InformationalVersion=$version `
    -p:Copyright="$copyright" `
    -p:Product="Livewire Browser" `
    -p:NeutralLanguage=ru-RU `
    -o $releaseDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "Published to $releaseDir"

<#
.SYNOPSIS
    End-to-end Windows build, package, zip, and (optional) GitHub release upload
    for VisUAL2-SU.

.DESCRIPTION
    This script automates the steps documented in docs/windows-build-release.md.
    It will:
        1. Pin the .NET Core 2.1 SDK via a temporary global.json
        2. Run the Fable build (node scripts/build.js)
        3. Repair any broken nested fs-extra installs in node_modules
           (these break electron-packager / mksnapshot / asar on Windows
           when files are stored as Dropbox "online-only" placeholders)
        4. Package the Electron app with --asar (node scripts/package.js win32)
        5. Re-zip the packaged folder using 7-Zip (cross-zip / Compress-Archive
           produce zips that WinRAR cannot extract)
        6. Optionally delete and re-upload the win32 asset on the matching
           GitHub release using the GitHub CLI

.PARAMETER Version
    The version string to embed in the zip name (e.g. "2.2.5"). If omitted,
    the value is read from package.json.

.PARAMETER Tag
    The GitHub release tag (e.g. "v2.2.5-SU"). If omitted, defaults to
    "v<Version>-SU".

.PARAMETER Repo
    The GitHub repo in <owner>/<name> form. Defaults to "rensutheart/Visual2".

.PARAMETER Upload
    If provided, deletes the existing win32 asset (if any) on the GitHub
    release and uploads the new zip. Requires `gh` to be installed and
    authenticated.

.PARAMETER SkipBuild
    If provided, skips the Fable build step and reuses an existing app/js
    build output. Useful when iterating on packaging only.

.EXAMPLE
    # Build, package, and zip only (no upload)
    pwsh -File scripts/build-and-release-win.ps1

.EXAMPLE
    # Full workflow: build, package, zip, and upload to v2.2.5-SU
    pwsh -File scripts/build-and-release-win.ps1 -Upload

.EXAMPLE
    # Force a different version/tag
    pwsh -File scripts/build-and-release-win.ps1 -Version 2.3.0 -Tag v2.3.0-SU -Upload
#>

[CmdletBinding()]
param(
    [string]$Version,
    [string]$Tag,
    [string]$Repo = "rensutheart/Visual2",
    [switch]$Upload,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

# Always operate from the repo root (one level up from this script).
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $RepoRoot

if (-not $Version) {
    $Version = (Get-Content "package.json" -Raw | ConvertFrom-Json).version
}
if (-not $Tag) {
    $Tag = "v$Version-SU"
}

$ZipName = "VisUAL2-SU-v$Version-win32-x64.zip"
$PackageDir = Join-Path "dist-win32" "VisUAL2-SU-win32-x64"
$SevenZip = "C:\Program Files\7-Zip\7z.exe"

Write-Host ""
Write-Host "=== VisUAL2-SU Windows Build & Release ===" -ForegroundColor Cyan
Write-Host "Repo root : $RepoRoot"
Write-Host "Version   : $Version"
Write-Host "Tag       : $Tag"
Write-Host "Repo      : $Repo"
Write-Host "Zip name  : $ZipName"
Write-Host "Upload    : $Upload"
Write-Host "Skip build: $SkipBuild"
Write-Host ""

# ---------------------------------------------------------------------------
# 1. Build with Fable (.NET Core 2.1 pinned via temporary global.json)
# ---------------------------------------------------------------------------
if (-not $SkipBuild) {
    Write-Host "[1/5] Building with Fable..." -ForegroundColor Yellow

    $dotnetDir = Join-Path $env:LOCALAPPDATA "dotnet"
    if (-not (Test-Path $dotnetDir)) {
        throw "Expected .NET Core SDK at $dotnetDir. Install .NET Core SDK 2.1.818 first."
    }
    $env:Path = "$dotnetDir;" + $env:Path

    Set-Content -Path "global.json" -Value '{"sdk":{"version":"2.1.818"}}' -Encoding UTF8

    # Capture pre-build timestamp/hash so we can detect a *silent* failure where
    # webpack couldn't overwrite app/js/renderer.js (e.g. Dropbox file lock) and
    # we'd otherwise package and ship the previously committed stale bundle.
    $bundle = "app/js/renderer.js"
    $beforeHash = if (Test-Path $bundle) { (Get-FileHash $bundle -Algorithm MD5).Hash } else { "" }
    $beforeTime = if (Test-Path $bundle) { (Get-Item $bundle).LastWriteTimeUtc } else { [datetime]::MinValue }
    $buildStart = Get-Date

    try {
        node scripts/build.js
        if ($LASTEXITCODE -ne 0) {
            throw "Fable build failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        # CRITICAL: leaving global.json behind breaks start.js and other scripts
        Remove-Item "global.json" -Force -ErrorAction SilentlyContinue
    }

    if (-not (Test-Path $bundle)) {
        throw "Fable build completed but $bundle does not exist."
    }
    $afterHash = (Get-FileHash $bundle -Algorithm MD5).Hash
    $afterTime = (Get-Item $bundle).LastWriteTimeUtc
    if ($afterHash -eq $beforeHash -and $afterTime -lt $buildStart.ToUniversalTime()) {
        throw ("Fable build appeared to succeed but $bundle was NOT overwritten " +
               "(hash unchanged and mtime $afterTime predates build start $($buildStart.ToUniversalTime())). " +
               "This usually means Dropbox or another process held a lock on the file. " +
               "Close the file in any editor / pause Dropbox sync and re-run.")
    }
    Write-Host "  Bundle refreshed: $bundle ($([math]::Round((Get-Item $bundle).Length/1KB)) KiB, hash $($afterHash.Substring(0,8)))" -ForegroundColor Green
} else {
    Write-Host "[1/5] Skipping Fable build (-SkipBuild)" -ForegroundColor DarkYellow
}

# ---------------------------------------------------------------------------
# 2. Repair broken nested fs-extra installs
#    Dropbox sometimes stores newly-installed npm files as "online-only"
#    placeholders, which causes node_modules\<pkg>\node_modules\fs-extra to
#    appear to exist but with empty subdirectories. We replace them from the
#    healthy node_modules-win32 mirror.
# ---------------------------------------------------------------------------
Write-Host "[2/5] Verifying nested fs-extra installs..." -ForegroundColor Yellow

$fsExtraTargets = @(
    "node_modules\electron-download\node_modules\fs-extra",
    "node_modules\electron-packager\node_modules\fs-extra",
    "node_modules\electron-packager\node_modules\electron-download\node_modules\fs-extra",
    "node_modules\mksnapshot\node_modules\fs-extra",
    "node_modules\flora-colossus\node_modules\fs-extra"
)

foreach ($target in $fsExtraTargets) {
    if (-not (Test-Path $target)) { continue }

    $libPath = Join-Path $target "lib"
    if (-not (Test-Path $libPath)) { continue }

    $emptyCount = (Get-ChildItem $libPath -Directory -ErrorAction SilentlyContinue |
        Where-Object { (Get-ChildItem $_.FullName -ErrorAction SilentlyContinue | Measure-Object).Count -eq 0 } |
        Measure-Object).Count

    if ($emptyCount -gt 0) {
        $mirror = $target -replace '^node_modules\\', 'node_modules-win32\'
        Write-Host "  Repairing $target ($emptyCount empty lib subdirs)" -ForegroundColor DarkYellow
        if (Test-Path $mirror) {
            Remove-Item -Recurse -Force $target -ErrorAction SilentlyContinue
            Copy-Item -Recurse -Force $mirror $target
        } else {
            # Fallback: flora-colossus has no -win32 mirror. Just delete it
            # so resolution falls through to the healthy parent fs-extra.
            Write-Host "    No mirror at $mirror -- deleting nested copy instead" -ForegroundColor DarkYellow
            Remove-Item -Recurse -Force $target
        }
    }
}

# ---------------------------------------------------------------------------
# 3. Package with electron-packager (--asar enabled by scripts/package.js)
# ---------------------------------------------------------------------------
Write-Host "[3/5] Packaging Electron app for win32-x64..." -ForegroundColor Yellow
Remove-Item -Recurse -Force "dist-win32" -ErrorAction SilentlyContinue

# electron-packager writes its banner to stderr, which combined with PowerShell's
# default behaviour treats native-stderr as a terminating error when piping.
# We also can't trust $LASTEXITCODE here: scripts/package.js runs cross-zip after
# packaging, and cross-zip exits non-zero on Windows even though packaging itself
# succeeded. We re-zip with 7-Zip in the next step, so cross-zip's failure is
# harmless. Validate success by checking that the packaged folder + app.asar exist.
$prevEAP = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    & node scripts/package.js win32 2>&1 | ForEach-Object { "$_" } | Out-Host
}
finally {
    $ErrorActionPreference = $prevEAP
}

if (-not (Test-Path $PackageDir)) {
    throw "Expected packaged app at $PackageDir but it was not produced."
}

$asarPath = Join-Path $PackageDir "resources\app.asar"
if (-not (Test-Path $asarPath)) {
    throw "Packaged app does not contain resources\app.asar -- ASAR was not enabled."
}
$fileCount = (Get-ChildItem $PackageDir -Recurse -File | Measure-Object).Count
Write-Host "  Packaged $fileCount files (with app.asar)" -ForegroundColor Green

# ---------------------------------------------------------------------------
# 4. Re-zip with 7-Zip (cross-zip output is incompatible with WinRAR)
# ---------------------------------------------------------------------------
Write-Host "[4/5] Creating zip with 7-Zip..." -ForegroundColor Yellow
if (-not (Test-Path $SevenZip)) {
    throw "7-Zip not found at $SevenZip. Install 7-Zip or update the script."
}

$ZipPath = Join-Path "dist-win32" $ZipName
Remove-Item $ZipPath -Force -ErrorAction SilentlyContinue

Push-Location "dist-win32"
try {
    & $SevenZip a -tzip $ZipName "VisUAL2-SU-win32-x64" | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "7-Zip failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

# Remove the cross-zip-produced zip if it exists (avoid uploading the wrong one).
$badZip = Join-Path "dist-win32" "VisUAL2-SU-win32-x64.zip"
if (Test-Path $badZip) {
    Remove-Item $badZip -Force
}

$zipSize = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
Write-Host "  Created $ZipPath ($zipSize MiB)" -ForegroundColor Green

# ---------------------------------------------------------------------------
# 5. Optional: upload to GitHub release
# ---------------------------------------------------------------------------
if ($Upload) {
    Write-Host "[5/5] Uploading to GitHub release $Tag..." -ForegroundColor Yellow

    $gh = Get-Command gh -ErrorAction SilentlyContinue
    if (-not $gh) {
        throw "GitHub CLI ('gh') not found in PATH. Install it from https://cli.github.com/."
    }

    # Delete existing asset if present (gh upload does not overwrite by default).
    $existing = & gh release view $Tag -R $Repo --json assets --jq ".assets[].name" 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "gh release view failed for $Tag in $Repo. Does the release exist?"
    }
    if ($existing -match [regex]::Escape($ZipName)) {
        Write-Host "  Deleting existing asset $ZipName..."
        & gh release delete-asset $Tag $ZipName -R $Repo --yes
    }

    & gh release upload $Tag $ZipPath -R $Repo
    if ($LASTEXITCODE -ne 0) {
        throw "gh release upload failed with exit code $LASTEXITCODE"
    }
    Write-Host "  Uploaded $ZipName to release $Tag" -ForegroundColor Green
} else {
    Write-Host "[5/5] Skipping upload (-Upload not specified)" -ForegroundColor DarkYellow
    Write-Host "      Manual upload command:"
    Write-Host "        gh release upload $Tag `"$ZipPath`" -R $Repo"
}

Write-Host ""
Write-Host "Done." -ForegroundColor Cyan

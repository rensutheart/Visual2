# Building and Releasing VisUAL2-SU for Windows

This document describes the end-to-end process for producing a Windows
distributable of **VisUAL2-SU** and publishing it as an asset on the
corresponding GitHub release. Mac and Linux builds are produced separately;
this guide covers Windows only.

The process is automated by [`scripts/build-and-release-win.ps1`](../scripts/build-and-release-win.ps1).
This document explains *what* the script does and *why* each step exists, so
that the build can be reproduced or debugged manually if anything goes wrong.

---

## TL;DR — automated build

From the repo root, in PowerShell. Use `pwsh` if it is installed; Windows
PowerShell also works and is the safest fallback on a stock Windows machine:

```powershell
# Build, package, and zip only:
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-and-release-win.ps1

# Build, package, zip, and upload to an existing GitHub release:
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-and-release-win.ps1 -Upload

# Build, package, zip, create the release if needed, and upload:
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-and-release-win.ps1 -Upload -CreateRelease

# Override version/tag explicitly:
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-and-release-win.ps1 -Version 2.2.5.1 -Tag v2.2.5.1-SU -Upload
```

The final artefact is `dist-win32\VisUAL2-SU-v<VERSION>-win32-x64.zip`.

---

## Prerequisites

The build host must have the following installed:

| Tool                   | Notes                                                                                                |
| ---------------------- | ---------------------------------------------------------------------------------------------------- |
| Node.js (>= 14)        | Used to run `scripts/build.js` and `scripts/package.js`. Tested with Node 20.                        |
| .NET Core SDK 2.1.818  | **Required** — Fable 2.x will not build with newer SDKs. Install to the per-user location at `%LOCALAPPDATA%\dotnet`. |
| Yarn                   | Originally used to install dependencies; node_modules folders are committed to the repo.             |
| 7-Zip                  | Installed at `C:\Program Files\7-Zip\7z.exe`. Used to produce a WinRAR-compatible zip.               |
| GitHub CLI (`gh`)      | Required only for the upload step. Must be authenticated (`gh auth status`).                          |

The repo ships `node_modules-win32/` (a known-good Windows install) alongside
`node_modules/`. The build script uses the former to repair files in the
latter when Dropbox sync mangles them (see [Pitfall 4](#4-broken-nested-fs-extra-installs-windows-only)).

---

## Release artefacts

Each release tagged `v<VERSION>-SU` on `rensutheart/Visual2` ships three zips:

| Asset                                     | Built by                            |
| ----------------------------------------- | ----------------------------------- |
| `VisUAL2-SU-v<VERSION>-linux-x64.zip`     | Linux build host (out of scope)     |
| `VisUAL2-SU-v<VERSION>-macOS-x64.zip`     | macOS build host (out of scope)     |
| `VisUAL2-SU-v<VERSION>-win32-x64.zip`     | This document / the script          |

---

## The five-step workflow

### Step 1 — Build with Fable

Compile the F# sources to JavaScript and bundle with Webpack:

```powershell
$env:Path = "$env:LOCALAPPDATA\dotnet;" + $env:Path
Set-Content -Path "global.json" -Value '{"sdk":{"version":"2.1.818"}}' -Encoding UTF8
node scripts/build.js
Remove-Item "global.json" -Force   # CRITICAL!
```

`scripts/build.js` runs the Fable daemon and then Webpack, producing
`app/js/renderer.js` and friends.

> **Pitfall — the temporary `global.json`:** Fable 2.x cannot build with
> .NET SDK 3+. Pinning via `global.json` is required. **Always delete
> `global.json` immediately after the build** — leaving it behind breaks
> `start.js` and several other scripts that expect a newer SDK.

### Step 2 — Verify and repair nested `fs-extra`

This is a Windows + Dropbox-only nuisance. See
[Pitfall 4](#4-broken-nested-fs-extra-installs-windows-only) for details.
The script scans for nested `fs-extra` installs whose `lib/<subdir>` folders
are empty and replaces them with the healthy copies stored under
`node_modules-win32/`.

### Step 3 — Package with electron-packager

```powershell
Remove-Item -Recurse -Force dist-win32 -ErrorAction SilentlyContinue
node scripts/package.js win32
```

`scripts/package.js` invokes `electron-packager` with the following important
flags (see [scripts/package.js](../scripts/package.js)):

* `--platform=win32 --arch=x64`
* `--prune` — drops devDependencies
* `--ignore=/dist*`, `--ignore=/src` — keeps the package small
* **`--asar`** — bundles the application into a single `app.asar` file

> **Why `--asar` matters:** without it, the packaged folder contains roughly
> 27,000 small files. Zipping it works, but extracting on a student PC
> (especially with antivirus on-access scanning) takes minutes and frequently
> appears to "hang". With `--asar` the same payload is just **117 files**
> (the Electron runtime plus a single `resources/app.asar`), and extraction
> takes seconds. All app filesystem operations work transparently against the
> asar archive via Electron's built-in support.

The packaged app lands in `dist-win32\VisUAL2-SU-win32-x64\`.

### Step 4 — Zip with 7-Zip (NOT cross-zip)

`scripts/package.js` ends by calling `cross-zip`, which on Windows shells out
to PowerShell's `Compress-Archive`. **The zips it produces cannot be opened
by WinRAR** (some students reported "unexpected end of archive" errors).
Re-zip the packaged folder with 7-Zip instead:

```powershell
cd dist-win32
& "C:\Program Files\7-Zip\7z.exe" a -tzip "VisUAL2-SU-v<VERSION>-win32-x64.zip" "VisUAL2-SU-win32-x64"
cd ..
```

Delete the `cross-zip` output (`VisUAL2-SU-win32-x64.zip` without the
version suffix) so that you cannot accidentally upload the broken one.

For the v2.2.5 build, the resulting zip is approximately **91 MiB**.

### Step 5 — Upload to GitHub release

If another platform has already created the release, upload to that existing
tag. `gh release upload` does not overwrite by default, so delete the existing
win32 asset first if it exists:

```powershell
gh release delete-asset v<VERSION>-SU "VisUAL2-SU-v<VERSION>-win32-x64.zip" -R rensutheart/Visual2 --yes
gh release upload       v<VERSION>-SU "dist-win32\VisUAL2-SU-v<VERSION>-win32-x64.zip" -R rensutheart/Visual2
```

If Windows is the first platform being published, create the release with the
asset in one step. Use the full commit SHA (or a branch name) for `--target`;
short SHAs can be rejected by the GitHub API as an invalid `target_commitish`.

```powershell
$sha = git rev-parse HEAD
gh release create v<VERSION>-SU "dist-win32\VisUAL2-SU-v<VERSION>-win32-x64.zip" -R rensutheart/Visual2 --target $sha --title "VisUAL2-SU v<VERSION>" --notes "VisUAL2-SU v<VERSION>. Platform assets are uploaded as they are built."
```

The automated script does the same thing when run with `-Upload -CreateRelease`.
Without `-CreateRelease`, it intentionally fails if the release does not exist,
which prevents accidental duplicate or mistagged releases.

Verify with:

```powershell
gh release view v<VERSION>-SU -R rensutheart/Visual2 --json assets --jq '.assets[] | {name, size}'
```

---

## Pitfalls and gotchas

### 1. `global.json` must be deleted after building

Several other dev scripts (notably `scripts/start.js`) probe `dotnet` and
will fail if a `global.json` pinning 2.1.818 is present and that SDK is not
the active one. **Always remove `global.json` after `node scripts/build.js`.**

### 2. Use 7-Zip, not `cross-zip` / `Compress-Archive`

Both `cross-zip` and PowerShell's `Compress-Archive` produce ZIP files that
WinRAR refuses to extract. 7-Zip produces a standard ZIP that opens cleanly
in Windows Explorer, 7-Zip, and WinRAR.

### 3. Always package with `--asar`

Already enabled in `scripts/package.js`. Do **not** add platform-specific
exceptions. The application uses only Electron-asar-transparent filesystem
APIs, so there is no functional reason to ship the app unpacked.

### 4. Broken nested `fs-extra` installs (Windows only)

When the repository lives under Dropbox (or OneDrive / Google Drive), some
files inside deep `node_modules` trees are stored as cloud "online-only"
placeholders. Node's `require()` then fails with errors like:

```
Cannot find module './copy-sync'
Cannot find module './util/assign'
```

…originating from `node_modules\<x>\node_modules\fs-extra\lib\index.js`.
The known-affected installs are:

* `node_modules\electron-download\node_modules\fs-extra` (v0.30.0)
* `node_modules\electron-packager\node_modules\fs-extra` (v5.0.0)
* `node_modules\electron-packager\node_modules\electron-download\node_modules\fs-extra` (v4.0.3)
* `node_modules\mksnapshot\node_modules\fs-extra` (v0.26.7)
* `node_modules\flora-colossus\node_modules\fs-extra` (v7.0.1) — has no
  matching mirror; just delete it so resolution falls back to the
  top-level `node_modules\fs-extra`.

The build script auto-detects this state (any `lib\<subdir>` that exists
but is empty) and repairs it by copying the corresponding folder from
`node_modules-win32`. If you ever wipe `node_modules-win32`, restore it
from git before running the build.

### 5. Electron 2.x API limitations

The app targets Electron 2.0.8 and therefore cannot use modern Web APIs in the
renderer. Do not casually update Electron as part of a package refresh unless
the code is migrated away from Electron 2-era APIs first. In particular:

* `navigator.clipboard` does **not** work — use `electron.remote.clipboard.writeText()` instead.
* All native APIs (dialog, menu, clipboard) must go through `electron.remote`.

This is unrelated to packaging but is the most common runtime surprise after
upgrading any UI code.

### 6. Stale `app/js/renderer.js` bundle (Dropbox file lock)

**Symptom:** `node scripts/build.js` reports success and exits 0, but the
output bundle `app/js/renderer.js` is **not actually overwritten**. You then
package and ship the previously-committed bundle — your latest F# changes are
silently absent from the release.

**Cause:** Because the repository lives inside a Dropbox-synced folder, Dropbox
(or a VS Code editor that has the file open) can hold a write lock on
`app/js/renderer.js` at the exact moment webpack tries to emit it. Webpack
catches the EBUSY error internally and continues without re-writing the file.
The build still prints all its module IDs and exits 0.

**Detection:** The string `rptheart` (your username) appears in webpack's module
IDs in a fresh bundle (≈1300+ occurrences). A stale bundle from before the
fork will have 0 `rptheart` and hundreds of `rensu` (the original author).

```powershell
"rptheart count: $((Select-String -Path app/js/renderer.js -Pattern 'rptheart' -SimpleMatch | Measure-Object).Count)"
"rensu count:    $((Select-String -Path app/js/renderer.js -Pattern 'rensu'    -SimpleMatch | Measure-Object).Count)"
```

**Fix:** Pause Dropbox sync, close the file in any open editor, and re-run
`node scripts/build.js`. The automated build script
(`scripts/build-and-release-win.ps1`) now hashes the bundle before/after and
throws an explicit error if it wasn't actually rewritten.

### 7. PowerShell + native-stderr terminating errors

`node scripts/package.js` writes its `Packaging app for platform...` banner to
**stderr**, not stdout. Combined with PowerShell's default
`$ErrorActionPreference = "Stop"` and a `2>&1` pipe, this is treated as a
terminating error and aborts the script before packaging can finish.

Additionally, `scripts/package.js` invokes `cross-zip` after packaging, which
**exits non-zero** on Windows even when packaging itself succeeded. So a naive
`if ($LASTEXITCODE -ne 0) { throw }` after `node scripts/package.js` will
incorrectly abort.

The build script handles both:
* Wraps the `node scripts/package.js win32` call in a temporary
  `$ErrorActionPreference = 'Continue'` block.
* Validates success by checking that
  `dist-win32\VisUAL2-SU-win32-x64\resources\app.asar` exists, **not** by
  checking `$LASTEXITCODE`.

The cross-zip output (`dist-win32\VisUAL2-SU-win32-x64.zip`) is harmless — we
delete it and replace it with a 7-Zip-built archive in step 4.

### 8. GitHub CLI auth and release targets

Run `gh auth status` before uploading. If more than one account is configured,
make sure the active token can write to `rensutheart/Visual2`.

When creating a release manually, prefer `git rev-parse HEAD` for the
`--target` value. A short SHA that works in local Git commands can still be
rejected by GitHub release creation.

---

## Manual smoke-test

After producing the zip, before uploading it is worth doing the following:

1. Extract `dist-win32\VisUAL2-SU-v<VERSION>-win32-x64.zip` to a temp folder.
2. Run `VisUAL2-SU.exe` from the extracted folder.
3. Open one of the bundled samples (`File ▸ Open ▸ samples\…`).
4. Set a breakpoint, click Run, and confirm the red breakpoint dot remains
   visible when execution stops on that line.
5. Insert a line above the breakpoint, click Run again, and confirm the
   breakpoint follows the instruction instead of disappearing.

---

## Version naming

* Git tag: `v<VERSION>-SU` (e.g. `v2.2.5-SU` or `v2.2.5.1-SU`)
* Win zip: `VisUAL2-SU-v<VERSION>-win32-x64.zip`
* Mac zip: `VisUAL2-SU-v<VERSION>-macOS-x64.zip`
* Linux zip: `VisUAL2-SU-v<VERSION>-linux-x64.zip`

The version string in `package.json` is the source of truth — bump it before
building, and the script will use it automatically. A fourth hotfix component
is acceptable when you need a small follow-up release without changing the
broader minor/patch numbering scheme.

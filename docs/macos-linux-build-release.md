# Building and Releasing VisUAL2-SU for macOS and Linux

This document describes the end-to-end process for producing macOS and Linux
distributables of **VisUAL2-SU** and publishing them as assets on the
corresponding GitHub release. The Windows build is covered separately in
[windows-build-release.md](windows-build-release.md).

The process is automated by
[`scripts/build-and-release-unix.sh`](../scripts/build-and-release-unix.sh).
This document explains *what* the script does and *why* each step exists, so
that the build can be reproduced or debugged manually if anything goes wrong.

---

## TL;DR — automated build

From the repo root, in any POSIX shell:

```bash
# Build, package, and zip only:
scripts/build-and-release-unix.sh --platform darwin
scripts/build-and-release-unix.sh --platform linux

# Build, package, zip, and upload to an existing GitHub release:
scripts/build-and-release-unix.sh --platform darwin --upload
scripts/build-and-release-unix.sh --platform linux  --upload

# Build, package, zip, create the release if needed, and upload:
scripts/build-and-release-unix.sh --platform darwin --upload --create-release

# Override version/tag explicitly:
scripts/build-and-release-unix.sh --platform darwin --version 2.2.5.1 --tag v2.2.5.1-SU --upload
```

Final artefacts:

* `dist-darwin/VisUAL2-SU-v<VERSION>-macOS-x64.zip`
* `dist-linux/VisUAL2-SU-v<VERSION>-linux-x64.zip`

Both Linux and macOS builds can be produced from a single macOS host (Electron
packager cross-compiles transparently because Electron itself ships
prebuilt binaries for every target platform).

Before building on the Mac, pull the latest `master` and confirm that
`package.json` already contains the release version you intend to ship. For
example, the Windows hotfix release used version `2.2.5.1` and tag
`v2.2.5.1-SU`.

---

## Prerequisites

The build host must have the following installed:

| Tool                  | Notes                                                                                                |
| --------------------- | ---------------------------------------------------------------------------------------------------- |
| Node.js (>= 14)       | Used to run `scripts/build.js` and `scripts/package.js`. Tested with Node 20.                        |
| .NET Core SDK 2.1.818 | **Required** — Fable 2.x will not build with newer SDKs. See "x64 SDK on Apple Silicon" below.       |
| Yarn                  | Originally used to install dependencies; `node_modules-darwin/` is committed to the repo.            |
| `zip` (BSD or Info-ZIP) | Standard on macOS and most Linux distributions. Used to produce the final versioned zip.           |
| GitHub CLI (`gh`)     | Required only for the upload step. Must be authenticated (`gh auth status`).                          |

The repo ships `node_modules-darwin/` (a known-good install) alongside
`node_modules/`. `scripts/platform-lib.js` automatically routes Fable, Webpack,
and electron-packager through this folder so the build is self-contained per
platform.

### x64 .NET SDK on Apple Silicon

Fable 2.x does not run natively on `arm64` Macs. Install the **x64** .NET Core
SDK 2.1.818 to a per-user directory (so it doesn't conflict with the system
.NET install) and let `scripts/platform-lib.js` shell into it via Rosetta:

```bash
mkdir -p ~/.dotnet-x64
curl -L https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
arch -x86_64 /tmp/dotnet-install.sh --channel 2.1 --version 2.1.818 \
    --install-dir ~/.dotnet-x64 --architecture x64
```

`scripts/platform-lib.js` (function `runDotnet`) detects `darwin/arm64` and
automatically wraps every `dotnet` invocation in `arch -x86_64 ~/.dotnet-x64/dotnet`.
On Intel Macs and Linux this branch is skipped and the system `dotnet` is used
directly — install 2.1.818 into the system PATH if that's your setup.

> **Temporary `global.json` only.** The build script now creates a temporary
> `global.json` to pin .NET Core SDK 2.1.818, then removes it before exiting.
> This keeps the Fable build deterministic without breaking `start.js` or other
> scripts later. `global.json` is in `.gitignore` and must not be committed.

---

## Release artefacts

Each release tagged `v<VERSION>-SU` on `rensutheart/Visual2` ships three zips:

| Asset                                     | Built by                            |
| ----------------------------------------- | ----------------------------------- |
| `VisUAL2-SU-v<VERSION>-linux-x64.zip`     | This document / the script          |
| `VisUAL2-SU-v<VERSION>-macOS-x64.zip`     | This document / the script          |
| `VisUAL2-SU-v<VERSION>-win32-x64.zip`     | Windows build host (see [windows-build-release.md](windows-build-release.md)) |

---

## The four-step workflow

### Step 1 — Build with Fable

Compile the F# sources to JavaScript and bundle with Webpack:

```bash
node scripts/build.js
```

`scripts/build.js` calls `runDotnet` from `scripts/platform-lib.js`, which on
Apple Silicon transparently runs `arch -x86_64 ~/.dotnet-x64/dotnet`. The
output is `app/js/renderer.js` plus `main.js` at the repo root.

### Step 2 — Package with electron-packager

```bash
rm -rf dist-darwin               # or dist-linux
node scripts/package.js darwin   # or linux
```

`scripts/package.js` invokes `electron-packager` with the following important
flags (see [scripts/package.js](../scripts/package.js)):

* `--platform=<darwin|linux> --arch=x64`
* `--prune` — drops devDependencies
* `--ignore=/dist*`, `--ignore=/src` — keeps the package small
* **`--asar`** — bundles the application into a single `app.asar` file

> **Why `--asar` matters:** without it, the packaged folder contains tens of
> thousands of small files. Extraction on a student machine (especially with
> antivirus on-access scanning) takes minutes and frequently appears to
> "hang". With `--asar` the same payload is just a couple of hundred files
> (the Electron runtime plus a single `resources/app.asar`), and extraction
> takes seconds. All app filesystem operations work transparently against the
> asar archive via Electron's built-in support.

The packaged app lands in:

* `dist-<host>/VisUAL2-SU-darwin-x64/VisUAL2-SU.app/`
* `dist-<host>/VisUAL2-SU-linux-x64/`

> **Quirk:** `scripts/platform-lib.js` derives the `dist-*` directory name
> from the **host** platform (`process.platform`), not the build target. So
> on a macOS host, both the darwin and the linux outputs land in
> `dist-darwin/` (alongside each other). On a Linux host, both land in
> `dist-linux/`. The build script handles this transparently and the final
> zip name is always `VisUAL2-SU-v<VERSION>-<macOS|linux>-x64.zip`.

For macOS, `scripts/package.js` also runs `electron-installer-dmg` to produce
`dist-darwin/visual2-su-osx.dmg`. We don't ship that as the primary download
(the .zip is the canonical asset on the GitHub release for parity with the
other platforms), but it's useful for students who prefer a drag-to-Applications
installer.

### Step 3 — Create the versioned zip

`scripts/package.js` ends by calling `cross-zip`, which produces an
**unversioned** archive (`VisUAL2-SU-<platform>-x64.zip`). The release
convention is to embed the version in the filename, so the script produces a
fresh archive with the canonical name:

```bash
cd dist-<platform>
rm -f *.zip
zip -ryq "VisUAL2-SU-v<VERSION>-<macOS|linux>-x64.zip" "VisUAL2-SU-<platform>-x64"
```

The `-y` flag is **critical on macOS**: it preserves symbolic links inside the
`.app` bundle (Electron's `Versions/A` -> `Current` style symlinks). Without
it, the extracted app refuses to launch with a bad-bundle error.

For the v2.2.5 build, sizes are approximately:

* macOS: 210–225 MiB
* Linux: 120–135 MiB

### Step 4 — Upload to GitHub release

If another platform has already created the release, upload to that existing
tag. `gh release upload` does not overwrite by default, so the script deletes
the existing per-platform asset first if it exists:

```bash
gh release delete-asset v<VERSION>-SU "VisUAL2-SU-v<VERSION>-<plat>-x64.zip" -R rensutheart/Visual2 --yes
gh release upload       v<VERSION>-SU "dist-<plat>/VisUAL2-SU-v<VERSION>-<plat>-x64.zip" -R rensutheart/Visual2
```

If macOS or Linux is the first platform being published, create the release
with the asset in one step. Use the full commit SHA (or a branch name) for
`--target`; short SHAs can be rejected by the GitHub API as an invalid
`target_commitish`.

```bash
sha="$(git rev-parse HEAD)"
gh release create "v<VERSION>-SU" "dist-<plat>/VisUAL2-SU-v<VERSION>-<plat>-x64.zip" -R rensutheart/Visual2 --target "$sha" --title "VisUAL2-SU v<VERSION>" --notes "VisUAL2-SU v<VERSION>. Platform assets are uploaded as they are built."
```

The automated script does the same thing when run with `--upload
--create-release`. Without `--create-release`, it intentionally fails if the
release does not exist, which prevents accidental duplicate or mistagged
releases.

Verify with:

```bash
gh release view v<VERSION>-SU -R rensutheart/Visual2 --json assets --jq '.assets[] | {name, size}'
```

---

## Pitfalls and gotchas

### 1. Always preserve symlinks in the macOS zip

macOS `.app` bundles contain framework symlinks
(`Frameworks/Electron Framework.framework/Versions/Current` etc.). Building
the zip without `-y` (e.g. with `Compress-Archive` on PowerShell, or with a
naive Python `zipfile.ZipFile.write` loop) flattens those symlinks into
duplicate copies of the framework binary. The extracted app then either
refuses to launch ("damaged and can't be opened") or balloons to twice its
intended size.

The script uses `zip -ryq`, which Info-ZIP and BSD `zip` both honour
correctly.

### 2. Always package with `--asar`

Already enabled in `scripts/package.js`. Do **not** add platform-specific
exceptions. The application uses only Electron-asar-transparent filesystem
APIs, so there is no functional reason to ship the app unpacked.

### 3. Stale `app/js/renderer.js` bundle (Dropbox file lock)

**Symptom:** `node scripts/build.js` reports success and exits 0, but the
output bundle `app/js/renderer.js` is **not actually overwritten**. You then
package and ship the previously committed bundle — your latest F# changes are
silently absent from the release.

**Cause:** Because the repository lives inside a Dropbox-synced folder, Dropbox
(or a VS Code editor that has the file open) can hold a write lock on
`app/js/renderer.js` at the exact moment webpack tries to emit it. Webpack
catches the EBUSY error internally and continues without re-writing the file.
The build still prints all its module IDs and exits 0.

**Detection:** the script hashes the bundle before and after the build and
fails loudly if the hash is unchanged *and* the mtime predates the build
start. If you hit this, close any open editor on the file, pause Dropbox
sync, and re-run.

### 4. Cross-zip exit code is unreliable

`scripts/package.js` ends by invoking `cross-zip`. On some shells / dist
folders this exits non-zero even when packaging itself succeeded. The script
runs it with `|| true` and validates success by checking that the packaged
tree and `app.asar` exist, **not** by checking the exit code. The unversioned
cross-zip output is then deleted and replaced with the versioned `zip -ryq`
archive.

### 5. The macOS build only runs on macOS

`electron-packager` can target Linux from a macOS host (cross-builds work
because Electron ships prebuilt platform binaries), but it cannot target
macOS from Linux because of the macOS-specific code-signing / Info.plist
work. **Run macOS builds on a Mac.** Linux builds work from either Mac or
Linux.

### 6. Dotnet SDK selection on Apple Silicon

If `dotnet --version` prints something other than `2.1.818` when invoked from
the Fable build, you've probably let the system `dotnet` (`/usr/local/share/dotnet`,
typically arm64) win the PATH lookup. The build script handles this by creating
a temporary `global.json` and by running through `scripts/platform-lib.js`
`runDotnet`, which forces `arch -x86_64 ~/.dotnet-x64/dotnet` on Apple Silicon.
Do not leave a hand-written `global.json` behind after debugging; it breaks
`start.js` and other scripts later.

If you call `dotnet` directly from a shell on an arm64 Mac, you must do so
under Rosetta:

```bash
arch -x86_64 ~/.dotnet-x64/dotnet --version    # 2.1.818
```

### 7. Electron 2.x API limitations

The app targets Electron 2.0.8 and therefore cannot use modern Web APIs in the
renderer. Do not casually update Electron as part of a package refresh unless
the code is migrated away from Electron 2-era APIs first. In particular:

* `navigator.clipboard` does **not** work — use `electron.remote.clipboard.writeText()` instead.
* All native APIs (dialog, menu, clipboard) must go through `electron.remote`.

This is unrelated to packaging but is the most common runtime surprise after
upgrading any UI code.

### 8. GitHub CLI auth and release targets

Run `gh auth status` before uploading. If more than one account is configured,
make sure the active token can write to `rensutheart/Visual2`.

When creating a release manually, prefer `git rev-parse HEAD` for the
`--target` value. A short SHA that works in local Git commands can still be
rejected by GitHub release creation.

---

## Manual smoke-test

After producing each zip, before uploading it is worth doing the following:

### macOS

1. Extract `dist-darwin/VisUAL2-SU-v<VERSION>-macOS-x64.zip` to a temp folder.
2. Open `VisUAL2-SU.app` (Right-click → Open the first time, to bypass Gatekeeper —
   the app is unsigned).
3. Open one of the bundled samples (`File ▸ Open ▸ samples/…`).
4. Set a breakpoint, click Run, and confirm the red breakpoint dot remains
   visible when execution stops on that line.
5. Insert a line above the breakpoint, click Run again, and confirm the
   breakpoint follows the instruction instead of disappearing.

### Linux

1. Extract `dist-linux/VisUAL2-SU-v<VERSION>-linux-x64.zip` on a Linux box.
2. Run `./VisUAL2-SU` from the extracted folder. (You may need to
   `chmod +x VisUAL2-SU` if your zip extractor stripped the bit.)
3. Same smoke test as macOS.

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

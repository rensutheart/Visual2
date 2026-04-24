#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# build-and-release-unix.sh
#
# End-to-end macOS / Linux build, package, zip, and (optional) GitHub release
# upload for VisUAL2-SU.
#
# This is the Unix counterpart of scripts/build-and-release-win.ps1 and
# implements the workflow documented in docs/macos-linux-build-release.md.
#
# Steps:
#   1. Run the Fable build (node scripts/build.js)
#      - On darwin/arm64, platform-lib.js automatically routes through
#        `arch -x86_64 ~/.dotnet-x64/dotnet` so Fable 2.x runs against the
#        x64 .NET Core 2.1.818 SDK.
#   2. Validate that app/js/renderer.js was actually rewritten (Dropbox can
#      hold a file lock and silently skip the overwrite).
#   3. Package the Electron app with --asar (node scripts/package.js <plat>)
#   4. Re-zip the packaged folder with `zip -r` so the asset name carries
#      the version, matching the Windows naming convention.
#       - macOS additionally produces a .dmg via electron-installer-dmg
#         (created by scripts/package.js); we ship the .zip as the primary
#         download for parity with Windows/Linux.
#   5. Optionally create or update the matching GitHub release asset using
#      the GitHub CLI (`gh`).
#
# Usage:
#   scripts/build-and-release-unix.sh --platform darwin                # build + zip only
#   scripts/build-and-release-unix.sh --platform linux                 # build + zip only
#   scripts/build-and-release-unix.sh --platform darwin --upload       # build + upload
#   scripts/build-and-release-unix.sh --platform linux  --skip-build   # repackage only
#   scripts/build-and-release-unix.sh --platform darwin \
#       --version 2.3.0 --tag v2.3.0-SU --upload
#   scripts/build-and-release-unix.sh --platform darwin \
#       --version 2.3.0 --tag v2.3.0-SU --upload --create-release
# ---------------------------------------------------------------------------

set -euo pipefail

# ---------------------------------------------------------------------------
# Defaults / argument parsing
# ---------------------------------------------------------------------------
PLATFORM=""
VERSION=""
TAG=""
REPO="rensutheart/Visual2"
UPLOAD=0
CREATE_RELEASE=0
SKIP_BUILD=0

usage() {
    cat <<EOF
Usage: $(basename "$0") --platform <darwin|linux> [options]

Options:
  --platform <darwin|linux>   Required. Target platform.
  --version <ver>             Override version string (default: read from package.json).
  --tag <tag>                 Override release tag (default: v<version>-SU).
  --repo <owner/name>         GitHub repo (default: $REPO).
  --upload                    Delete & re-upload the platform asset on the release.
  --create-release            With --upload, create the release if it does not exist.
  --skip-build                Skip the Fable build, reuse existing app/js/renderer.js.
  -h, --help                  Show this help.
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --platform) PLATFORM="$2"; shift 2 ;;
        --version)  VERSION="$2";  shift 2 ;;
        --tag)      TAG="$2";      shift 2 ;;
        --repo)     REPO="$2";     shift 2 ;;
        --upload)   UPLOAD=1;      shift ;;
        --create-release) CREATE_RELEASE=1; shift ;;
        --skip-build) SKIP_BUILD=1; shift ;;
        -h|--help)  usage; exit 0 ;;
        *) echo "Unknown argument: $1" >&2; usage; exit 2 ;;
    esac
done

if [[ "$PLATFORM" != "darwin" && "$PLATFORM" != "linux" ]]; then
    echo "ERROR: --platform must be 'darwin' or 'linux'" >&2
    usage
    exit 2
fi

# Always operate from the repo root (one level up from this script).
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT"

if [[ -z "$VERSION" ]]; then
    VERSION="$(node -p "require('./package.json').version")"
fi
if [[ -z "$TAG" ]]; then
    TAG="v${VERSION}-SU"
fi

# Asset naming mirrors the Windows convention: linux-x64 / macOS-x64.
case "$PLATFORM" in
    darwin) ASSET_PLATFORM_NAME="macOS" ;;
    linux)  ASSET_PLATFORM_NAME="linux" ;;
esac

# scripts/platform-lib.js sets distDirName = "dist-" + process.platform — i.e.
# it's the *host* platform, not the *target*. Cross-builds (linux from macOS)
# therefore also land in dist-darwin. Mirror that quirk here.
HOST_PLATFORM="$(uname -s | tr '[:upper:]' '[:lower:]')"
case "$HOST_PLATFORM" in
    darwin) HOST_DIST="dist-darwin" ;;
    linux)  HOST_DIST="dist-linux" ;;
    *) red "Unsupported host platform: $HOST_PLATFORM"; exit 1 ;;
esac

DIST_DIR="$HOST_DIST"
PACKAGE_DIR="${DIST_DIR}/VisUAL2-SU-${PLATFORM}-x64"
ZIP_NAME="VisUAL2-SU-v${VERSION}-${ASSET_PLATFORM_NAME}-x64.zip"
ZIP_PATH="${DIST_DIR}/${ZIP_NAME}"

cyan()   { printf '\033[36m%s\033[0m\n' "$*"; }
yellow() { printf '\033[33m%s\033[0m\n' "$*"; }
green()  { printf '\033[32m%s\033[0m\n' "$*"; }
red()    { printf '\033[31m%s\033[0m\n' "$*"; }

cyan ""
cyan "=== VisUAL2-SU ${ASSET_PLATFORM_NAME} Build & Release ==="
echo  "Repo root : $REPO_ROOT"
echo  "Platform  : $PLATFORM ($ASSET_PLATFORM_NAME)"
echo  "Version   : $VERSION"
echo  "Tag       : $TAG"
echo  "Repo      : $REPO"
echo  "Zip name  : $ZIP_NAME"
echo  "Upload    : $UPLOAD"
echo  "Create rel: $CREATE_RELEASE"
echo  "Skip build: $SKIP_BUILD"
echo ""

# ---------------------------------------------------------------------------
# 1. Build with Fable
# ---------------------------------------------------------------------------
BUNDLE="app/js/renderer.js"

if [[ "$SKIP_BUILD" -eq 0 ]]; then
    yellow "[1/4] Building with Fable..."

    # On darwin/arm64, scripts/platform-lib.js routes dotnet through
    # `arch -x86_64 ~/.dotnet-x64/dotnet` so Fable 2.x can run.
    if [[ "$(uname -s)" == "Darwin" && "$(uname -m)" == "arm64" ]]; then
        if [[ ! -x "$HOME/.dotnet-x64/dotnet" ]]; then
            red "ERROR: Expected .NET Core SDK at ~/.dotnet-x64/dotnet."
            red "       Install .NET Core SDK 2.1.818 (x64) and place it under ~/.dotnet-x64."
            exit 1
        fi
    elif ! command -v dotnet >/dev/null 2>&1; then
        red "ERROR: 'dotnet' not found in PATH. Install .NET Core SDK 2.1.818."
        exit 1
    fi

    # Pin SDK to 2.1.818 via global.json so any newer dotnet on PATH is ignored.
    # Removed in the trap below so other scripts (start.js, etc.) aren't broken.
    GLOBAL_JSON_CREATED=0
    if [[ ! -f global.json ]]; then
        echo '{"sdk":{"version":"2.1.818","allowPrerelease":true,"rollForward":"latestMajor"}}' > global.json
        GLOBAL_JSON_CREATED=1
    fi
    cleanup_global_json() {
        if [[ "$GLOBAL_JSON_CREATED" -eq 1 ]]; then
            rm -f global.json
        fi
    }
    trap cleanup_global_json EXIT

    # Preflight: Paket 5.176.4 (pinned by paket.dependencies) only understands
    # TFMs up to netcoreapp3.x. A test/*.fsproj targeting net6.0 / net10.0 will
    # cause Paket to abort restore for *every* project, which then surfaces as
    # a confusing Fable internal "Invalid value" / IteratedAdjustArityOfLambda
    # crash on the renderer. Fail fast with a clear message instead.
    BAD_TFM_FILES="$(grep -rlE '<TargetFramework>net[0-9]+\.[0-9]+</TargetFramework>' \
        --include='*.fsproj' src test 2>/dev/null || true)"
    if [[ -n "$BAD_TFM_FILES" ]]; then
        red "ERROR: Found .fsproj files with a modern TFM (net6.0+). Paket 5.176.4 cannot"
        red "       parse these and will fail restore for all projects. Set them back to"
        red "       'netcoreapp2.1' to match the rest of the build:"
        echo "$BAD_TFM_FILES" | sed 's/^/         /'
        exit 1
    fi

    # Release builds should not depend on stale Paket/MSBuild state. In the
    # Dropbox checkout this cache can survive file churn and leave the
    # dotnet-fable CLI tool unresolved until obj/bin and the restore cache are
    # cleared manually.
    find src test -type d \( -name obj -o -name bin \) -prune -exec rm -rf {} +
    rm -f paket-files/paket.restore.cached

    # Capture pre-build state so we can detect a silent "didn't actually
    # overwrite" failure (Dropbox / open editor file lock).
    BEFORE_HASH=""
    if [[ -f "$BUNDLE" ]]; then
        BEFORE_HASH="$(md5 -q "$BUNDLE" 2>/dev/null || md5sum "$BUNDLE" | awk '{print $1}')"
    fi
    BUILD_START_TS="$(date +%s)"

    if ! node scripts/build.js; then
        red "ERROR: Fable build failed."
        red "       If the failure is an 'Invalid value' / IteratedAdjustArityOfLambda"
        red "       crash from the F# compiler, it is usually caused by stale obj/ folders"
        red "       or a missing dotnet-fable CliTool registration. Recover with:"
        red "         find src test -type d \( -name obj -o -name bin \) -prune -exec rm -rf {} +"
        red "         arch -x86_64 ~/.dotnet-x64/dotnet restore src/Main/Main.fsproj  # macOS arm64"
        red "         dotnet restore src/Main/Main.fsproj                              # other"
        red "       then re-run this script."
        exit 1
    fi

    if [[ ! -f "$BUNDLE" ]]; then
        red "ERROR: Fable build completed but $BUNDLE does not exist."
        exit 1
    fi

    AFTER_HASH="$(md5 -q "$BUNDLE" 2>/dev/null || md5sum "$BUNDLE" | awk '{print $1}')"
    BUNDLE_MTIME="$(stat -f %m "$BUNDLE" 2>/dev/null || stat -c %Y "$BUNDLE")"

    if [[ "$AFTER_HASH" == "$BEFORE_HASH" && "$BUNDLE_MTIME" -lt "$BUILD_START_TS" ]]; then
        red "ERROR: Fable build appeared to succeed but $BUNDLE was NOT overwritten"
        red "       (hash unchanged and mtime predates build start)."
        red "       This usually means Dropbox or another process held a lock on the file."
        red "       Close the file in any editor / pause Dropbox sync and re-run."
        exit 1
    fi

    BUNDLE_KB=$(( $(wc -c < "$BUNDLE") / 1024 ))
    green "  Bundle refreshed: $BUNDLE (${BUNDLE_KB} KiB, hash ${AFTER_HASH:0:8})"
else
    yellow "[1/4] Skipping Fable build (--skip-build)"
fi

# ---------------------------------------------------------------------------
# 2. Package with electron-packager (--asar enabled by scripts/package.js)
# ---------------------------------------------------------------------------
yellow "[2/4] Packaging Electron app for ${PLATFORM}-x64..."

# Don't wipe the whole dist dir — on a macOS host it's shared between darwin
# and linux cross-builds, and may already contain a sibling-platform zip we
# want to keep. Just remove the target's package folder + cross-zip output.
rm -rf "$PACKAGE_DIR"
rm -f  "${DIST_DIR}/VisUAL2-SU-${PLATFORM}-x64.zip"

# scripts/package.js calls cross-zip after Linux packaging, which can be flaky.
# For macOS release zips, skip the optional DMG because it is not uploaded and
# its native macos-alias dependency can be the wrong architecture on Apple
# Silicon/Rosetta hosts. We verify the packaged tree instead of trusting the
# package script exit code.
if [[ "$PLATFORM" == "darwin" ]]; then
    VISUAL2_SKIP_DMG=1 node scripts/package.js "$PLATFORM"
else
    node scripts/package.js "$PLATFORM" || true
fi

# Validate the packaged folder + asar payload exist.
if [[ "$PLATFORM" == "darwin" ]]; then
    ASAR_PATH="${PACKAGE_DIR}/VisUAL2-SU.app/Contents/Resources/app.asar"
else
    ASAR_PATH="${PACKAGE_DIR}/resources/app.asar"
fi

if [[ ! -d "$PACKAGE_DIR" ]]; then
    red "ERROR: Expected packaged app at $PACKAGE_DIR but it was not produced."
    exit 1
fi
if [[ ! -f "$ASAR_PATH" ]]; then
    red "ERROR: Packaged app does not contain $ASAR_PATH -- ASAR was not enabled."
    exit 1
fi

FILE_COUNT="$(find "$PACKAGE_DIR" -type f | wc -l | tr -d ' ')"
green "  Packaged $FILE_COUNT files (with app.asar)"

# ---------------------------------------------------------------------------
# 3. Create the release zip with `zip -r` (preserves symlinks for .app bundles)
# ---------------------------------------------------------------------------
yellow "[3/4] Creating ${ZIP_NAME}..."

# Remove only the target's own previous versioned zip — don't nuke siblings
# (a darwin run on macOS may have left a linux zip in dist-darwin/).
rm -f "$ZIP_PATH"
rm -f "${DIST_DIR}/VisUAL2-SU-${PLATFORM}-x64.zip"

(
    cd "$DIST_DIR"
    # -y preserves symlinks (essential for macOS .app bundles).
    # -r recurses; -q is quiet.
    zip -ryq "$ZIP_NAME" "VisUAL2-SU-${PLATFORM}-x64"
)

if [[ ! -f "$ZIP_PATH" ]]; then
    red "ERROR: zip did not produce $ZIP_PATH"
    exit 1
fi

ZIP_MB=$(( $(wc -c < "$ZIP_PATH") / 1024 / 1024 ))
green "  Created $ZIP_PATH (${ZIP_MB} MiB)"

# ---------------------------------------------------------------------------
# 4. Optional: upload to GitHub release
# ---------------------------------------------------------------------------
if [[ "$UPLOAD" -eq 1 ]]; then
    yellow "[4/4] Uploading to GitHub release ${TAG}..."

    if ! command -v gh >/dev/null 2>&1; then
        red "ERROR: GitHub CLI ('gh') not found in PATH. Install from https://cli.github.com/."
        exit 1
    fi

    # Verify release exists. If this platform is the first one being published,
    # --create-release creates the release using the full HEAD SHA. Avoid short
    # SHAs here because the GitHub API can reject them as target_commitish.
    if ! gh release view "$TAG" -R "$REPO" --json tagName >/dev/null 2>&1; then
        if [[ "$CREATE_RELEASE" -eq 0 ]]; then
            red "ERROR: Release $TAG does not exist on $REPO."
            red "       If this is the first platform for the release, re-run with --create-release."
            exit 1
        fi

        TARGET_SHA="$(git rev-parse HEAD)"
        yellow "  Release $TAG does not exist; creating it at $TARGET_SHA..."
        gh release create "$TAG" "$ZIP_PATH" -R "$REPO" \
            --target "$TARGET_SHA" \
            --title "VisUAL2-SU v${VERSION}" \
            --notes "VisUAL2-SU v${VERSION}. Platform assets are uploaded as they are built."
        green "  Created release $TAG and uploaded $ZIP_NAME"
        cyan ""
        cyan "Done."
        exit 0
    fi

    EXISTING="$(gh release view "$TAG" -R "$REPO" --json assets --jq '.assets[].name')"
    if grep -Fxq "$ZIP_NAME" <<<"$EXISTING"; then
        echo "  Deleting existing asset $ZIP_NAME..."
        gh release delete-asset "$TAG" "$ZIP_NAME" -R "$REPO" --yes
    fi

    gh release upload "$TAG" "$ZIP_PATH" -R "$REPO"
    green "  Uploaded $ZIP_NAME to release $TAG"
else
    yellow "[4/4] Skipping upload (--upload not specified)"
    echo   "      Manual upload command:"
    echo   "        gh release upload $TAG \"$ZIP_PATH\" -R $REPO"
fi

cyan ""
cyan "Done."

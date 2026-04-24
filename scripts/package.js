"use strict";

const path = require("path");
const { distDirName, ensurePlatformDirs, localBin, platform, repoRoot, resolveFromRoot, run } = require("./platform-lib");

const targetPlatform = process.argv[2];

if (!targetPlatform) {
  console.error("Usage: node scripts/package.js <darwin|linux|win32>");
  process.exit(1);
}

ensurePlatformDirs();

const packagerArgs = [
  ".",
  "VisUAL2-SU",
  "--platform=" + targetPlatform,
  "--arch=x64",
  "--out=" + distDirName,
  "--prune",
  "--ignore=/dist",
  "--ignore=/dist-darwin",
  "--ignore=/dist-linux",
  "--ignore=/dist-win32",
  "--ignore=/src",
  "--overwrite",
  "--asar"
];

if (targetPlatform === "darwin") {
  packagerArgs.push("--icon=app/visual.ico.icns");
}

if (targetPlatform === "win32") {
  packagerArgs.push("--icon=app/visual.ico");
}

run(localBin("electron-packager"), packagerArgs, { cwd: repoRoot });

const packageDir = resolveFromRoot(path.join(distDirName, "VisUAL2-SU-" + targetPlatform + "-x64"));

if (targetPlatform === "darwin") {
  if (process.env.VISUAL2_SKIP_DMG === "1") {
    console.log("Skipping optional macOS DMG creation (VISUAL2_SKIP_DMG=1)");
    process.exit(0);
  }

  run(localBin("electron-installer-dmg"), [
    "--overwrite",
    "--icon=app/visual.ico.icns",
    packageDir,
    resolveFromRoot(path.join(distDirName, "visual2-su-osx"))
  ], { cwd: repoRoot });
  process.exit(0);
}

run(localBin("cross-zip"), [packageDir], { cwd: repoRoot });

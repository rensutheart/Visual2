"use strict";

const path = require("path");
const { platform, resolveFromRoot, run } = require("./platform-lib");

const targetPlatform = process.argv[2];

if (!targetPlatform) {
  console.error("Usage: node scripts/launch-packaged.js <platform>");
  process.exit(1);
}

const appDir = resolveFromRoot("dist-" + platform + "/VisUAL2-SU-" + targetPlatform + "-x64");

if (targetPlatform === "win32") {
  run(path.join(appDir, "VisUAL2-SU.exe"), [], { cwd: appDir });
} else if (targetPlatform === "linux") {
  run(path.join(appDir, "VisUAL2-SU"), [], { cwd: appDir });
} else {
  console.error("Packaged launch is only scripted for win32 and linux.");
  process.exit(1);
}

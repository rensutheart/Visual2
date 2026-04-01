"use strict";

const { ensurePlatformDirs, localBin, platform, repoRoot, run } = require("./platform-lib");

ensurePlatformDirs();

const extraArgs = process.argv.slice(2);

if (platform === "darwin" && process.arch === "arm64") {
  run("arch", ["-x86_64", localBin("electron"), ".", "-w"].concat(extraArgs), {
    cwd: repoRoot
  });
} else {
  run(localBin("electron"), [".", "-w"].concat(extraArgs), {
    cwd: repoRoot
  });
}

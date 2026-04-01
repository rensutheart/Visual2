"use strict";

const { ensurePlatformDirs, modulesDirName, repoRoot, run } = require("./platform-lib");

ensurePlatformDirs();

run("yarn", ["install", "--modules-folder", modulesDirName], {
  cwd: repoRoot
});

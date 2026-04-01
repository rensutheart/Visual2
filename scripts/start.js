"use strict";

const path = require("path");
const { ensurePlatformDirs, repoRoot, runDotnet } = require("./platform-lib");

ensurePlatformDirs();

runDotnet(["fable", "webpack", "-w", "--port", "free", "--", "-w", "--config", "webpack.config.js", "-w"], {
  cwd: path.join(repoRoot, "src/Main")
});

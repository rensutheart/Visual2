"use strict";

const path = require("path");
const { ensurePlatformDirs, repoRoot, runDotnet } = require("./platform-lib");

ensurePlatformDirs();

runDotnet(["fable", "webpack", "--port", "free", "--", "--config", "webpack.config.js"], {
  cwd: path.join(repoRoot, "src/Main")
});

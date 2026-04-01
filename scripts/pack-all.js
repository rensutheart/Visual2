"use strict";

const { run } = require("./platform-lib");

run("yarn", ["build"]);
run("yarn", ["pack-nobuild-win"]);
run("yarn", ["pack-nobuild-linux"]);
run("yarn", ["pack-nobuild-osx"]);

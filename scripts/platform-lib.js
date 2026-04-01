"use strict";

const fs = require("fs");
const path = require("path");
const childProcess = require("child_process");

const repoRoot = path.resolve(__dirname, "..");
const platform = process.platform;
const modulesDirName = "node_modules-" + platform;
const distDirName = "dist-" + platform;
const fableDirName = ".fable-" + platform;
const x64DotnetPath = path.join(process.env.HOME || "", ".dotnet-x64", "dotnet");

function resolveFromRoot(relativePath) {
  return path.join(repoRoot, relativePath);
}

function ensureDir(dirPath) {
  fs.mkdirSync(dirPath, { recursive: true });
}

function platformEnv(extraEnv) {
  const modulesDir = resolveFromRoot(modulesDirName);
  const binDir = path.join(modulesDir, ".bin");
  const env = Object.assign({}, process.env, extraEnv || {});
  const pathKey = process.platform === "win32" ? "Path" : "PATH";
  const currentPath = env[pathKey] || process.env[pathKey] || "";
  const delimiter = path.delimiter;

  env.NODE_PATH = modulesDir + (env.NODE_PATH ? delimiter + env.NODE_PATH : "");
  env[pathKey] = binDir + (currentPath ? delimiter + currentPath : "");
  env.VISUAL2_MODULES_DIR = modulesDir;
  env.VISUAL2_DIST_DIR = resolveFromRoot(distDirName);
  env.VISUAL2_FABLE_DIR = resolveFromRoot(fableDirName);

  if (platform === "darwin") {
    env.npm_config_arch = "x64";
    env.npm_config_target_arch = "x64";
    env.electron_config_arch = "x64";
  }

  return env;
}

function run(command, args, options) {
  const spawnOptions = Object.assign(
    {
      stdio: "inherit",
      cwd: repoRoot,
      env: platformEnv()
    },
    options || {}
  );

  const result = childProcess.spawnSync(command, args, spawnOptions);

  if (result.error) {
    throw result.error;
  }

  if (typeof result.status === "number" && result.status !== 0) {
    process.exit(result.status);
  }
}

function ensurePlatformDirs() {
  ensureDir(resolveFromRoot(modulesDirName));
  ensureDir(resolveFromRoot(distDirName));
  ensureDir(resolveFromRoot(fableDirName));
}

function localBin(binName) {
  const suffix = process.platform === "win32" ? ".cmd" : "";
  return path.join(resolveFromRoot(modulesDirName), ".bin", binName + suffix);
}

function runDotnet(args, options) {
  if (platform === "darwin" && process.arch === "arm64" && fs.existsSync(x64DotnetPath)) {
    run("arch", ["-x86_64", x64DotnetPath].concat(args), options);
    return;
  }

  run("dotnet", args, options);
}

module.exports = {
  distDirName,
  ensureDir,
  ensurePlatformDirs,
  fableDirName,
  localBin,
  modulesDirName,
  platform,
  platformEnv,
  repoRoot,
  resolveFromRoot,
  run,
  runDotnet
};

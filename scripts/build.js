"use strict";

const childProcess = require("child_process");
const net = require("net");
const path = require("path");
const {
  dotnetInvocation,
  ensurePlatformDirs,
  localBin,
  platformEnv,
  repoRoot
} = require("./platform-lib");

ensurePlatformDirs();

function getFreePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.on("error", reject);
    server.listen(0, "127.0.0.1", () => {
      const port = server.address().port;
      server.close(() => resolve(port));
    });
  });
}

function waitForDaemon(proc) {
  return new Promise((resolve, reject) => {
    let output = "";
    let resolved = false;

    function handleData(data) {
      const text = data.toString();
      output += text;
      process.stdout.write(text);
      if (!resolved && /daemon started on port/i.test(text)) {
        resolved = true;
        resolve();
      }
    }

    proc.stdout.on("data", handleData);
    proc.stderr.on("data", handleData);
    proc.on("error", reject);
    proc.on("exit", code => {
      if (!resolved) {
        reject(new Error("Fable daemon exited before startup completed (exit " + code + ").\n" + output));
      }
    });
  });
}

async function main() {
  const port = process.env.FABLE_SERVER_PORT || String(await getFreePort());
  const fableEnv = platformEnv({ FABLE_SERVER_PORT: port });

  const restore = dotnetInvocation(["restore", path.join(repoRoot, "src/Main/Main.fsproj")]);
  const restoreResult = childProcess.spawnSync(restore.command, restore.args, {
    cwd: repoRoot,
    env: fableEnv,
    stdio: "inherit"
  });
  if (restoreResult.error) {
    throw restoreResult.error;
  }
  if (typeof restoreResult.status === "number" && restoreResult.status !== 0) {
    process.exit(restoreResult.status);
  }

  const invocation = dotnetInvocation(["fable", "start", "--cwd", repoRoot, "--port", port]);

  const daemon = childProcess.spawn(invocation.command, invocation.args, {
    cwd: path.join(repoRoot, "src/Main"),
    env: fableEnv,
    detached: process.platform !== "win32",
    stdio: ["ignore", "pipe", "pipe"]
  });

  try {
    await waitForDaemon(daemon);

    const result = childProcess.spawnSync(localBin("webpack"), ["--config", "webpack.config.js"], {
      cwd: repoRoot,
      env: fableEnv,
      stdio: "inherit"
    });

    if (result.error) {
      throw result.error;
    }
    process.exitCode = typeof result.status === "number" ? result.status : 1;
  } finally {
    if (process.platform !== "win32") {
      try {
        process.kill(-daemon.pid);
      } catch (_) {
        daemon.kill();
      }
    } else {
      daemon.kill();
    }
  }
}

main().catch(err => {
  console.error(err.message || err);
  process.exit(1);
});

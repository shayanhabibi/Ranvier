import { chromium } from "@playwright/test";
import { spawn, execFileSync } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import os from "node:os";

const cwd = fileURLToPath(new URL("../", import.meta.url));
const out = fileURLToPath(new URL("../../../docs/research/branch-snapshots/", import.meta.url));
const server = spawn(process.execPath, ["node_modules/vite/bin/vite.js", "--config", "benchmarks/view-vite.config.mjs", "--host", "127.0.0.1", "--port", "5184", "--strictPort"], { cwd, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
let output = "", browser, chromiumVersion;
server.stdout.on("data", data => output += data);
server.stderr.on("data", data => output += data);
const branches = [], snapshots = [];
try {
  let ready = false;
  for (let i = 0; i < 100; i++) {
    if (server.exitCode !== null) throw Error(output);
    try { ready = (await fetch("http://127.0.0.1:5184/tests/branch-bench.html")).ok; } catch {}
    if (ready) break;
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  if (!ready) throw Error(output);
  for (let replicate = 0; replicate < 3; replicate++) {
    browser = await chromium.launch();
    chromiumVersion = browser.version();
    const page = await browser.newPage();
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto("http://127.0.0.1:5184/tests/branch-bench.html");
    try { await page.waitForFunction(() => window.branchBench && window.snapshotBench); }
    catch (error) { throw Error(`${error}\n${errors.join("\n")}\n${output}`); }
    const result = await page.evaluate(replicate => {
      if (!crossOriginIsolated) throw Error("High resolution timers required");
      window.snapshotBench.verify();
      const branches = [], snapshots = [];
      const modes = [{path: "branch-list", queued: false}, {path: "branch", queued: false}, {path: "branch-list", queued: true}, {path: "branch", queued: true}];
      for (const width of [1, 16]) for (const phase of ["construct", "mount", "update", "replace", "dispose"]) {
        const iterations = phase === "construct" ? 10000 : phase === "update" ? 1000 : phase === "replace" ? 128 : 32;
        for (const mode of modes) window.branchBench.sample(mode.path, mode.queued, width, phase, 3);
        for (let round = -3; round < 11; round++) for (let offset = 0; offset < modes.length; offset++) {
          const mode = modes[(offset + round + 4 + replicate) % modes.length];
          const ms = window.branchBench.sample(mode.path, mode.queued, width, phase, iterations);
          if (round >= 0) branches.push({replicate, round, width, phase, ...mode, ms});
        }
      }
      for (const width of [1, 16, 256, 1024]) for (const phase of ["adapt", "traverse", "lookup"]) {
        const iterations = phase === "lookup" ? Math.max(16, Math.floor(1048576 / width ** 2)) : Math.max(256, Math.floor(1048576 / width));
        const modes = ["list", "array", "array-copy"];
        for (let round = -3; round < 11; round++) for (let offset = 0; offset < modes.length; offset++) {
          const storage = modes[(offset + round + 3 + replicate) % modes.length];
          const run = window.snapshotBench.workload(storage, phase, width);
          const started = performance.now();
          for (let i = 0; i < iterations; i++) run();
          const ms = (performance.now() - started) / iterations;
          if (round >= 0) snapshots.push({replicate, round, width, phase, storage, ms});
        }
      }
      return {branches, snapshots};
    }, replicate);
    branches.push(...result.branches); snapshots.push(...result.snapshots);
    if (errors.length) throw Error(errors.join("\n"));
    await browser.close(); browser = undefined;
    console.log(`Completed Chromium replicate ${replicate + 1}`);
  }
  await mkdir(out, {recursive: true});
  await writeFile(`${out}browser-results.json`, JSON.stringify({ measuredAt: new Date().toISOString(), baseCommit: execFileSync("rtk", ["git", "rev-parse", "HEAD"], {cwd, encoding: "utf8"}).trim(), dirtyExperiment: true, node: process.version, chromium: chromiumVersion, cpu: os.cpus()[0].model, fable: "5.18.0", configuration: "Release", independentProcesses: 3, warmups: 3, rounds: 11, unit: "milliseconds per operation", queuedTiming: "manual flush included; structural reconciliation synchronous", branches, snapshots }, null, 2) + "\n");
} finally {
  if (browser) await browser.close();
  if (server.exitCode === null) { const stopped = new Promise(resolve => server.once("exit", resolve)); server.kill(); await stopped; }
}

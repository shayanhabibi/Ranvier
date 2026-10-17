import { chromium } from "@playwright/test";
import { spawn, execFileSync } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import os from "node:os";

const cwd = fileURLToPath(new URL("../", import.meta.url));
const out = fileURLToPath(new URL("../../../docs/research/binding-minimal/", import.meta.url));
const port = 5183;
const server = spawn(process.execPath, ["node_modules/vite/bin/vite.js", "--config", "benchmarks/view-vite.config.mjs", "--host", "127.0.0.1", "--port", String(port), "--strictPort"], { cwd, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
let output = "", browser;
server.stdout.on("data", data => output += data);
server.stderr.on("data", data => output += data);
const modes = [
  { path: "legacy", queued: false },
  { path: "direct", queued: false },
  { path: "helper", queued: false },
  { path: "direct", queued: true },
  { path: "helper", queued: true }
];
const phases = ["construct", "mount", "update", "coalesce", "dispose"];
const samples = [];
let version;
try {
  let ready = false;
  for (let i = 0; i < 100; i++) {
    if (server.exitCode !== null) throw Error(output);
    try { ready = (await fetch(`http://127.0.0.1:${port}/tests/binding-bench.html`)).ok; } catch {}
    if (ready) break;
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  if (!ready) throw Error(`Server startup failed: ${output}`);
  for (let replicate = 0; replicate < 3; replicate++) {
    browser = await chromium.launch();
    version = browser.version();
    const page = await browser.newPage();
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto(`http://127.0.0.1:${port}/tests/binding-bench.html`);
    await page.waitForFunction(() => window.bindingBench);
    samples.push(...await page.evaluate(({ modes, phases, replicate }) => {
      if (!crossOriginIsolated) throw Error("Benchmark requires isolated high-resolution timers");
      window.bindingBench.verify();
      const rows = [];
      for (const width of [1, 64]) for (const phase of phases) {
        const iterations = phase === "construct" ? 500 : phase === "update" ? (width === 1 ? 3000 : 500) : phase === "coalesce" ? 200 : 64;
        for (let round = -3; round < 11; round++) for (let i = 0; i < modes.length; i++) {
          const mode = modes[(i + round + 5 + replicate) % modes.length];
          const ms = window.bindingBench.sample(mode.path, mode.queued, width, phase, iterations);
          if (round >= 0) rows.push({ replicate, round, width, phase, iterations, ...mode, ms });
        }
      }
      return rows;
    }, { modes, phases, replicate }));
    if (errors.length) throw Error(errors.join("\n"));
    await browser.close(); browser = undefined;
    console.log(`Completed independent Chromium replicate ${replicate + 1}`);
  }
  const quantile = (values, fraction) => [...values].sort((a, b) => a - b)[Math.floor((values.length - 1) * fraction)];
  const summaries = [1, 64].flatMap(width => phases.flatMap(phase => modes.map(mode => {
    const rows = samples.filter(row => row.width === width && row.phase === phase && row.path === mode.path && row.queued === mode.queued);
    const values = rows.map(row => row.ms);
    return { width, phase, ...mode, medianMs: quantile(values, .5), p95Ms: quantile(values, .95), replicateMediansMs: [0, 1, 2].map(replicate => quantile(rows.filter(row => row.replicate === replicate).map(row => row.ms), .5)) };
  })));
  const result = { baselineCommit: "79b9e96", baselineSource: "benchmarks/BindingBaselineMounting.fs (module name changed for side-by-side compilation)", measuredAt: new Date().toISOString(), baseCommit: execFileSync("rtk", ["git", "rev-parse", "HEAD"], { cwd, encoding: "utf8" }).trim(), dirtyExperiment: true, node: process.version, chromium: version, cpu: os.cpus()[0].model, platform: `${os.platform()} ${os.release()}`, fable: "5.18.0", configuration: "Release", crossOriginIsolated: true, warmups: 3, rounds: 11, independentProcesses: 3, unit: "milliseconds per operation (batch elapsed / iterations); coalesce operation is 100 source writes", queuedTiming: "manual flush included; microtask latency excluded", samples, summaries };
  await mkdir(out, { recursive: true });
  await writeFile(`${out}browser-results.json`, JSON.stringify(result, null, 2) + "\n");
  console.log(`Saved ${samples.length} samples to ${out}browser-results.json`);
} finally {
  if (browser) await browser.close();
  if (server.exitCode === null) {
    const stopped = new Promise(resolve => server.once("exit", resolve));
    server.kill(); await stopped;
  }
}

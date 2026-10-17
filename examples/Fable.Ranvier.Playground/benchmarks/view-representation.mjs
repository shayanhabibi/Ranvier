import { chromium } from "@playwright/test";
import { spawn, execFileSync } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import os from "node:os";

const cwd = fileURLToPath(new URL("../", import.meta.url));
const out = fileURLToPath(new URL("../../../docs/research/view-representation/", import.meta.url));
const port = 5181;
const server = spawn(process.execPath, ["node_modules/vite/bin/vite.js", "--config", "benchmarks/view-vite.config.mjs", "--host", "127.0.0.1", "--port", String(port), "--strictPort"], { cwd, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
let output = "", browser;
server.stdout.on("data", data => output += data);
server.stderr.on("data", data => output += data);
const samples = [];
const representations = ["tree", "program"], sugars = ["direct", "functions", "builder"];
const workloads = ["static", "wide", "fragments", "reactive"];
const phases = ["construct", "mount", "dispose", "update"];
let version;
try {
  let ready = false;
  for (let i = 0; i < 100; i++) {
    if (server.exitCode !== null) throw Error(output);
    try { ready = (await fetch(`http://127.0.0.1:${port}/tests/representation.html`)).ok; } catch {}
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
    await page.goto(`http://127.0.0.1:${port}/tests/representation.html`);
    await page.waitForFunction(() => window.representationBench);
    const rows = await page.evaluate(({ representations, sugars, workloads, phases, replicate }) => {
      window.representationBench.verify();
      if (!crossOriginIsolated) throw Error("Benchmark requires isolated high-resolution timers");
      const rows = [];
      for (const workload of workloads) for (const phase of phases) {
        if (phase === "update" && workload !== "reactive") continue;
        const iterations = phase === "construct" ? 500 : phase === "update" ? 10000 : 64;
        const modes = representations.flatMap(representation => sugars.map(sugar => ({ representation, sugar })));
        for (let round = -3; round < 11; round++) for (let i = 0; i < modes.length; i++) {
          const mode = modes[(i + round + 6 + replicate) % modes.length];
          const ms = window.representationBench.sample(mode.representation, workload, mode.sugar, phase, iterations);
          if (round >= 0) rows.push({ replicate, round, workload, phase, iterations, ...mode, ms });
        }
      }
      return rows;
    }, { representations, sugars, workloads, phases, replicate });
    if (errors.length) throw Error(errors.join("\n"));
    samples.push(...rows);
    await browser.close(); browser = undefined;
    console.log(`Completed independent Chromium replicate ${replicate + 1}`);
  }
  const quantile = (values, fraction) => [...values].sort((a, b) => a - b)[Math.floor((values.length - 1) * fraction)];
  const summaries = workloads.flatMap(workload => phases.filter(phase => phase !== "update" || workload === "reactive").flatMap(phase => representations.flatMap(representation => sugars.map(sugar => {
    const rows = samples.filter(row => row.workload === workload && row.phase === phase && row.representation === representation && row.sugar === sugar);
    const values = rows.map(row => row.ms);
    return { workload, phase, representation, sugar, medianMs: quantile(values, .5), p95Ms: quantile(values, .95), minMs: Math.min(...values), maxMs: Math.max(...values), replicateMediansMs: [0, 1, 2].map(replicate => quantile(rows.filter(row => row.replicate === replicate).map(row => row.ms), .5)) };
  }))));
  const result = { measuredAt: new Date().toISOString(), commit: execFileSync("rtk", ["git", "rev-parse", "HEAD"], { cwd, encoding: "utf8" }).trim(), dirtyExperiment: true, node: process.version, chromium: version, cpu: os.cpus()[0].model, platform: `${os.platform()} ${os.release()}`, crossOriginIsolated: true, warmups: 3, rounds: 11, independentProcesses: 3, unit: "milliseconds per operation (batch elapsed / iterations)", samples, summaries };
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

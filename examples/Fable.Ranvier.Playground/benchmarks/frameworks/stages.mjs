import { chromium } from "@playwright/test";
import { spawn, execFileSync } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

const cwd = fileURLToPath(new URL("../../", import.meta.url));
const out = fileURLToPath(new URL("../../../../docs/research/framework-performance/investigation/", import.meta.url));
const port = 5188;
execFileSync("rtk", ["node", "node_modules/vite/bin/vite.js", "build", "--config", "benchmarks/frameworks/vite.config.mjs"], { cwd, stdio: "inherit", windowsHide: true, env: { ...process.env, RANVIER_PERF_PROFILE: "false", RANVIER_SHARED_BACKEND: "false" } });
const server = spawn(process.execPath, ["node_modules/vite/bin/vite.js", "preview", "--config", "benchmarks/frameworks/vite.config.mjs", "--host", "127.0.0.1", "--port", String(port), "--strictPort"], { cwd, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
let output = "", browser;
server.stdout.on("data", data => output += data);
server.stderr.on("data", data => output += data);
const samples = [], probes = [], comparisons = [];
try {
  const url = `http://127.0.0.1:${port}/benchmarks/frameworks/index.html`;
  let ready = false;
  for (let i = 0; i < 100; i++) {
    if (server.exitCode !== null) throw Error(output);
    try { ready = (await fetch(url)).ok; } catch {}
    if (ready) break;
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  if (!ready) throw Error(output);
  for (let replicate = 0; replicate < 3; replicate++) {
    browser = await chromium.launch();
    const page = await browser.newPage();
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto(url);
    await page.waitForFunction(() => window.frameworkPerf);
    comparisons.push(...await page.evaluate(replicate => {
      const rows = [];
      for (const width of [1, 16, 128, 512]) for (const phase of ["mount", "dispose"]) {
        for (let round = -3; round < 7; round++) for (let offset = 0; offset < 3; offset++) {
          const mode = ["ranvier", "ranvier-batched", "ranvier-unbatched"][(round + offset + replicate + 3) % 3];
          const ms = window.frameworkPerf.sample(mode, width, phase, width < 128 ? 128 : 16);
          if (round >= 0) rows.push({ replicate, width, phase, mode, round, ms });
        }
      }
      return rows;
    }, replicate));
    for (const kind of ["parts", "scopes", "core"]) {
      samples.push(...await page.evaluate(({ kind, replicate }) => {
        const rows = [], widths = kind === "scopes" ? [128, 512, 2048, 8192] : [1, 16, 128, 512];
        const host = document.createElement("main");
        document.body.append(host);
        try {
          for (const width of widths) for (let round = -3; round < 11; round++) {
            const totals = {};
            for (let i = 0; i < 16; i++) {
              const result = kind === "parts" ? window.frameworkPerf.parts(host, width) : window.frameworkPerf[kind](width);
              for (const [phase, ms] of result) totals[phase] = (totals[phase] || 0) + ms;
            }
            if (host.childNodes.length) throw Error("Stage benchmark retained DOM");
            if (round >= 0) for (const [phase, ms] of Object.entries(totals)) rows.push({ kind, phase, width, round, replicate, ms: ms / 16 });
          }
          return rows;
        } finally { host.remove(); }
      }, { kind, replicate }));
    }
    samples.push(...await page.evaluate(replicate => {
      const rows = [];
      for (const width of [16, 128, 512]) for (let round = -3; round < 11; round++) for (const batched of [false, true]) {
        let total = 0;
        for (let i = 0; i < 16; i++) total += window.frameworkPerf.coreOwners(width, batched);
        if (round >= 0) rows.push({ kind: "coreOwners", phase: batched ? "batched" : "unbatched", width, round, replicate, ms: total / 16 });
      }
      return rows;
    }, replicate));
    probes.push(...await page.evaluate(replicate => {
      const rows = [];
      for (const phase of ["mount", "dispose"]) for (let round = -3; round < 11; round++) {
        const modes = phase === "mount" ? ["baseline", "claims-disabled"] : ["baseline", "root-removal-only", "detach-first", "remove-disabled", "unclaim-disabled"];
        for (let i = 0; i < modes.length; i++) {
          const probe = modes[(round + 5 + i + replicate) % modes.length];
          const ms = window.frameworkPerf.probe(probe, 128, phase, 32);
          if (round >= 0) rows.push({ probe, phase, replicate, round, ms });
        }
      }
      return rows;
    }, replicate));
    if (errors.length) throw Error(errors.join("\n"));
    await browser.close(); browser = undefined;
    console.log(`Completed isolated stages and scaling ${replicate + 1}/3`);
  }
  await mkdir(out, { recursive: true });
  await writeFile(`${out}/stages.json`, JSON.stringify({ measuredAt: new Date().toISOString(), configuration: "production minified diagnostic bundle; no profiling during timings", samples, comparisons, probes }, null, 2) + "\n");
} finally {
  if (browser) await browser.close();
  if (server.exitCode === null) { const stopped = new Promise(resolve => server.once("exit", resolve)); server.kill(); await stopped; }
}

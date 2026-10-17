import { chromium } from "@playwright/test";
import { spawn, execFileSync } from "node:child_process";
import { mkdir, readFile, readdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { gzipSync } from "node:zlib";

const cwd = fileURLToPath(new URL("../../", import.meta.url));
// `--out=<name>` writes to investigation/<name>/ instead of the baseline directories.
const outName = process.argv.find(arg => arg.startsWith("--out="))?.slice(6);
const out = fileURLToPath(new URL(outName ? `../../../../docs/research/framework-performance/investigation/${outName}/` : process.argv.includes("--cleanup") ? "../../../../docs/research/framework-performance/investigation/cleanup-profile/" : "../../../../docs/research/framework-performance/investigation/", import.meta.url));
const port = 5187;
// `--modes=a,b` and `--phases=mount` narrow the profiled adapters; `--roots=<n>` sets the roots per profiled run and `--width=<n>` the rows per root.
// `--repeat=<n>` profiles n prepared runs in sequence; preparation and cleanup run inside the profile, outside `run`.
const listArg = (flag, fallback) => process.argv.find(arg => arg.startsWith(`--${flag}=`))?.slice(flag.length + 3).split(",") || fallback;
const modes = listArg("modes", ["ranvier", "solid", "ripple", "react"]), phases = listArg("phases", ["mount", "dispose"]);
const roots = Number(listArg("roots", ["256"])[0]), width = Number(listArg("width", ["128"])[0]), repeat = Number(listArg("repeat", ["1"])[0]);
if (process.argv.includes("--build")) execFileSync("rtk", ["node", "node_modules/vite/bin/vite.js", "build", "--config", "benchmarks/frameworks/vite.config.mjs"], { cwd, stdio: "inherit", windowsHide: true, env: { ...process.env, RANVIER_PERF_PROFILE: "true" } });
const server = spawn(process.execPath, ["node_modules/vite/bin/vite.js", "preview", "--config", "benchmarks/frameworks/vite.config.mjs", "--host", "127.0.0.1", "--port", String(port), "--strictPort"], { cwd, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
let output = "", browser;
server.stdout.on("data", data => output += data);
server.stderr.on("data", data => output += data);
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
  browser = await chromium.launch();
  const page = await browser.newPage();
  const errors = [];
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(url);
  await page.waitForFunction(() => window.frameworkPerf);
  const cdp = await page.context().newCDPSession(page);
  await cdp.send("Profiler.enable");
  await cdp.send("Profiler.setSamplingInterval", { interval: 100 });
  const results = [];
  await mkdir(out, { recursive: true });
  const assets = fileURLToPath(new URL("../../generated-framework-dist/assets/", import.meta.url));
  const maps = (await readdir(assets)).filter(file => file.endsWith(".js.map"));
  if (maps.length !== 1) throw Error(`Expected one benchmark source map, found ${maps.length}`);
  await writeFile(`${out}/profile-source-map.json.gz`, gzipSync(await readFile(`${assets}/${maps[0]}`)));
  for (const mode of modes) {
    await page.evaluate(({ mode, width }) => window.frameworkPerf.verify(mode, width), { mode, width });
    for (const phase of phases) {
      const timings = await page.evaluate(({ mode, phase, width }) => {
        for (let i = 0; i < 3; i++) window.frameworkPerf.sample(mode, width, phase, 32);
        return Array.from({ length: 7 }, () => window.frameworkPerf.sample(mode, width, phase, 32) * 1000);
      }, { mode, phase, width });
      await cdp.send("HeapProfiler.collectGarbage");
      await cdp.send("Profiler.start");
      const profiledRunMs = await page.evaluate(({ mode, phase, roots, width, repeat }) => {
        let ms = 0;
        for (let i = 0; i < repeat; i++) {
          const prepared = window.frameworkPerf.prepare(mode, width, phase, roots);
          const start = performance.now();
          prepared.run();
          ms += performance.now() - start;
          prepared.cleanup();
        }
        return ms;
      }, { mode, phase, roots, width, repeat });
      const { profile } = await cdp.send("Profiler.stop");
      await writeFile(`${out}/${mode}-${phase}.cpuprofile`, JSON.stringify(profile));
      const nodes = new Map(profile.nodes.map(node => [node.id, node]));
      const self = new Map();
      for (let i = 0; i < profile.samples.length; i++) {
        const node = nodes.get(profile.samples[i]);
        const key = `${node.callFrame.functionName || "(anonymous)"}:${node.callFrame.lineNumber + 1}`;
        self.set(key, (self.get(key) || 0) + profile.timeDeltas[i]);
      }
      const hotspots = [...self].sort((a, b) => b[1] - a[1]).slice(0, 25).map(([functionName, us]) => ({ functionName, ms: us / 1000 }));
      const operations = await page.evaluate(({ mode, phase, width }) => window.frameworkPerf.operations(mode, width, phase), { mode, phase, width });
      results.push({ mode, phase, width, timingsUs: timings, operations, profiledRoots: roots, profiledRuns: repeat, profiledRunMs, hotspots });
      console.log(JSON.stringify({ mode, phase, hotspots: hotspots.slice(0, 8) }));
    }
  }
  const probes = process.argv.some(arg => arg.startsWith("--modes=")) ? [] : await page.evaluate(() => {
    const rows = [];
    for (const phase of ["mount", "dispose"]) for (let round = -3; round < 11; round++) {
      const modes = phase === "mount" ? ["baseline", "claims-disabled"] : ["baseline", "root-removal-only", "detach-first", "remove-disabled", "unclaim-disabled"];
      for (let i = 0; i < modes.length; i++) {
        const probe = modes[(round + 5 + i) % modes.length];
        const ms = window.frameworkPerf.probe(probe, 128, phase, 32);
        if (round >= 0) rows.push({ probe, phase, round, ms });
      }
    }
    return rows;
  });
  if (errors.length) throw Error(errors.join("\n"));
  await writeFile(`${out}/profiles.json`, JSON.stringify({ measuredAt: new Date().toISOString(), chromium: browser.version(), sourceMap: maps[0], configuration: "production unminified diagnostic bundle; sampling enabled only for CPU profiles", modes, phases, results, probes }, null, 2) + "\n");
} finally {
  if (browser) await browser.close();
  if (server.exitCode === null) { const stopped = new Promise(resolve => server.once("exit", resolve)); server.kill(); await stopped; }
}

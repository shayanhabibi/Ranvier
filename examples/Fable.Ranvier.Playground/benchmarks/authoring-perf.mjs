import { chromium } from "@playwright/test";
import { spawn, execFileSync } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import os from "node:os";
import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";

const cwd = fileURLToPath(new URL("../", import.meta.url));
const out = fileURLToPath(new URL("../../../docs/research/authoring-performance/", import.meta.url));
const port = 5185;
if (process.argv.includes("--build")) {
  for (const [args, counters] of [
    [["dotnet", "build", "../../experiments/Ranvier.View.Foundation/AuthoringPerf/AuthoringPerf.fsproj", "-c", "Release", "-p:RanvierCounters=false", "--nologo"], "false"],
    [["dotnet", "fable", "tests/DomTests.fsproj", "--outDir", "generated-authoring-counters", "--configuration", "Release", "--noCache"], "true"],
    [["dotnet", "fable", "tests/DomTests.fsproj", "--outDir", "generated-tests", "--configuration", "Release", "--noCache"], "false"]
  ]) execFileSync("rtk", args, { cwd, env: { ...process.env, RanvierCounters: counters }, stdio: "inherit", windowsHide: true });
}
const server = spawn(process.execPath, ["node_modules/vite/bin/vite.js", "--config", "benchmarks/view-vite.config.mjs", "--host", "127.0.0.1", "--port", String(port), "--strictPort"], { cwd, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
let output = "", browser;
server.stdout.on("data", data => output += data);
server.stderr.on("data", data => output += data);
const samples = [], heaps = [], resources = [], modes = ["explicit", "overloaded"], widths = [1, 16];
let version;
try {
  let ready = false;
  for (let i = 0; i < 100; i++) {
    if (server.exitCode !== null) throw Error(output);
    try { ready = (await fetch(`http://127.0.0.1:${port}/tests/authoring-perf.html`)).ok; } catch {}
    if (ready) break;
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  if (!ready) throw Error(output);
  for (let replicate = 0; replicate < 3; replicate++) {
    browser = await chromium.launch();
    version = browser.version();
    const page = await browser.newPage();
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto(`http://127.0.0.1:${port}/tests/authoring-perf.html?counters`);
    await page.waitForFunction(() => window.authoringPerf);
    resources.push(...await page.evaluate(({ modes, widths, replicate }) => {
      const results = [];
      for (const width of widths) {
        const variants = modes.map(mode => {
          const writes = { attributes: 0, input: 0, disabled: 0, text: 0, listenersAdded: 0, listenersRemoved: 0 };
          const undo = [];
          const method = (proto, key, counter, filter = () => true) => {
            const original = proto[key];
            proto[key] = function (...args) { if (filter(args)) writes[counter]++; return original.apply(this, args); };
            undo.push(() => proto[key] = original);
          };
          const setter = (proto, key, counter) => {
            const original = Object.getOwnPropertyDescriptor(proto, key);
            Object.defineProperty(proto, key, { ...original, set(value) { writes[counter]++; original.set.call(this, value); } });
            undo.push(() => Object.defineProperty(proto, key, original));
          };
          method(Element.prototype, "setAttribute", "attributes");
          method(Element.prototype, "removeAttribute", "attributes");
          method(EventTarget.prototype, "addEventListener", "listenersAdded", args => args[0] === "click");
          method(EventTarget.prototype, "removeEventListener", "listenersRemoved", args => args[0] === "click");
          setter(HTMLInputElement.prototype, "value", "input");
          setter(HTMLButtonElement.prototype, "disabled", "disabled");
          setter(Node.prototype, "textContent", "text");
          try {
            const [initial, updated, constructed, mounted, changed, disposed] = window.authoringPerf.verify(mode, width);
            if (!mounted.length) throw Error("Counters build is missing");
            return { mode, width, replicate, initial, updated, constructed, mounted, changed, disposed, writes };
          } finally { for (const restore of undo.reverse()) restore(); }
        });
        const comparable = ({ mode, ...value }) => JSON.stringify(value);
        if (comparable(variants[0]) !== comparable(variants[1])) throw Error("Authoring changed resources or output");
        results.push(...variants);
      }
      return results;
    }, { modes, widths, replicate }));
    await page.goto(`http://127.0.0.1:${port}/tests/authoring-perf.html`);
    await page.waitForFunction(() => window.authoringPerf);
    samples.push(...await page.evaluate(({ modes, widths, replicate }) => {
      if (!crossOriginIsolated) throw Error("High resolution timer isolation missing");
      if (window.authoringPerf.verify("explicit", 1)[3].length) throw Error("Timing build has counters enabled");
      const rows = [];
      for (const width of widths) for (const phase of ["construct", "mount", "update", "dispose"]) {
        const iterations = phase === "construct" ? 500 : phase === "update" ? (width === 1 ? 1000 : 500) : 64;
        for (let round = -3; round < 11; round++) for (let offset = 0; offset < 2; offset++) {
          const mode = modes[(round + offset + 4 + replicate) % 2];
          const ms = window.authoringPerf.sample(mode, width, phase, iterations);
          if (round >= 0) rows.push({ mode, width, phase, replicate, round, iterations, ms });
        }
      }
      return rows;
    }, { modes, widths, replicate }));
    const cdp = await page.context().newCDPSession(page);
    for (const width of widths) for (const mode of modes) {
      const count = 512;
      await cdp.send("HeapProfiler.collectGarbage");
      const before = await cdp.send("Runtime.getHeapUsage");
      await page.evaluate(({ mode, width, count }) => { window.releaseDefinitions = window.authoringPerf.retain(mode, width, count); }, { mode, width, count });
      await cdp.send("HeapProfiler.collectGarbage");
      const retained = await cdp.send("Runtime.getHeapUsage");
      await page.evaluate(() => { window.releaseDefinitions(); delete window.releaseDefinitions; });
      await cdp.send("HeapProfiler.collectGarbage");
      const released = await cdp.send("Runtime.getHeapUsage");
      heaps.push({ replicate, width, mode, count, retainedBytesPerDefinition: (retained.usedSize - before.usedSize) / count, releasedDeltaBytes: released.usedSize - before.usedSize });
    }
    if (errors.length) throw Error(errors.join("\n"));
    await browser.close(); browser = undefined;
    console.log(`Completed Chromium process ${replicate + 1}/3`);
  }
  const result = { measuredAt: new Date().toISOString(), baseCommit: execFileSync("rtk", ["git", "rev-parse", "HEAD"], { cwd, encoding: "utf8" }).trim(), dirtyExperiment: true, node: process.version, chromium: version, cpu: os.cpus()[0].model, platform: `${os.platform()} ${os.release()}`, warmups: 3, rounds: 11, independentProcesses: 3, configuration: "Release", fable: "5.18.0", unit: "milliseconds per operation", allocationNote: "CDP measures GC-stabilized retained JS heap, not allocation rate; .NET construction uses exact per-thread allocated bytes.", resources, samples, heaps };
  result.dotnet = [0, 1, 2].map(replicate => {
    const dll = fileURLToPath(new URL("../../../experiments/Ranvier.View.Foundation/AuthoringPerf/bin/Release/net10.0/AuthoringPerf.dll", import.meta.url));
    const raw = execFileSync("rtk", ["proxy", "dotnet", dll], { cwd, encoding: "utf8", windowsHide: true, env: { ...process.env, DOTNET_TieredCompilation: "0" } });
    const data = JSON.parse(raw.trim().split("\n").find(line => line.startsWith("{")));
    return { replicate, tieredCompilation: false, ...data };
  });
  for (const process of result.dotnet) for (const row of process.samples.filter(row => row.mode === "explicit")) {
    const other = process.samples.find(other => other.mode === "overloaded" && other.width === row.width && other.round === row.round);
    if (row.bytes !== other.bytes) throw Error("Authoring changed .NET definition allocation");
    if (!process.noGcRegion || [...row.collections, ...other.collections].some(count => count !== 0)) throw Error(".NET timing has GC interference");
  }
  const median = values => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];
  result.summaries = widths.flatMap(width => ["construct", "mount", "update", "dispose"].map(phase => {
    const rows = samples.filter(row => row.width === width && row.phase === phase);
    const explicit = rows.filter(row => row.mode === "explicit");
    const overloaded = rows.filter(row => row.mode === "overloaded");
    return { width, phase, explicitUs: median(explicit.map(row => row.ms)) * 1000, overloadedUs: median(overloaded.map(row => row.ms)) * 1000, pairedMedianRatio: median(explicit.map(row => overloaded.find(other => other.replicate === row.replicate && other.round === row.round).ms / row.ms)), replicateRatios: [0, 1, 2].map(replicate => median(overloaded.filter(row => row.replicate === replicate).map(row => row.ms)) / median(explicit.filter(row => row.replicate === replicate).map(row => row.ms))) };
  }));
  await mkdir(out, { recursive: true });
  result.sourceHashes = Object.fromEntries(["benchmarks/authoring-perf.mjs", "tests/AuthoringPerf.fs", "../../experiments/Ranvier.View.Foundation/AuthoringPerfFixtures.fs", "../../experiments/Ranvier.View.Foundation/AuthoringPerf/Program.fs"].map(path => [path, createHash("sha256").update(readFileSync(new URL(path, new URL("../", import.meta.url)))).digest("hex")]));
  await writeFile(`${out}browser-results.json`, JSON.stringify(result, null, 2) + "\n");
} finally {
  if (browser) await browser.close();
  if (server.exitCode === null) { const stopped = new Promise(resolve => server.once("exit", resolve)); server.kill(); await stopped; }
}

import { chromium } from "@playwright/test";
import { spawn, execFileSync } from "node:child_process";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { createHash } from "node:crypto";
import { fileURLToPath } from "node:url";
import os from "node:os";

const cwd = fileURLToPath(new URL("../../", import.meta.url));
const outputDirectory = fileURLToPath(new URL("../../../../docs/research/framework-performance/", import.meta.url));
const verifyOnly = process.argv.includes("--verify-only");
const nativeDiagnostic = process.argv.includes("--native-diagnostic");
const directDiagnostic = process.argv.includes("--direct-diagnostic");
const cleanupComparison = process.argv.includes("--cleanup-comparison");
const bindingComparison = process.argv.includes("--binding-comparison");
const preparedComparison = process.argv.includes("--prepared-comparison");
const templateComparison = process.argv.includes("--template-comparison");
if ([nativeDiagnostic, directDiagnostic, cleanupComparison, bindingComparison, preparedComparison, templateComparison].filter(Boolean).length > 1) throw Error("Choose one diagnostic adapter set per run");
const modes = templateComparison ? ["ranvier", "ranvier-template", "solid", "ripple", "react"] : preparedComparison ? ["ranvier", "ranvier-prepared", "solid", "ripple", "react"] : directDiagnostic ? ["ranvier", "ranvier-direct", "solid", "ripple"] : nativeDiagnostic ? ["ranvier", "solid", "ripple", "native-create", "native-clone", "native-descendants"] : ["ranvier", "solid", "ripple", "react"], widths = [1, 16, 128];
const port = 5186;
if (process.argv.includes("--build")) {
  for (const args of [
    ["dotnet", "fable", "tests/DomTests.fsproj", "--outDir", "generated-tests", "--configuration", "Release", "--noCache"],
    ["dotnet", "fable", "benchmarks/ripple/RipplePerf.fsproj", "--outDir", "generated-ripple", "--configuration", "Release", "--noCache"],
    ["node", "node_modules/vite/bin/vite.js", "build", "--config", "benchmarks/frameworks/vite.config.mjs"]
  ]) execFileSync("rtk", args, { cwd, stdio: "inherit", windowsHide: true, env: { ...process.env, RanvierCounters: "false" } });
}
const server = spawn(process.execPath, ["node_modules/vite/bin/vite.js", "preview", "--config", "benchmarks/frameworks/vite.config.mjs", "--host", "127.0.0.1", "--port", String(port), "--strictPort"], { cwd, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
let serverOutput = "", browser;
server.stdout.on("data", data => serverOutput += data);
server.stderr.on("data", data => serverOutput += data);
const samples = [], correctness = [], heaps = [];
let chromiumVersion;
try {
  const url = `http://127.0.0.1:${port}/benchmarks/frameworks/index.html`;
  let ready = false;
  for (let attempt = 0; attempt < 100; attempt++) {
    if (server.exitCode !== null) throw Error(serverOutput);
    try { ready = (await fetch(url)).ok; } catch {}
    if (ready) break;
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  if (!ready) throw Error(serverOutput);
  for (let replicate = 0; replicate < (verifyOnly ? 1 : 3); replicate++) {
    browser = await chromium.launch();
    chromiumVersion = browser.version();
    const page = await browser.newPage();
    const errors = [];
    page.on("pageerror", error => errors.push(String(error)));
    await page.goto(url);
    await page.waitForFunction(() => window.frameworkPerf);
    correctness.push(...await page.evaluate(({ modes, widths, replicate }) => {
      if (!crossOriginIsolated) throw Error("High resolution timer isolation missing");
      return widths.flatMap(width => modes.map(mode => ({ mode, replicate, ...window.frameworkPerf.verify(mode, width) })));
    }, { modes, widths, replicate }));
    console.log(`Verified ${modes.length} adapters, Chromium process ${replicate + 1}`);
    if (!verifyOnly) {
      // Each independent process rotates variant order; warmups are discarded.
      for (const width of widths) for (const phase of ["mount", "update", "dispose"]) {
        samples.push(...await page.evaluate(({ modes, width, phase, replicate }) => {
          const iterations = phase === "update" ? (width === 128 ? 200 : 1000) : (width === 1 ? 256 : width === 16 ? 128 : 32);
          const rows = [];
          for (let round = -3; round < 11; round++) for (let offset = 0; offset < modes.length; offset++) {
            const mode = modes[(round + 4 + offset + replicate) % modes.length];
            const ms = window.frameworkPerf.sample(mode, width, phase, iterations);
            if (!Number.isFinite(ms) || ms <= 0) throw Error(`Invalid timing ${mode}/${phase}: ${ms}`);
            if (round >= 0) rows.push({ mode, width, phase, replicate, round, iterations, ms });
          }
          return rows;
        }, { modes, width, phase, replicate }));
        console.log(`Measured width ${width}, ${phase}, process ${replicate + 1}`);
      }
      const cdp = await page.context().newCDPSession(page);
      for (const width of widths) for (let offset = 0; offset < modes.length; offset++) {
        const mode = modes[(offset + replicate) % modes.length], count = 64;
        await cdp.send("HeapProfiler.collectGarbage");
        const before = await cdp.send("Runtime.getHeapUsage");
        await page.evaluate(({ mode, width, count }) => { window.releaseFrameworks = window.frameworkPerf.retain(mode, width, count); }, { mode, width, count });
        await cdp.send("HeapProfiler.collectGarbage");
        const retained = await cdp.send("Runtime.getHeapUsage");
        await page.evaluate(() => { window.releaseFrameworks(); delete window.releaseFrameworks; });
        await cdp.send("HeapProfiler.collectGarbage");
        const released = await cdp.send("Runtime.getHeapUsage");
        heaps.push({ mode, width, replicate, count, retainedBytesPerMount: (retained.usedSize - before.usedSize) / count, releasedDeltaBytes: released.usedSize - before.usedSize });
      }
    }
    if (errors.length) throw Error(errors.join("\n"));
    await browser.close(); browser = undefined;
  }
  if (!verifyOnly) {
    const median = values => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];
    const summaries = widths.flatMap(width => ["mount", "update", "dispose"].flatMap(phase => modes.map(mode => {
      const rows = samples.filter(row => row.width === width && row.phase === phase && row.mode === mode);
      const byProcessUs = [0, 1, 2].map(replicate => median(rows.filter(row => row.replicate === replicate).map(row => row.ms)) * 1000);
      return { width, phase, mode, medianUs: median(rows.map(row => row.ms)) * 1000, byProcessUs };
    })));
    const pkg = JSON.parse(await readFile(new URL("../../package.json", import.meta.url), "utf8"));
    const files = ["benchmarks/frameworks/run.mjs", "benchmarks/frameworks/page.mjs", "benchmarks/frameworks/contract.mjs", "benchmarks/frameworks/solid.jsx", "benchmarks/frameworks/react.mjs", "benchmarks/frameworks/vite.config.mjs", "benchmarks/ripple/RipplePerf.fs", "benchmarks/ripple/RipplePerf.fsproj", "tests/FrameworkPerf.fs", "../../experiments/Ranvier.View.Foundation/AuthoringPerfFixtures.fs", "package-lock.json"];
    files.push(...["Representation.fs", "Typed.fs", "Mounting.fs", "Components.fs", "Authoring.fs", "Lifetime.fs", "Binding.fs", "Scheduling.fs", "Generated/Html.fs"].map(path => `../../experiments/Ranvier.View.Foundation/${path}`));
    files.push("../../experiments/Ranvier.View.Foundation/Prepared.fs", "../../experiments/Ranvier.View.Foundation/Template.fs", "../../experiments/Ranvier.View.Foundation/TemplateDom.fs");
    if (directDiagnostic) files.push("tests/FrameworkDiagnostics.fs");
    if (nativeDiagnostic) files.push("benchmarks/frameworks/native.mjs");
    const sourceHashes = Object.fromEntries(await Promise.all(files.map(async path => [path, createHash("sha256").update(await readFile(new URL(path, new URL("../../", import.meta.url)))).digest("hex")])));
    const result = {
      measuredAt: new Date().toISOString(), baseCommit: execFileSync("rtk", ["git", "rev-parse", "HEAD"], { cwd, encoding: "utf8" }).trim(), dirtyExperiment: true,
      node: process.version, chromium: chromiumVersion, cpu: os.cpus()[0].model, platform: `${os.platform()} ${os.release()}`,
      production: true, minified: true, counters: false, fable: "5.18.0", warmups: 3, rounds: 11, independentProcesses: 3,
      packages: { solid: pkg.devDependencies["solid-js"], react: pkg.devDependencies.react, reactDom: pkg.devDependencies["react-dom"], rippleDom: "1.0.0-beta.9", ripple: "1.0.0-beta.8" },
      unit: "milliseconds per operation", notes: [
        "Mount includes state, derived value, component setup, view construction and committed DOM; host creation is outside timing.",
        "Each update alternates a/blocked and synchronously commits title, input.value and disabled for every row; React uses flushSync.",
        "Mount and disposal are batch averages across 256/128/32 independent roots at widths 1/16/128; paint/layout and asynchronous throughput are excluded.",
        "CDP measures GC-stabilized retained JavaScript heap per mounted root, including adapter/host references, not allocation rate or total DOM memory.",
        directDiagnostic ? "ranvier-direct bypasses Program views through the existing owned backend, retaining typed token lists; compiled Solid JSX includes template optimization." : nativeDiagnostic ? "Native adapters are handwritten DOM lower bounds without Ranvier lifecycle/ownership guarantees; Solid JSX includes template optimization." : "All implementations use their public authoring paths; compiled Solid JSX includes its template optimization.",
        "React uses controlled input with onChange and maintains an extra default value attribute; semantic live DOM state is checked, HTML byte equality is not required.",
        templateComparison ? "ranvier-template mounts the row as a compiled template component; the ranvier adapter uses authored components." : preparedComparison ? "ranvier-prepared clones a shared static skeleton and applies shared modifier recipes; both Ranvier adapters retain component/fragment ownership, descendant claims and anchors." : "The ordinary Ranvier adapter uses element construction; the opt-in prepared adapter is excluded from this run."
      ], sourceHashes, correctness, samples, summaries, heaps
    };
    await mkdir(nativeDiagnostic || directDiagnostic || cleanupComparison || bindingComparison || preparedComparison || templateComparison ? `${outputDirectory}/investigation` : outputDirectory, { recursive: true });
    await writeFile(`${outputDirectory}/${templateComparison ? "investigation/template-framework-results" : preparedComparison ? "investigation/prepared-framework-results" : bindingComparison ? "investigation/binding-framework-results" : cleanupComparison ? "investigation/cleanup-framework-results" : directDiagnostic ? "investigation/direct-results" : nativeDiagnostic ? "investigation/native-results" : "results"}.json`, JSON.stringify(result, null, 2) + "\n");
    console.log(JSON.stringify(summaries));
  }
} finally {
  if (browser) await browser.close();
  if (server.exitCode === null) { const stopped = new Promise(resolve => server.once("exit", resolve)); server.kill(); await stopped; }
}

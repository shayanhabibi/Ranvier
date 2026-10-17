import { chromium } from "@playwright/test";
import { spawn, execFileSync } from "node:child_process";
import { readFile, readdir, mkdir, writeFile } from "node:fs/promises";
import { createHash } from "node:crypto";
import { fileURLToPath } from "node:url";

const cwd = fileURLToPath(new URL("../../", import.meta.url));
const out = fileURLToPath(new URL("../../../../docs/research/framework-performance/investigation/", import.meta.url));
const variants = ["before", "after"], samples = [], checks = [], heaps = [], servers = [], bundleHashes = {};
const bindingComparison = process.argv.includes("--binding-comparison");
const median = values => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];
let browser, chromiumVersion;
try {
  const dirs = [bindingComparison ? "generated-framework-binding-before" : "generated-framework-before", "generated-framework-dist"];
  for (const dir of dirs) await readdir(`${cwd}/${dir}/assets`);
  execFileSync("rtk", ["node", "node_modules/vite/bin/vite.js", "build", "--config", "benchmarks/frameworks/vite.config.mjs"], { cwd, stdio: "inherit", windowsHide: true, env: { ...process.env, RANVIER_SHARED_BACKEND: "false", RANVIER_PERF_PROFILE: "false", RANVIER_PERF_OUTPUT: dirs[1] } });
  for (let index = 0; index < 2; index++) {
    const dir = dirs[index], port = 5192 + index;
    const assets = (await readdir(`${cwd}/${dir}/assets`)).filter(name => name.endsWith(".js"));
    bundleHashes[variants[index]] = Object.fromEntries(await Promise.all(assets.map(async name => [name, createHash("sha256").update(await readFile(`${cwd}/${dir}/assets/${name}`)).digest("hex")])));
    const server = spawn(process.execPath, ["node_modules/vite/bin/vite.js", "preview", "--config", "benchmarks/frameworks/vite.config.mjs", "--host", "127.0.0.1", "--port", String(port), "--strictPort"], { cwd, env: { ...process.env, RANVIER_PERF_OUTPUT: dir }, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
    servers.push(server);
    let output = "", ready = false;
    server.stdout.on("data", data => output += data);
    server.stderr.on("data", data => output += data);
    for (let attempt = 0; attempt < 100; attempt++) {
      if (server.exitCode !== null) throw Error(output);
      try { ready = (await fetch(`http://127.0.0.1:${port}/benchmarks/frameworks/index.html`)).ok; } catch {}
      if (ready) break;
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    if (!ready) throw Error(output);
  }
  for (let replicate = 0; replicate < 3; replicate++) {
    browser = await chromium.launch();
    chromiumVersion = browser.version();
    const pages = [], errors = [];
    for (let index = 0; index < 2; index++) {
      const page = await browser.newPage();
      page.on("pageerror", error => errors.push(String(error)));
      await page.goto(`http://127.0.0.1:${5192 + index}/benchmarks/frameworks/index.html`);
      await page.waitForFunction(() => window.frameworkPerf);
      checks.push(...await page.evaluate(({ variant, replicate }) => {
        if (!crossOriginIsolated) throw Error("Timer isolation missing");
        return [1, 16, 128].map(width => ({ variant, replicate, ...window.frameworkPerf.verify("ranvier", width) }));
      }, { variant: variants[index], replicate }));
      pages.push(page);
    }
    for (const width of [1, 16, 128]) for (const phase of ["mount", "update", "dispose"]) {
      const iterations = phase === "update" ? (width === 128 ? 200 : 1000) : (width === 1 ? 256 : width === 16 ? 128 : 32);
      for (let round = -3; round < 11; round++) for (let offset = 0; offset < 2; offset++) {
        const index = (round + offset + replicate + 4) % 2;
        const ms = await pages[index].evaluate(({ width, phase, iterations }) => window.frameworkPerf.sample("ranvier", width, phase, iterations), { width, phase, iterations });
        if (!Number.isFinite(ms) || ms <= 0) throw Error("Invalid timing");
        if (round >= 0) samples.push({ variant: variants[index], replicate, width, phase, round, iterations, ms });
      }
    }
    for (let index = 0; index < 2; index++) {
      const page = pages[index], cdp = await page.context().newCDPSession(page), count = 64;
      await cdp.send("HeapProfiler.collectGarbage");
      const before = await cdp.send("Runtime.getHeapUsage");
      await page.evaluate(count => { window.releaseCleanup = window.frameworkPerf.retain("ranvier", 128, count); }, count);
      await cdp.send("HeapProfiler.collectGarbage");
      const mounted = await cdp.send("Runtime.getHeapUsage");
      await page.evaluate(() => { window.releaseCleanup(); delete window.releaseCleanup; });
      await cdp.send("HeapProfiler.collectGarbage");
      const released = await cdp.send("Runtime.getHeapUsage");
      heaps.push({ variant: variants[index], replicate, width: 128, count, retainedBytesPerMount: (mounted.usedSize - before.usedSize) / count, releasedDeltaBytes: released.usedSize - before.usedSize });
    }
    if (errors.length) throw Error(errors.join("\n"));
    await browser.close(); browser = undefined;
    console.log(`Paired cleanup comparison ${replicate + 1}/3 passed`);
  }
  const summaries = [1, 16, 128].flatMap(width => ["mount", "update", "dispose"].flatMap(phase => variants.map(variant => {
    const rows = samples.filter(row => row.width === width && row.phase === phase && row.variant === variant);
    return { width, phase, variant, medianUs: median(rows.map(row => row.ms)) * 1000, byProcessUs: [0, 1, 2].map(replicate => median(rows.filter(row => row.replicate === replicate).map(row => row.ms)) * 1000) };
  })));
  await mkdir(out, { recursive: true });
  await writeFile(`${out}/${bindingComparison ? "binding" : "cleanup"}-results.json`, JSON.stringify({ measuredAt: new Date().toISOString(), chromium: chromiumVersion, baselineCommit: bindingComparison ? "a2f2c7f" : "ca3a5d5", configuration: "counter-free Release Fable, minified production; saved baseline bundle and rebuilt changed bundle, alternating variant order in three independent browser processes", warmups: 3, rounds: 11, independentProcesses: 3, unit: "milliseconds per operation", note: "Batch averages include GC, exclude host setup and paint/layout. Retained heap is JavaScript only. Baseline bundle must be prepared from the stated commit before compiling changed adapters.", bundleHashes, checks, samples, summaries, heaps }, null, 2) + "\n");
  console.log(JSON.stringify(summaries.filter(row => row.width === 128)));
} finally {
  if (browser) await browser.close();
  for (const server of servers) if (server.exitCode === null) { const stopped = new Promise(resolve => server.once("exit", resolve)); server.kill(); await stopped; }
}

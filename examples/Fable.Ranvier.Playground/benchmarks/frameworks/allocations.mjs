import { chromium } from "@playwright/test";
import { spawn, execFileSync } from "node:child_process";
import { readFile, readdir, mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { TraceMap, originalPositionFor } from "@jridgewell/trace-mapping";

const cwd = fileURLToPath(new URL("../../", import.meta.url));
// `--out=<name>` writes to investigation/<name>/ instead of the baseline directories.
const outName = process.argv.find(arg => arg.startsWith("--out="))?.slice(6);
const out = fileURLToPath(new URL(outName ? `../../../../docs/research/framework-performance/investigation/${outName}/` : process.argv.includes("--cleanup") ? "../../../../docs/research/framework-performance/investigation/cleanup-profile/" : "../../../../docs/research/framework-performance/investigation/", import.meta.url));
// `--modes=a,b`, `--width=<n>`, `--roots=<n>`, `--interval=<bytes>` and `--top=<n>` narrow the sample; the defaults sample 128 roots of 128 rows at 4096 bytes.
const listArg = (flag, fallback) => process.argv.find(arg => arg.startsWith(`--${flag}=`))?.slice(flag.length + 3).split(",") || fallback;
const modes = listArg("modes", ["ranvier", "ranvier-template", "ranvier-direct", "solid", "ripple", "native-descendants"]);
const width = Number(listArg("width", ["128"])[0]), roots = Number(listArg("roots", ["128"])[0]), interval = Number(listArg("interval", ["4096"])[0]), top = Number(listArg("top", ["30"])[0]);
const assets = fileURLToPath(new URL("../../generated-framework-dist/assets/", import.meta.url));
execFileSync("rtk", ["node", "node_modules/vite/bin/vite.js", "build", "--config", "benchmarks/frameworks/vite.config.mjs"], { cwd, stdio: "inherit", windowsHide: true, env: { ...process.env, RANVIER_PERF_PROFILE: "true", RANVIER_SHARED_BACKEND: "false" } });
const mapFile = (await readdir(assets)).find(name => name.endsWith(".js.map"));
const sourceMap = new TraceMap(JSON.parse(await readFile(`${assets}/${mapFile}`, "utf8")));
const server = spawn(process.execPath, ["node_modules/vite/bin/vite.js", "preview", "--config", "benchmarks/frameworks/vite.config.mjs", "--host", "127.0.0.1", "--port", "5191", "--strictPort"], { cwd, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
let output = "", browser;
server.stdout.on("data", data => output += data);
server.stderr.on("data", data => output += data);
try {
  const url = "http://127.0.0.1:5191/benchmarks/frameworks/index.html";
  let ready = false;
  for (let i = 0; i < 100; i++) {
    if (server.exitCode !== null) throw Error(output);
    try { ready = (await fetch(url)).ok; } catch {}
    if (ready) break;
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  if (!ready) throw Error(output);
  browser = await chromium.launch();
  const results = [];
  for (const mode of modes) {
    const page = await browser.newPage();
    await page.goto(url);
    await page.waitForFunction(() => window.frameworkPerf);
    await page.evaluate(({ mode, width, roots }) => {
      window.frameworkPerf.verify(mode, width);
      for (let i = 0; i < 3; i++) window.frameworkPerf.sample(mode, width, "mount", 32);
      window.sampleAllocations = window.frameworkPerf.prepare(mode, width, "mount", roots);
    }, { mode, width, roots });
    const cdp = await page.context().newCDPSession(page);
    await cdp.send("HeapProfiler.collectGarbage");
    await cdp.send("HeapProfiler.startSampling", { samplingInterval: interval, includeObjectsCollectedByMajorGC: true, includeObjectsCollectedByMinorGC: true });
    await page.evaluate(() => window.sampleAllocations.run());
    const { profile } = await cdp.send("HeapProfiler.stopSampling");
    const functions = new Map(), sources = new Map();
    const walk = node => {
      const frame = node.callFrame;
      const mapped = frame.url.includes("/assets/") ? originalPositionFor(sourceMap, { line: frame.lineNumber + 1, column: frame.columnNumber }) : null;
      const source = mapped?.source || frame.url || "(runtime)";
      const name = `${source}:${mapped?.line || frame.lineNumber + 1} ${frame.functionName || "(anonymous)"}`;
      functions.set(name, (functions.get(name) || 0) + node.selfSize);
      sources.set(source, (sources.get(source) || 0) + node.selfSize);
      for (const child of node.children) walk(child);
    };
    walk(profile.head);
    const ranked = data => [...data].sort((a, b) => b[1] - a[1]).slice(0, top).map(([name, bytes]) => ({ name, estimatedBytesPerMount: bytes / roots }));
    const result = { mode, width, roots, estimatedBytesPerMount: [...functions.values()].reduce((a, b) => a + b, 0) / roots, functions: ranked(functions), sources: ranked(sources) };
    results.push(result);
    console.log(JSON.stringify({ mode, estimatedBytesPerMount: result.estimatedBytesPerMount, sources: result.sources.slice(0, 6) }));
    await page.evaluate(() => { window.sampleAllocations.cleanup(); delete window.sampleAllocations; });
    await page.close();
  }
  await mkdir(out, { recursive: true });
  await writeFile(`${out}/allocations.json`, JSON.stringify({ measuredAt: new Date().toISOString(), chromium: browser.version(), configuration: `production unminified; CDP allocation sampling at ${interval}-byte interval, including collected objects`, note: "Sampled allocation estimates, one browser process, exclude total native DOM memory and are not exact allocation counts", results }, null, 2) + "\n");
} finally {
  if (browser) await browser.close();
  if (server.exitCode === null) { const stopped = new Promise(resolve => server.once("exit", resolve)); server.kill(); await stopped; }
}

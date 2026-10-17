import { chromium } from "@playwright/test";
import { spawn } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import os from "node:os";
import { execFileSync } from "node:child_process";

const cwd = fileURLToPath(new URL("../", import.meta.url));
const outDir = fileURLToPath(new URL("../../../docs/.ai/benchmarks/dom-scheduling/", import.meta.url));
const port = 5179;
const server = spawn(process.execPath, ["node_modules/vite/bin/vite.js", "--host", "127.0.0.1", "--port", String(port), "--strictPort"], { cwd, stdio: ["ignore", "pipe", "pipe"], windowsHide: true });
let serverOutput = "";
server.stdout.on("data", chunk => { serverOutput += chunk; });
server.stderr.on("data", chunk => { serverOutput += chunk; });
let browser;
try {
  let ready = false;
  for (let attempt = 0; attempt < 100; attempt++) {
    if (server.exitCode !== null) throw new Error(serverOutput);
    try { ready = (await fetch(`http://127.0.0.1:${port}/tests/scheduled.html`)).ok; } catch {}
    if (ready) break;
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  if (!ready) throw new Error(`Vite startup timed out: ${serverOutput}`);
  browser = await chromium.launch();
  const page = await browser.newPage();
  const pageErrors = [];
  page.on("pageerror", error => pageErrors.push(error.message));
  await page.goto(`http://127.0.0.1:${port}/tests/scheduled.html`);
  await page.waitForFunction(() => window.scheduledChecks);
  const data = await page.evaluate(async () => {
    const modes = [
      { id: "sync", synchronous: true, batch: false },
      { id: "microtask", synchronous: false, batch: false },
      { id: "sync+batch", synchronous: true, batch: true },
      { id: "microtask+batch", synchronous: false, batch: true }
    ];
    const workloads = [
      { id: "counter-burst", rows: 1, writesPerRow: 100, waves: 400, input: false },
      { id: "dashboard-burst", rows: 40, writesPerRow: 10, waves: 100, input: false },
      { id: "dashboard-single", rows: 40, writesPerRow: 1, waves: 200, input: false },
      { id: "input-single", rows: 1, writesPerRow: 1, waves: 3000, input: true },
      { id: "layout-reading-setter", rows: 20, writesPerRow: 10, waves: 100, input: false, layoutRead: true }
    ];
    const samples = [];
    async function run(workload, mode) {
      const fixtures = Array.from({ length: workload.rows }, () => window.scheduledChecks.create(mode.synchronous, false));
      const inputs = fixtures.map(f => f.Root.querySelector("input"));
      for (const f of fixtures) document.body.appendChild(f.Host);
      if (workload.layoutRead) for (const f of fixtures) f.OnWrite(() => { f.Root.offsetHeight; });
      document.body.offsetHeight;
      let setterCount = 0;
      let computeCount = 0;
      let sourceMs = 0;
      const started = performance.now();
      try {
        for (let wave = 0; wave < workload.waves; wave++) {
          const before = performance.now();
          for (let row = 0; row < fixtures.length; row++) {
            const f = fixtures[row];
            const writes = () => {
              for (let write = 0; write < workload.writesPerRow; write++) {
                const value = 1000 + wave * workload.writesPerRow + write;
                if (workload.input) {
                  inputs[row].value = `typed-${value}`;
                  inputs[row].dispatchEvent(new InputEvent("input", { bubbles: true, data: String(value) }));
                } else f.Set(value);
              }
            };
            if (mode.batch) f.Batch(writes); else writes();
          }
          sourceMs += performance.now() - before;
          await Promise.resolve(); await Promise.resolve();
          document.body.offsetHeight;
        }
        const totalMs = performance.now() - started;
        const last = 1000 + workload.waves * workload.writesPerRow - 1;
        const expectedSetters = workload.input ? 0 : workload.rows * workload.waves * (mode.batch || !mode.synchronous ? 1 : workload.writesPerRow);
        const expectedComputes = workload.input ? 0 : workload.rows * workload.waves * (mode.batch ? 1 : workload.writesPerRow);
        for (let row = 0; row < fixtures.length; row++) {
          const f = fixtures[row];
          if (workload.input) {
            if (inputs[row].value !== `typed-${last}`) throw new Error("Incorrect final input");
            f.SetName("verification");
          } else {
            if (f.Root.firstChild.data !== String(last) || f.Root.getAttribute("data-value") !== String(last)
              || f.Root.title !== String(last) || f.Root.getAttribute("data-doubled") !== String(last * 2)) {
              throw new Error(`Incorrect final DOM for ${workload.id}/${mode.id}`);
            }
          }
          if (f.Errors().length) throw new Error(`Setter errors: ${f.Errors()}`);
          setterCount += f.Writes() - 1;
          computeCount += f.Computes() - 1;
        }
        if (setterCount !== expectedSetters || computeCount !== expectedComputes) {
          throw new Error(`Unexpected work counts for ${workload.id}/${mode.id}: ${setterCount}/${computeCount}`);
        }
        await Promise.resolve(); await Promise.resolve();
        if (workload.input && inputs.some(input => input.value !== "verification")) throw new Error("Input state did not remain connected");
        return { totalMs, sourceMs, setterCount, computeCount };
      } finally { for (const f of fixtures) { f.DisposeGraph(); f.Host.remove(); } }
    }
    for (const workload of workloads) {
      for (let round = -5; round < 21; round++) {
        const order = [...modes];
        for (let i = 0; i < modes.length; i++) {
          const mode = order[(i + round + 8) % modes.length];
          const result = await run(workload, mode);
          if (round >= 0) samples.push({ workload: workload.id, mode: mode.id, round, ...result });
        }
      }
    }
    return { workloads, modes, samples, userAgent: navigator.userAgent };
  });
  if (pageErrors.length) throw new Error(`Uncaught browser errors: ${pageErrors}`);
  const median = values => { const sorted = [...values].sort((a, b) => a - b); return sorted[Math.floor(sorted.length / 2)]; };
  const summaries = data.workloads.flatMap(workload => data.modes.map(mode => {
    const rows = data.samples.filter(sample => sample.workload === workload.id && sample.mode === mode.id);
    const sorted = rows.map(row => row.totalMs).sort((a, b) => a - b);
    return { workload: workload.id, mode: mode.id, medianMs: median(sorted), p25Ms: sorted[5], p75Ms: sorted[15], medianSourceMs: median(rows.map(row => row.sourceMs)), minMs: sorted[0], maxMs: sorted.at(-1), setterCount: rows[0].setterCount, computeCount: rows[0].computeCount };
  }));
  const result = { timestamp: new Date().toISOString(), commit: execFileSync("rtk", ["git", "rev-parse", "HEAD"], { cwd, encoding: "utf8" }).trim(), node: process.version, platform: `${os.platform()} ${os.release()}`, cpu: os.cpus()[0].model, chromium: browser.version(), warmups: 5, rounds: 21, ...data, summaries };
  await mkdir(outDir, { recursive: true });
  await writeFile(`${outDir}results.json`, JSON.stringify(result, null, 2) + "\n");
  const section = data.workloads.map(workload => {
    const rows = summaries.filter(row => row.workload === workload.id);
    const baseline = rows.find(row => row.mode === "sync");
    const syncBatch = rows.find(row => row.mode === "sync+batch");
    const microBatch = rows.find(row => row.mode === "microtask+batch");
    return `## ${workload.id}\n\n${workload.rows} independently mounted rows × ${workload.writesPerRow} writes per row per wave × ${workload.waves} waves.\n\n` +
      rows.map(row => `- **${row.mode}**: ${row.medianMs.toFixed(2)} ms median (middle 50%: ${row.p25Ms.toFixed(2)}–${row.p75Ms.toFixed(2)} ms); ${(baseline.medianMs / row.medianMs).toFixed(2)}× baseline throughput. Counter property setter calls: ${row.setterCount}; derived memo computations: ${row.computeCount}.`).join("\n") +
      `\n\nAdding the queue to core batching: ${(syncBatch.medianMs / microBatch.medianMs).toFixed(2)}× throughput versus sync+batch.\n`;
  }).join("\n");
  const report = `# Fable.Ranvier DOM scheduling benchmark\n\nMeasured ${result.timestamp} at commit ${result.commit}.\n\n` +
    `Host: ${result.cpu.trim()}, ${result.platform}, Node ${result.node}, Chromium ${result.chromium} (headless).\n\n` +
    `## Method\n\nRelease-compiled F# scheduling fixtures run on attached real Chromium DOM nodes. Four modes isolate the queue from core batching. Each row has its own graph and mount; this is a multi-widget workload, not a single shared-graph dashboard. Core batching wraps each row's writes using the fixture's Batch function; the input case dispatches synthetic input events.\n\n` +
    `Five warm-up runs per workload/mode, then 21 measured samples with rotating mode order in one browser process. Setup/mounting and disposal are excluded. The timer includes signal/event work, two Promise microtask boundaries after every wave (in all modes), and an offsetHeight read after each wave to settle layout. It excludes animation-frame scheduling and paint; waves are drained immediately rather than paced at real frame or typing intervals. Ratios are ratios of median elapsed times for identical work, not absolute frame rates. No GC control, confidence intervals or cross-browser runs. Samples within a run share a browser process; an earlier separate browser-process run from this investigation is retained in [replicate-1.json](replicate-1.json).\n\n` +
    `Final text, attributes, derived values, input connectivity, error state and expected setter/memo counts are asserted. The setter counter measures one counter-property binding, not all DOM writes. An input-only run therefore reports zero counter setter/memo calls. Input setters compare the live DOM value and skip identical assignments in both modes. The layout-reading case additionally calls offsetHeight inside every counter setter; it deliberately exposes interleaved writes/layout reads and is not representative of cheap setters.\n\n` +
    section +
    `\n## Interpretation\n\nFewer DOM mutations do not imply less total work: the queue adds a memo, ownership handling and queue operations, while unbatched source writes still recompute the graph. Core batching removes those intermediate computations as well as intermediate DOM writes. Read the microtask versus sync comparison separately from microtask+batch versus sync: the latter combines two optimizations. Compare microtask+batch with sync+batch to isolate whether the queue adds value once state writes are already batched.\n\n` +
    `This is a local naive benchmark of the current PoC fixtures, not a general renderer or production-app performance claim. No production runtime was changed to obtain these results. Raw samples are in [results.json](results.json).\n\n` +
    `## Reproduce\n\nFrom the repository root, with playground dependencies and Playwright Chromium installed:\n\n\`\`\`powershell\nrtk proxy npm --prefix examples/Fable.Ranvier.Playground run bench:dom\n\`\`\`\n\nThe command starts its own Vite server on port 5179 and closes that process and its browser on completion. It overwrites this report and results.json.\n`;
  await writeFile(`${outDir}README.md`, report);
  console.log(JSON.stringify({ environment: { cpu: result.cpu, chromium: result.chromium }, summaries }, null, 2));
} finally {
  if (browser) await browser.close();
  if (server.exitCode === null) {
    const stopped = new Promise(resolve => server.once("exit", resolve));
    server.kill();
    await stopped;
  }
}

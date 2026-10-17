import { chromium } from "@playwright/test";
import { spawn, execFileSync } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

const cwd = fileURLToPath(new URL("../../", import.meta.url));
const out = fileURLToPath(new URL("../../../../docs/research/framework-performance/investigation/", import.meta.url));
const servers = [], samples = [], checks = [];
let browser;
try {
  for (const shared of [false, true]) {
    const env = { ...process.env, RANVIER_SHARED_BACKEND: String(shared), RANVIER_PERF_PROFILE: "false" };
    execFileSync("rtk", ["node", "node_modules/vite/bin/vite.js", "build", "--config", "benchmarks/frameworks/vite.config.mjs"], { cwd, env, stdio: "inherit", windowsHide: true });
    const port = shared ? 5190 : 5189;
    const server = spawn(process.execPath, ["node_modules/vite/bin/vite.js", "preview", "--config", "benchmarks/frameworks/vite.config.mjs", "--host", "127.0.0.1", "--port", String(port), "--strictPort"], { cwd, env, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
    servers.push(server);
    let output = "";
    server.stdout.on("data", data => output += data);
    server.stderr.on("data", data => output += data);
    let ready = false;
    for (let i = 0; i < 100; i++) {
      if (server.exitCode !== null) throw Error(output);
      try { ready = (await fetch(`http://127.0.0.1:${port}/benchmarks/frameworks/index.html`)).ok; } catch {}
      if (ready) break;
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    if (!ready) throw Error(output);
  }
  for (let replicate = 0; replicate < 3; replicate++) {
    browser = await chromium.launch();
    const pages = [];
    for (const [variant, port] of [["baseline", 5189], ["shared-backend", 5190]]) {
      const page = await browser.newPage();
      await page.goto(`http://127.0.0.1:${port}/benchmarks/frameworks/index.html`);
      await page.waitForFunction(() => window.frameworkPerf);
      const proof = await page.evaluate(() => ({ workload: window.frameworkPerf.verify("ranvier", 128), ranges: window.frameworkPerf.rangeProofs() }));
      checks.push({ variant, replicate, ...proof });
      pages.push(page);
    }
    for (const width of [16, 128, 512]) for (const phase of ["mount", "dispose"]) {
      for (let round = -3; round < 11; round++) for (let offset = 0; offset < 2; offset++) {
        const index = (round + offset + replicate + 4) % 2;
        const ms = await pages[index].evaluate(({ width, phase }) => window.frameworkPerf.sample("ranvier", width, phase, 32), { width, phase });
        if (round >= 0) samples.push({ variant: index ? "shared-backend" : "baseline", replicate, width, phase, round, ms });
      }
    }
    await browser.close(); browser = undefined;
    console.log(`Shared backend probe process ${replicate + 1}/3 passed`);
  }
  await mkdir(out, { recursive: true });
  await writeFile(`${out}/shared-backend.json`, JSON.stringify({ measuredAt: new Date().toISOString(), configuration: "production minified; compiler-only diagnostic, no runtime source changes", checks, samples }, null, 2) + "\n");
} finally {
  if (browser) await browser.close();
  for (const server of servers) if (server.exitCode === null) { const stopped = new Promise(resolve => server.once("exit", resolve)); server.kill(); await stopped; }
}

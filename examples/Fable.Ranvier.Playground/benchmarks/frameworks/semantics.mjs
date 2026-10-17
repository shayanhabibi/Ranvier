import { chromium } from "@playwright/test";
import { spawn, execFileSync } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";

// Probes caret preservation and mount failure behaviour of each framework in the comparison bundle.
const cwd = fileURLToPath(new URL("../../", import.meta.url));
const out = fileURLToPath(new URL("../../../../docs/research/framework-performance/investigation/", import.meta.url));
const frameworks = ["ranvier", "solid", "ripple"], port = 5196;
if (process.argv.includes("--build")) {
  for (const args of [
    ["dotnet", "fable", "tests/DomTests.fsproj", "--outDir", "generated-tests", "--configuration", "Release"],
    ["dotnet", "fable", "benchmarks/ripple/RipplePerf.fsproj", "--outDir", "generated-ripple", "--configuration", "Release"],
    ["node", "node_modules/vite/bin/vite.js", "build", "--config", "benchmarks/frameworks/vite.config.mjs"]
  ]) execFileSync("rtk", args, { cwd, stdio: "inherit", windowsHide: true, env: { ...process.env, RanvierCounters: "false" } });
}
const server = spawn(process.execPath, ["node_modules/vite/bin/vite.js", "preview", "--config", "benchmarks/frameworks/vite.config.mjs", "--host", "127.0.0.1", "--port", String(port), "--strictPort"], { cwd, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
let output = "", browser;
server.stdout.on("data", data => output += data);
server.stderr.on("data", data => output += data);
const results = [];
try {
  const url = `http://127.0.0.1:${port}/benchmarks/frameworks/index.html`;
  let ready = false;
  for (let attempt = 0; attempt < 100 && !ready; attempt++) {
    if (server.exitCode !== null) throw Error(output);
    try { ready = (await fetch(url)).ok; } catch { await new Promise(resolve => setTimeout(resolve, 100)); }
  }
  if (!ready) throw Error(output);
  browser = await chromium.launch();
  for (const framework of frameworks) for (const scenario of ["caret", "setter", "reader"]) {
    const page = await browser.newPage();
    const pageErrors = [];
    page.on("pageerror", error => pageErrors.push(String(error.message)));
    await page.goto(url);
    await page.waitForFunction(() => window.frameworkSemantics);
    const result = await page.evaluate(({ framework, scenario }) => {
      const probes = window.frameworkSemantics[framework];
      const host = window.frameworkSemantics.host();
      if (scenario === "caret") {
        const dispose = probes.caret(host);
        const input = host.querySelector("input");
        input.focus();
        input.value = "Grace";
        input.setSelectionRange(2, 2);
        input.dispatchEvent(new Event("input", { bubbles: true }));
        const state = {
          sameNode: input === host.querySelector("input"),
          focused: document.activeElement === input,
          selection: [input.selectionStart, input.selectionEnd],
          value: input.value
        };
        dispose();
        return state;
      }
      Object.defineProperty(HTMLElement.prototype, "explode", {
        configurable: true,
        set(value) { if (value === "boom") throw Error("setter"); }
      });
      host.innerHTML = "<span>keep</span>";
      const probe = probes.failure(host, scenario);
      const afterMount = host.innerHTML;
      const cleanedAtMount = probe.cleaned();
      let laterReads, updateError = null;
      try { laterReads = probe.update(); } catch (error) { updateError = String(error.message); }
      let disposeError = null;
      try { probe.dispose(); } catch (error) { disposeError = String(error.message); }
      return { threw: probe.threw, message: probe.message, afterMount, cleanedAtMount, laterReads, updateError, cleanedAfterDispose: probe.cleaned(), disposeError, afterDispose: host.innerHTML };
    }, { framework, scenario });
    results.push({ framework, scenario, ...result, pageErrors });
    await page.close();
  }
  await mkdir(out, { recursive: true });
  await writeFile(`${out}/semantics-results.json`, JSON.stringify({ measuredAt: new Date().toISOString(), chromium: browser.version(), results }, null, 2) + "\n");
  console.log(JSON.stringify(results, null, 2));
} finally {
  if (browser) await browser.close();
  if (server.exitCode === null) { const stopped = new Promise(resolve => server.once("exit", resolve)); server.kill(); await stopped; }
}

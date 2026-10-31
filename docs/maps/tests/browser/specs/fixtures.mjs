import { test as base, expect } from "@playwright/test";
import { writeFile } from "node:fs/promises";

/** A page that records console errors and uncaught exceptions; each test ends by expecting none. */
export const test = base.extend({
  problems: async ({ page }, use, testInfo) => {
    const problems = [];
    const requests = new Map();
    const events = [];
    const started = Date.now();
    await page.addInitScript(() => {
      const messages = [];
      globalThis.__docsWorkerProtocol = messages;
      const started = performance.now();
      const record = (direction, packet) => {
        if (messages.length >= 100) return;
        let tag;
        try { tag = typeof packet === "string" ? JSON.parse(packet)?.[0] : undefined; } catch {}
        if (typeof tag === "string") messages.push({ ms: Math.round(performance.now() - started), direction, tag });
      };
      const OriginalWorker = globalThis.Worker;
      if (!OriginalWorker) return;
      globalThis.Worker = class extends OriginalWorker {
        constructor(...args) {
          super(...args);
          this.addEventListener("message", event => record("from-worker", event.data));
          this.addEventListener("error", event => {
            if (messages.length < 100) messages.push({ ms: Math.round(performance.now() - started), direction: "worker-error", message: event.message });
          });
        }
        postMessage(packet, ...args) {
          record("to-worker", packet);
          return super.postMessage(packet, ...args);
        }
      };
    });
    const record = (kind, detail) => {
      if (events.length < 200) events.push({ ms: Date.now() - started, kind, detail });
    };
    page.on("request", request => requests.set(request, Date.now()));
    page.on("requestfinished", request => {
      record("requestfinished", { url: request.url(), ms: Date.now() - requests.get(request) });
      requests.delete(request);
    });
    page.on("requestfailed", request => {
      record("requestfailed", { url: request.url(), failure: request.failure() });
      requests.delete(request);
    });
    page.on("response", response => {
      if (response.status() >= 400) record("response", { url: response.url(), status: response.status() });
    });
    page.on("worker", worker => record("worker", worker.url()));
    page.on("pageerror", error => problems.push(`pageerror: ${error.stack ?? error.message}`));
    page.on("console", message => { if (message.type() === "error") problems.push(`console: ${message.text()}`); });
    await use(problems);
    const failed = testInfo.status !== testInfo.expectedStatus || problems.length > 0;
    const diagnostic = process.env.DOCS_COMPILER_DIAGNOSTIC === "1";
    const capture = async (label, filename) => {
      const live = await page.locator(".nacara-live__actions").evaluateAll(actions => actions.map(action => ({
        status: action.querySelector(".nacara-live__status")?.textContent,
        disabled: action.querySelector(".nacara-live__run")?.disabled,
        editor: !!action.closest(".nacara-code")?.querySelector(".cm-editor")
      }))).catch(error => ({ error: String(error) }));
      const workerProtocol = await page.evaluate(() => globalThis.__docsWorkerProtocol ?? []).catch(error => [{ error: String(error) }]);
      const diagnostics = { problems, live, events, workerProtocol, test: { title: testInfo.title, status: testInfo.status, worker: testInfo.workerIndex }, pending: [...requests].map(([request, time]) => ({
        url: request.url(), ms: Date.now() - time
      })) };
      console.log(label, JSON.stringify(diagnostics));
      const path = testInfo.outputPath(filename);
      await writeFile(path, JSON.stringify(diagnostics, null, 2));
      await testInfo.attach(filename, { path, contentType: "application/json" });
    };
    if (failed || diagnostic) {
      await capture(failed ? "DOCS_BROWSER_FAILURE" : "DOCS_BROWSER_DIAGNOSTIC", "browser-diagnostics.json");
    }
    if (failed && diagnostic) {
      await new Promise(resolve => setTimeout(resolve, 10_000));
      await capture("DOCS_BROWSER_OBSERVATION", "browser-observation.json");
    }
    expect(problems).toEqual([]);
  }
});

export { expect };

/** The map cards of a page, in page order, once every map has run its scenario. */
export async function openMaps(page, path) {
  await page.goto(path);
  const maps = page.locator(".rv-map");
  await expect(maps.first()).toBeVisible();
  await expect.poll(() => page.locator(".rv-map .rv-map-node").count()).toBeGreaterThan(0);
  for (const map of await maps.all()) {
    await expect(map).toHaveAttribute("aria-busy", "false");
    await expect(map.locator(".rv-map-node").first()).toBeAttached();
  }
  return maps;
}

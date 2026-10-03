import { test, expect } from "@playwright/test";

test.beforeEach(async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  page.scheduledErrors = errors;
  await page.goto("/tests/scheduled.html");
  await page.waitForFunction(() => window.scheduledChecks);
});
test.afterEach(async ({ page }) => { expect(page.scheduledErrors).toEqual([]); });

test("100 source writes produce one DOM text mutation", async ({ page }) => {
  expect(await page.evaluate(async () => {
    const f = window.scheduledChecks.create(false, false);
    try {
      document.body.appendChild(f.Host);
      const text = f.Root.firstChild;
      const records = [];
      const observer = new MutationObserver(items => records.push(...items));
      observer.observe(text, { characterData: true });
      for (let i = 1; i <= 100; i++) f.Set(i);
      const before = [text.data, f.Writes()];
      await Promise.resolve(); await Promise.resolve();
      records.push(...observer.takeRecords()); observer.disconnect();
      return { before, after: [text.data, f.Writes()], mutations: records.length, same: text === f.Root.firstChild };
    } finally { f.DisposeGraph(); }
  })).toEqual({ before: ["0", 1], after: ["100", 2], mutations: 1, same: true });
});

test("composition input echo keeps the caret and rejects an older queued value", async ({ page }) => {
  expect(await page.evaluate(async () => {
    const f = window.scheduledChecks.create(false, true);
    try {
      document.body.appendChild(f.Host);
      const input = f.Root.querySelector("input");
      input.focus(); f.SetName("obsolete");
      input.dispatchEvent(new CompositionEvent("compositionstart", { bubbles: true }));
      input.value = "AdXa"; input.setSelectionRange(3, 3);
      input.dispatchEvent(new InputEvent("input", { bubbles: true, isComposing: true, data: "X" }));
      await Promise.resolve(); await Promise.resolve();
      input.dispatchEvent(new CompositionEvent("compositionend", { bubbles: true }));
      return [input.value, input.selectionStart, document.activeElement === input];
    } finally { f.DisposeGraph(); }
  })).toEqual(["AdXa", 3, true]);
});

test("dispose before the microtask cancels writes on retained nodes", async ({ page }) => {
  expect(await page.evaluate(async () => {
    const f = window.scheduledChecks.create(false, false);
    f.Set(1); f.Dispose(); await Promise.resolve(); await Promise.resolve();
    const result = [f.Root.firstChild.data, f.Writes(), f.Host.childNodes.length];
    f.DisposeGraph(); return result;
  })).toEqual(["0", 1, 0]);
});

test("setter error is recorded while sibling and later writes complete", async ({ page }) => {
  expect(await page.evaluate(async () => {
    const f = window.scheduledChecks.create(false, false);
    try {
      f.Set(13); await Promise.resolve(); await Promise.resolve();
      const first = [f.Root.firstChild.data, f.Errors()];
      f.Set(14); await Promise.resolve(); await Promise.resolve();
      return [first, f.Root.getAttribute("data-value"), f.Errors()];
    } finally { f.DisposeGraph(); }
  })).toEqual([["13", ["setter failed"]], "14", []]);
});

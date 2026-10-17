import { test, expect } from "@playwright/test";
import { readFile, writeFile } from "node:fs/promises";

test.beforeEach(async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto("/");
  page.errors = errors;
});

test.afterEach(async ({ page }) => {
  expect(page.errors).toEqual([]);
});

test("counter, derived output and disabled property update", async ({ page }) => {
  await expect(page.getByTestId("reset")).toBeDisabled();
  await page.getByTestId("increment").click();
  await expect(page.getByTestId("count")).toHaveText("1");
  await expect(page.getByTestId("doubled")).toHaveText("2");
  await expect(page.getByTestId("reset")).toBeEnabled();
  await expect(page.getByTestId("demo-root")).toHaveAttribute("data-parity", "odd");
  await page.getByTestId("reset").click();
  await expect(page.getByTestId("count")).toHaveText("0");
  await expect(page.getByTestId("doubled")).toHaveText("0");
  await expect(page.getByTestId("reset")).toBeDisabled();
});

test("input node, focus and selection survive a reactive input event", async ({ page }) => {
  const input = page.getByTestId("name");
  await input.fill("Ada");
  await expect(page.getByTestId("greeting")).toHaveText("Hello, Ada");
  const retained = await input.elementHandle();
  await input.evaluate(element => {
    element.focus();
    element.value = "Grace";
    element.setSelectionRange(2, 2);
    element.dispatchEvent(new Event("input", { bubbles: true }));
  });
  await expect(page.getByTestId("greeting")).toHaveText("Hello, Grace");
  expect(await retained.evaluate(element => [
    element === document.querySelector('[data-testid="name"]'),
    document.activeElement === element,
    element.selectionStart,
    element.selectionEnd
  ])).toEqual([true, true, 2, 2]);
  await page.getByTestId("increment").click();
  expect(await retained.evaluate(element => element === document.querySelector('[data-testid="name"]'))).toBe(true);
});

test("text and element identity stay stable", async ({ page }) => {
  await expect(page.getByTestId("count")).toHaveText("0");
  expect(await page.evaluate(() => {
    const root = document.querySelector('[data-testid="demo-root"]');
    const count = document.querySelector('[data-testid="count"]');
    const text = count.firstChild;
    document.querySelector('[data-testid="increment"]').click();
    return root === document.querySelector('[data-testid="demo-root"]')
      && count === document.querySelector('[data-testid="count"]')
      && text === count.firstChild
      && text.data === "1";
  })).toBe(true);
});

test("Foundation keyed views are consumed by the playground app", async ({ page }) => {
  const alpha = page.locator("#foundation-row-1");
  const beta = page.locator("#foundation-row-2");
  await expect(alpha).toHaveText("Alpha");
  await expect(beta).toHaveText("Beta");
  await expect(page.locator("#foundation-demo")).toHaveAttribute("data-row-count", "2");

  const retainedAlpha = await alpha.elementHandle();
  await page.locator("#foundation-add").click();
  await expect(page.locator("#foundation-row-3")).toHaveText("Item 3");
  await expect(page.locator("#foundation-demo")).toHaveAttribute("data-row-count", "3");
  await page.locator("#foundation-reverse").click();

  expect(await retainedAlpha.evaluate(element => element === document.querySelector("#foundation-row-1"))).toBe(true);
  await expect(page.locator("#foundation-demo")).toHaveText("Foundation implementation: keyed rowsAdd rowReverse rowsItem 3BetaAlpha");
});

test("authored components share reactive props and keep independent local state", async ({ page }) => {
  const first = page.locator("#component-first");
  const second = page.locator("#component-second");
  await expect(first.locator("h2")).toHaveText("Shared label");
  await expect(second.locator("h2")).toHaveText("Shared label");
  await expect(page.locator("#component-first-count")).toHaveText("0");
  await expect(page.locator("#component-second-count")).toHaveText("10");
  await expect(first).toContainText("First counter child content");
  await expect(second).toContainText("Child sees Shared label");
  const retained = await page.locator("#component-first-count").elementHandle();
  await page.locator("#component-first-increment").click();
  await expect(page.locator("#component-first-count")).toHaveText("1");
  await expect(page.locator("#component-second-count")).toHaveText("10");
  await expect(page.locator("#component-changed")).toHaveText("First changed to 1");
  await page.locator("#component-label").fill("Edited");
  await expect(first.locator("h2")).toHaveText("Edited");
  await expect(second.locator("h2")).toHaveText("Edited");
  await expect(first).toHaveAttribute("title", "Edited");
  await expect(page.locator("#component-first-count")).toHaveText("1");
  await page.locator("#component-rename").click();
  await expect(first.locator("h2")).toHaveText("Renamed");
  await expect(second.locator("h2")).toHaveText("Renamed");
  await expect(second).toContainText("Child sees Renamed");
  await expect(page.locator("#component-rename")).toBeDisabled();
  await expect(page.locator("#component-label")).toHaveValue("Renamed");
  await expect(page.locator("#component-first-count")).toHaveText("1");
  await page.locator("#component-second-increment").click();
  await expect(page.locator("#component-second-count")).toHaveText("11");
  await expect(page.locator("#component-changed")).toHaveText("Second changed to 11");
  expect(await retained.evaluate(node => node === document.querySelector("#component-first-count"))).toBe(true);
});

test("authored input composition preserves node focus and caret", async ({ page }) => {
  const input = page.locator("#component-label");
  const retained = await input.elementHandle();
  expect(await input.evaluate(async element => {
    element.focus();
    element.dispatchEvent(new CompositionEvent("compositionstart", { bubbles: true }));
    element.value = "編集中";
    element.setSelectionRange(2, 2);
    element.dispatchEvent(new InputEvent("input", { bubbles: true, data: "中", isComposing: true }));
    await Promise.resolve(); await Promise.resolve();
    const during = [document.activeElement === element, element.selectionStart, element.selectionEnd, element.value];
    element.dispatchEvent(new CompositionEvent("compositionend", { bubbles: true, data: "中" }));
    element.dispatchEvent(new InputEvent("input", { bubbles: true, data: "中", isComposing: false }));
    await Promise.resolve(); await Promise.resolve();
    return [during, [document.activeElement === element, element.selectionStart, element.selectionEnd, element.value]];
  })).toEqual([[true, 2, 2, "編集中"], [true, 2, 2, "編集中"]]);
  await expect(page.locator("#component-first h2")).toHaveText("編集中");
  await expect(page.locator("#component-second h2")).toHaveText("編集中");
  await page.locator("#component-rename").evaluate(button => button.click());
  await expect(input).toHaveValue("Renamed");
  expect(await retained.evaluate(element => element === document.querySelector("#component-label") && document.activeElement === element)).toBe(true);
});

test("authored checkbox handles DOM events and programmatic signal changes", async ({ page }) => {
  const input = page.locator("#component-checkbox");
  await expect(input).not.toBeChecked();
  expect(await input.getAttribute("checked")).toBeNull();
  const retained = await input.elementHandle();
  await input.check();
  await expect(page.locator("#component-checkbox-state")).toHaveText("Checked");
  expect(await input.getAttribute("checked")).toBeNull();
  await page.locator("#component-toggle-checkbox").click();
  await expect(input).not.toBeChecked();
  await expect(page.locator("#component-checkbox-state")).toHaveText("Unchecked");
  expect(await input.getAttribute("checked")).toBeNull();
  await page.locator("#component-toggle-checkbox").click();
  await expect(input).toBeChecked();
  await expect(page.locator("#component-checkbox-state")).toHaveText("Checked");
  expect(await retained.evaluate(element => element === document.querySelector("#component-checkbox"))).toBe(true);
  expect(await input.getAttribute("checked")).toBeNull();
});

test("unmount removes listeners and repeated remount preserves state", async ({ page }) => {
  await page.getByTestId("increment").click();
  const detachedButton = await page.getByTestId("increment").elementHandle();
  await page.getByTestId("mount-toggle").click();
  await expect(page.getByTestId("demo-root")).toHaveCount(0);
  await detachedButton.evaluate(element => element.click());
  await page.getByTestId("mount-toggle").click();
  await expect(page.getByTestId("count")).toHaveText("1");
  for (let i = 0; i < 5; i++) {
    await page.getByTestId("mount-toggle").click();
    await page.getByTestId("mount-toggle").click();
  }
  await expect(page.getByTestId("demo-root")).toHaveCount(1);
  await page.getByTestId("increment").click();
  await expect(page.getByTestId("count")).toHaveText("2");
  await expect(page.getByTestId("doubled")).toHaveText("4");
});

test("Foundation core boundary shows pending failure and recovery on retained nodes", async ({ page }) => {
  const result = page.locator("#foundation-load-result");
  const status = page.locator("#foundation-load-status");
  const retained = await result.elementHandle();
  await expect(result).toHaveText("Loaded result");
  await expect(page.locator("#foundation-load")).toHaveAttribute("aria-busy", "false");
  await page.locator("#foundation-pending").click();
  await expect(result).toHaveText("Loading…");
  await expect(status).toHaveText("Pending");
  await expect(page.locator("#foundation-load")).toHaveAttribute("aria-busy", "true");
  await page.locator("#foundation-fail").click();
  await expect(result).toHaveText("Could not load the result");
  await expect(status).toHaveText("Failed");
  await expect(page.locator("#foundation-load-error")).toHaveText("Connection unavailable");
  await page.locator("#foundation-recover").click();
  await expect(result).toHaveText("Recovered result");
  await expect(status).toHaveText("Ready");
  await expect(page.locator("#foundation-load")).toHaveAttribute("aria-busy", "false");
  await expect(page.locator("#foundation-load-error")).toBeEmpty();
  expect(await retained.evaluate(node => node === document.querySelector("#foundation-load-result"))).toBe(true);
});

test("default synchronous mode batches a burst before click returns", async ({ page }) => {
  await expect(page.getByTestId("scheduling-status")).toHaveText("Synchronous DOM commits · batched events");
  expect(await page.evaluate(() => {
    const text = document.querySelector('[data-testid="count"]').firstChild;
    const observer = new MutationObserver(() => {});
    observer.observe(text, { characterData: true });
    document.querySelector('[data-testid="burst"]').click();
    const result = [text.data, observer.takeRecords().length];
    observer.disconnect();
    return result;
  })).toEqual(["100", 1]);
});

test("microtask mode batches a burst and preserves state across scheduling changes", async ({ page }) => {
  await page.getByTestId("scheduling-toggle").click();
  await expect(page.getByTestId("scheduling-status")).toContainText("Microtask");
  expect(await page.evaluate(async () => {
    const output = document.querySelector('[data-testid="count"]');
    const text = output.firstChild;
    const records = [];
    const observer = new MutationObserver(items => records.push(...items));
    observer.observe(text, { characterData: true });
    document.querySelector('[data-testid="burst"]').click();
    const immediate = text.data;
    await Promise.resolve(); await Promise.resolve();
    records.push(...observer.takeRecords()); observer.disconnect();
    return [immediate, text.data, records.length];
  })).toEqual(["0", "100", 1]);
  await expect(page.getByTestId("doubled")).toHaveText("200");
  await page.getByTestId("scheduling-toggle").click();
  await expect(page.getByTestId("count")).toHaveText("100");
  await expect(page.getByTestId("scheduling-status")).toContainText("Synchronous");
  expect(await page.evaluate(() => {
    document.querySelector('[data-testid="increment"]').click();
    return document.querySelector('[data-testid="count"]').textContent;
  })).toBe("101");
});

test("Fable watch replaces the app through Vite cleanup without reloading the page", async ({ page }) => {
  test.setTimeout(45_000);
  const source = new URL("../App.fs", import.meta.url);
  const original = await readFile(source, "utf8");
  const modified = original.replace('Dom.text "Create once."', 'Dom.text "Create once, live."');
  expect(modified).not.toBe(original);
  await expect(page.getByTestId("count")).toHaveText("0");
  const oldRoot = await page.getByTestId("demo-root").elementHandle();
  await page.evaluate(() => { document.documentElement.dataset.watchProbe = "same-document"; });
  try {
    await writeFile(source, modified);
    await expect(page.getByRole("heading", { level: 1 })).toContainText("Create once, live.", { timeout: 20_000 });
    expect(await page.evaluate(() => document.documentElement.dataset.watchProbe)).toBe("same-document");
    expect(await oldRoot.evaluate(element => element.parentNode === null)).toBe(true);
    await expect(page.getByTestId("demo-root")).toHaveCount(1);
    await page.getByTestId("increment").click();
    await expect(page.getByTestId("count")).toHaveText("1");
  } finally {
    await writeFile(source, original);
    await expect(page.getByRole("heading", { level: 1 })).toContainText("Create once.", { timeout: 20_000 });
  }
});

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

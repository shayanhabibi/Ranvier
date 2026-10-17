import { test, expect } from "@playwright/test";

test("owned range lifecycle and attachment rollback", async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto("/tests/range.html");
  await page.waitForFunction(() => window.rangeCases);
  const results = await page.evaluate(() => window.rangeCases.map(([name, check]) => {
    try { check(); return { name, passed: true }; }
    catch (error) { return { name, passed: false, error: String(error) }; }
  }));
  expect(results.length).toBeGreaterThan(0);
  expect(results.filter(result => !result.passed)).toEqual([]);
  expect(errors).toEqual([]);
});

test("binding microtasks coalesce and cancel in Chromium", async ({ page }) => {
  await page.goto("/tests/range.html");
  await page.waitForFunction(() => window.bindingMicrotaskFixture);
  const values = await page.evaluate(async () => {
    const fixture = window.bindingMicrotaskFixture();
    try {
      const initial = fixture.Values();
      fixture.Update(2);
      fixture.Update(3);
      await Promise.resolve();
      const committed = fixture.Values();
      fixture.Update(4);
      fixture.Dispose();
      await Promise.resolve();
      return [initial, committed, fixture.Values()];
    } finally { fixture.Dispose(); }
  });
  expect(values).toEqual([[], [3], [3]]);
});

test("template rows keep focus and selection across list moves in Chromium", async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto("/tests/range.html");
  await page.waitForFunction(() => window.templateBrowserCases);
  const results = await page.evaluate(() => window.templateBrowserCases.map(([name, check]) => {
    try { check(); return { name, passed: true }; }
    catch (error) { return { name, passed: false, error: String(error) }; }
  }));
  expect(results.length).toBeGreaterThan(0);
  expect(results.filter(result => !result.passed)).toEqual([]);
  expect(errors).toEqual([]);
});

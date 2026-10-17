import { test, expect } from "@playwright/test";

test("lifetime ordering across core entry points", async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto("/tests/lifetime.html");
  await page.waitForFunction(() => window.lifetimeCases);
  const results = await page.evaluate(() => window.lifetimeCases.map(([name, check]) => {
    try { check(); return { name, passed: true }; }
    catch (error) { return { name, passed: false, error: String(error) }; }
  }));
  expect(results.length).toBeGreaterThan(0);
  expect(results.filter(result => !result.passed)).toEqual([]);
  expect(errors).toEqual([]);
});

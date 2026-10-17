import { test, expect } from "@playwright/test";
test("typed components and prop lifecycle", async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto("/tests/component.html");
  await page.waitForFunction(() => window.componentCases);
  const results = await page.evaluate(() => window.componentCases.map(([name, check]) => {
    try { check(); return { name, passed: true }; }
    catch (error) { return { name, passed: false, error: String(error) }; }
  }));
  expect(results.length).toBeGreaterThan(0);
  expect(results.filter(result => !result.passed)).toEqual([]);
  expect(errors).toEqual([]);
});

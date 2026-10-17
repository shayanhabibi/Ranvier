import { test, expect } from "@playwright/test";

test("representation ordering across core entry points", async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.goto("/tests/representation.html");
  await page.waitForFunction(() => window.representationCases && window.representationBench);
  await page.evaluate(() => window.representationBench.verify());
  const results = await page.evaluate(() => window.representationCases.map(([name, check]) => {
    try { check(); return { name, passed: true }; }
    catch (error) { return { name, passed: false, error: String(error) }; }
  }));
  expect(results.length).toBeGreaterThan(0);
  expect(results.filter(result => !result.passed)).toEqual([]);
  expect(errors).toEqual([]);
});

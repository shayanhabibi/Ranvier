import { test, expect } from "@playwright/test";

test("wide mount cleanup preserves ownership and isolates sibling mounts", async ({ page }) => {
  const errors = [];
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto("/tests/mounting-investigation.html");
  await page.waitForFunction(() => window.mountingInvestigationCases);
  const results = await page.evaluate(() => window.mountingInvestigationCases.map(([name, run]) => {
    try { run(); return { name, passed: true }; }
    catch (error) { return { name, passed: false, error: String(error) }; }
  }));
  expect(results).toHaveLength(4);
  expect(results.filter(row => !row.passed)).toEqual([]);
  expect(errors).toEqual([]);
});

import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./tests",
  testMatch: "**/*.spec.mjs",
  fullyParallel: false,
  timeout: 15_000,
  expect: { timeout: 3_000 },
  use: { baseURL: "http://127.0.0.1:5178", browserName: "chromium" },
  webServer: {
    command: "npm run dev:test",
    url: "http://127.0.0.1:5178",
    reuseExistingServer: false,
    timeout: 30_000
  }
});

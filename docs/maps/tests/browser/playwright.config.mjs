import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "specs",
  fullyParallel: true,
  use: {
    baseURL: "http://127.0.0.1:5090/Ranvier/", browserName: "chromium",
    trace: "retain-on-failure", screenshot: "only-on-failure"
  },
  webServer: {
    command: "node serve.mjs",
    url: "http://127.0.0.1:5090/Ranvier/",
    reuseExistingServer: !process.env.CI,
    timeout: 30_000
  }
});

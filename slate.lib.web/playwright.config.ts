import { defineConfig } from "@playwright/test";
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 2,
  timeout: 45000,
  expect: { timeout: 10000 },
  use: {
    baseURL: "http://127.0.0.1:3100",
    viewport: { width: 1440, height: 900 },
    serviceWorkers: "block",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  reporter: [["list"], ["html", { open: "never" }]],
  webServer: {
    command: "node --env-file-if-exists=.env.local scripts/start.mjs",
    url: "http://127.0.0.1:3100",
    env: { PORT: "3100", SLATE_WEB_HOST: "127.0.0.1" },
    reuseExistingServer: !process.env.CI,
    timeout: 60000,
  },
  outputDir: "test-results",
});

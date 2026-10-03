import { defineConfig } from "@playwright/test";
import base from "./playwright.config";

const address = process.env.SLATE_CONTAINER_URL;
if (!address) throw new Error("Set SLATE_CONTAINER_URL to the running local container URL.");
const url = new URL(address);
if (url.protocol !== "http:" || !["localhost", "127.0.0.1", "[::1]"].includes(url.hostname)) {
  throw new Error("Container verification must use a local HTTP address.");
}

export default defineConfig({
  ...base,
  use: { ...base.use, baseURL: url.origin },
  webServer: undefined,
  outputDir: "output/docker/test-results",
  reporter: [["list"], ["html", { outputFolder: "output/docker/playwright-report", open: "never" }]],
});

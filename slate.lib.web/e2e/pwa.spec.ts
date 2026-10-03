import { test, expect } from "@playwright/test";
import { mockSlate, connect, openNote } from "./fixture";
test.use({ serviceWorkers: "allow" });
test("PWA registers, caches only static assets, and gives an honest offline page", async ({
  page,
  context,
}) => {
  await mockSlate(page);
  await connect(page);
  await openNote(page);
  await page.evaluate(() => navigator.serviceWorker.ready.then(() => true));
  const manifest = await page.request.get("/manifest.webmanifest");
  expect((await manifest.json()).display).toBe("standalone");
  await page.reload();
  await expect(page.getByRole("article")).toBeVisible();
  const urls = await page.evaluate(async () =>
    (
      await Promise.all(
        (await caches.keys()).map(async (key) =>
          (await (await caches.open(key)).keys()).map(
            (request) => new URL(request.url).pathname,
          ),
        ),
      )
    ).flat(),
  );
  expect(urls).toContain("/offline.html");
  expect(
    urls.every(
      (url) =>
        url.startsWith("/icons/") ||
        url.startsWith("/_next/static/") ||
        url === "/offline.html",
    ),
  ).toBe(true);
  await context.setOffline(true);
  await page.goto("/");
  await expect(page.getByRole("heading")).toContainText("offline");
});

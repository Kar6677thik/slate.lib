import { test, expect } from "@playwright/test";
import { mockSlate, connect, openNote, first, second } from "./fixture";
test("connection, lazy folders, tabs, read, edit, save, search and history", async ({
  page,
}) => {
  const state = await mockSlate(page);
  await connect(page);
  expect(
    state.requests.filter((r) => r.path === "v1/library").length,
  ).toBeLessThan(4);
  await openNote(page);
  await page.getByRole("button", { name: "Write", exact: true }).click();
  const editor = page.getByRole("textbox", { name: "Markdown editor" });
  await editor.click();
  await page.keyboard.press("Control+End");
  await page.keyboard.type("\n\nAcceptance marker: persistence.");
  await page.getByRole("button", { name: "Save note", exact: true }).click();
  await expect(page.getByText("Saved", { exact: true })).toBeVisible();
  expect(state.notes.get(first)?.markdown).toContain("Acceptance marker");
  await page.getByRole("button", { name: "Read", exact: true }).click();
  await expect(
    page.getByRole("article").getByText("Acceptance marker: persistence."),
  ).toBeVisible();
  await page.getByRole("button", { name: "Search your library" }).click();
  await page.getByLabel("Quick search").fill("persistence");
  await page
    .getByRole("listitem")
    .filter({ hasText: "Library architecture" })
    .click();
  await expect(
    page.getByRole("tab", { name: "Library architecture" }),
  ).toHaveCount(1);
  await page.getByRole("button", { name: "History", exact: true }).click();
  await page
    .getByRole("button", { name: /Document library structure/ })
    .click();
  await expect(
    page.getByRole("heading", { name: "Historical version" }),
  ).toBeVisible();
  await expect(
    page.getByRole("heading", { name: /Earlier version/ }),
  ).toBeVisible();
});
test("conflicting save preserves local text and offers review", async ({
  page,
}) => {
  const state = await mockSlate(page);
  await connect(page);
  await openNote(page);
  await page.getByRole("button", { name: "Write", exact: true }).click();
  await page.getByRole("textbox", { name: "Markdown editor" }).click();
  await page.keyboard.press("Control+End");
  await page.keyboard.type("\nLocal edit to preserve");
  state.setConflict(true);
  await page.getByRole("button", { name: "Save note" }).click();
  await expect(
    page.getByRole("alert").filter({ hasText: "changed elsewhere" }),
  ).toContainText("changed elsewhere");
  await expect(
    page.getByRole("textbox", { name: "Markdown editor" }),
  ).toContainText("Local edit to preserve");
  await page.getByRole("button", { name: "Review latest" }).click();
  await expect(
    page.getByRole("heading", { name: "Review both versions" }),
  ).toBeVisible();
  expect(state.notes.get(first)?.markdown).not.toContain(
    "Local edit to preserve",
  );
});
test("unsaved draft survives reload and is explicitly recovered", async ({
  page,
}) => {
  await mockSlate(page);
  await connect(page);
  await openNote(page);
  await page.getByRole("button", { name: "Write", exact: true }).click();
  await page.getByRole("textbox", { name: "Markdown editor" }).click();
  await page.keyboard.press("Control+End");
  await page.keyboard.type("\nRecover this browser draft");
  await expect(
    page.getByText("Unsaved changes", { exact: true }),
  ).toBeVisible();
  page.on("dialog", (d) => void d.accept());
  await page.reload();
  await page.getByRole("button", { name: "Continue editing draft" }).click();
  await page.getByRole("textbox", { name: "Markdown editor" }).click();
  await page.keyboard.press("Control+End");
  await expect(
    page.getByRole("textbox", { name: "Markdown editor" }),
  ).toContainText("Recover this browser draft");
});
test("capture, move from Inbox, rename, duplicate and explicit deletion", async ({
  page,
}) => {
  const state = await mockSlate(page);
  await connect(page);
  await page
    .getByRole("button", { name: "Quick Thought", exact: true })
    .first()
    .click();
  await page.getByLabel("Title optional").fill("Field observation");
  await page
    .getByLabel("Your thought")
    .fill("Check this idea after the next reading session.");
  await page.getByRole("button", { name: "Save to Inbox" }).click();
  await expect(
    page
      .getByRole("heading", { name: "Field observation", exact: true })
      .first(),
  ).toBeVisible();
  const capture = Array.from(state.notes.values()).find(
    (n) => n.title === "Field observation",
  )!;
  await page
    .getByRole("button", {
      name: `Actions for ${capture.path.split("/").at(-1)}`,
    })
    .first()
    .click();
  await page.getByRole("menuitem", { name: "Move to…" }).click();
  await page.getByLabel("Destination folder", { exact: true }).fill("Projects");
  await page.getByRole("button", { name: "Confirm destination" }).click();
  await expect
    .poll(() => state.notes.get(capture.id)?.path)
    .toContain("Projects/");
  await page
    .getByRole("button", {
      name: `Actions for ${capture.path.split("/").at(-1)}`,
    })
    .first()
    .click();
  await page.getByRole("menuitem", { name: "Rename", exact: true }).click();
  await page.getByLabel("Name", { exact: true }).fill("Observation.md");
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Observation", exact: true }).first(),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "Actions for Observation.md" })
    .first()
    .click();
  await page.getByRole("menuitem", { name: "Duplicate", exact: true }).click();
  await page.getByRole("button", { name: "Create duplicate" }).click();
  await expect.poll(() => state.notes.size).toBe(4);
  await page
    .getByRole("button", { name: "Actions for Observation.md" })
    .first()
    .click();
  await page.getByRole("menuitem", { name: "Delete", exact: true }).click();
  await expect(page.getByText(/This removes the item/)).toBeVisible();
  await page.getByRole("button", { name: "Delete", exact: true }).click();
  await expect.poll(() => state.notes.has(capture.id)).toBe(false);
});
test("attachments upload, backlinks navigate, and sync runs", async ({
  page,
}) => {
  const state = await mockSlate(page);
  await connect(page);
  await openNote(page);
  await page.getByLabel("Upload attachments").setInputFiles({
    name: "reference.txt",
    mimeType: "text/plain",
    buffer: Buffer.from("A reference document"),
  });
  await expect.poll(() => state.uploads()).toBe(1);
  await page.getByRole("textbox", { name: "Markdown editor" }).click();
  await page.keyboard.press("Control+End");
  await expect(
    page.getByRole("textbox", { name: "Markdown editor" }),
  ).toContainText(".assets/");
  await page.getByRole("button", { name: "Save note" }).click();
  await page.getByRole("button", { name: "Read", exact: true }).click();
  await expect(
    page.getByRole("button", { name: /reference.txt/ }),
  ).toBeVisible();
  await page
    .getByRole("button", {
      name: "Reading list Research/Reading list.md",
      exact: true,
    })
    .first()
    .click();
  await expect(page.getByRole("tab", { name: "Reading list" })).toBeVisible();
  expect(page.url()).toContain(second);
  await page.getByRole("button", { name: "Sync library", exact: true }).click();
  await expect
    .poll(() => state.requests.some((r) => r.path === "v1/sync"))
    .toBe(true);
});
test("light defaults, dark preference persists, and recent opens the note", async ({
  page,
}) => {
  await mockSlate(page);
  await connect(page);
  await expect(page.locator("html")).toHaveClass(/light/);
  await openNote(page);
  await page.getByRole("button", { name: "Change theme" }).click();
  await expect(page.locator("html")).toHaveClass(/dark/);
  await page.reload();
  await expect(page.locator("html")).toHaveClass(/dark/);
  await page
    .getByRole("button", { name: "Recent", exact: true })
    .first()
    .click();
  await page
    .getByRole("button")
    .filter({ hasText: "Library architectureProjects/Library architecture.md" })
    .click();
  await expect(
    page.getByRole("tab", { name: "Library architecture" }),
  ).toBeVisible();
});
for (const [width, height] of [
  [390, 844],
  [412, 915],
  [768, 1024],
  [1366, 768],
  [1440, 900],
  [1920, 1080],
]) {
  test(`responsive layout ${width}x${height}`, async ({ page }) => {
    await page.setViewportSize({ width, height });
    await mockSlate(page);
    await connect(page);
    await openNote(page);
    await expect(
      page
        .getByRole("heading", { name: "Library architecture", exact: true })
        .first(),
    ).toBeVisible();
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBe(true);
    if (width < 768) {
      await expect(
        page.getByRole("navigation", { name: "Main navigation" }),
      ).toBeVisible();
      await page.getByRole("button", { name: "Toggle details" }).click();
      await expect(page.getByRole("dialog")).toBeVisible();
      await page.getByRole("button", { name: "Close dialog" }).click();
    }
    await expect(page.getByRole("article")).toBeVisible();
    await page.screenshot({
      path: `output/playwright/workspace-${width}.png`,
      fullPage: true,
    });
  });
}
test("small-screen capture and search work without desktop panes", async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockSlate(page);
  await connect(page);
  await page
    .getByRole("button", { name: "Quick Thought", exact: true })
    .click();
  await page.getByLabel("Your thought").fill("Mobile capture text");
  await page.getByRole("button", { name: "Save to Inbox" }).click();
  await expect(
    page.getByRole("heading", { name: "Quick Thought", exact: true }).first(),
  ).toBeVisible();
  await page.getByRole("button", { name: "Search", exact: true }).click();
  await page.getByLabel("Search library", { exact: true }).fill("distributed");
  await page.getByRole("listitem").filter({ hasText: "Reading list" }).click();
  await expect(
    page.getByRole("heading", { name: "Reading list", exact: true }).first(),
  ).toBeVisible();
});
test("optional real development server: authenticated status and root listing", async ({
  request,
}) => {
  test.skip(
    !process.env.SLATE_TEST_SERVER || !process.env.SLATE_TEST_TOKEN,
    "Set explicit development-server credentials for this read-only integration check.",
  );
  const server = process.env.SLATE_TEST_SERVER!.replace(/\/$/, "");
  const headers = { Authorization: `Bearer ${process.env.SLATE_TEST_TOKEN}` };
  const status = await request.get(server + "/v1/status", { headers });
  expect(status.ok()).toBe(true);
  const root = await request.get(server + "/v1/library?path=&page=0", {
    headers,
  });
  expect(root.ok()).toBe(true);
  expect((await root.json()).entries).toBeInstanceOf(Array);
});

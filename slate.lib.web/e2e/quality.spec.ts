import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mockSlate, connect, openNote, first } from "./fixture";

test("accessible connection, reader, dark theme and settings", async ({
  page,
}) => {
  await mockSlate(page);
  await page.goto("/");
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
  await connect(page);
  await openNote(page);
  await expect(page.getByRole("article")).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
  await page.getByRole("button", { name: "Change theme" }).click();
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
  await page.screenshot({ path: "output/playwright/workspace-dark.png" });
  await page.getByRole("button", { name: "Settings", exact: true }).click();
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
  await page.screenshot({ path: "output/playwright/settings-dark.png" });
});

test("mobile drawer opens a note, editing saves, every formatting command remains reachable", async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const state = await mockSlate(page);
  await connect(page);
  await page.getByRole("button", { name: "Toggle library" }).click();
  const sheet = page.getByRole("dialog");
  await sheet.getByRole("treeitem", { name: "Projects", exact: true }).click();
  await sheet
    .getByRole("treeitem", { name: "Library architecture", exact: true })
    .click();
  await expect(sheet).toHaveCount(0);
  await page.getByRole("button", { name: "Write", exact: true }).click();
  await page.getByRole("textbox", { name: "Markdown editor" }).click();
  await page.keyboard.press("Control+End");
  await page.keyboard.type("\nMobile save verification");
  await page.getByRole("button", { name: "Wiki link", exact: true }).click();
  await page.getByRole("button", { name: "Save note", exact: true }).click();
  await expect
    .poll(() => state.notes.get(first)?.markdown)
    .toContain("Mobile save verification");
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
  await page.screenshot({ path: "output/playwright/mobile-editor.png" });
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
});

test("revoked token preserves an edit and gives a reconnect message", async ({
  page,
}) => {
  await mockSlate(page);
  await connect(page);
  await openNote(page);
  await page.getByRole("button", { name: "Write", exact: true }).click();
  await page.getByRole("textbox", { name: "Markdown editor" }).click();
  await page.keyboard.press("Control+End");
  await page.keyboard.type("\nNever lose this text");
  await page.route(`**/api/slate/v1/notes/${first}`, (route) =>
    route.request().method() === "PUT"
      ? route.fulfill({ status: 401 })
      : route.fallback(),
  );
  await page.getByRole("button", { name: "Save note", exact: true }).click();
  await expect(
    page.getByRole("alert").filter({ hasText: "revoked" }),
  ).toBeVisible();
  await expect(
    page.getByRole("textbox", { name: "Markdown editor" }),
  ).toContainText("Never lose this text");
});

test("raw HTML cannot execute; math and diagrams load on demand", async ({
  page,
}) => {
  const state = await mockSlate(page);
  const note = state.notes.get(first)!;
  note.markdown =
    '# Library architecture\n\n<script>window.slateInjected=true</script>\n\n<img src=x onerror="window.slateInjected=true">\n\n[Unsafe](javascript:alert(1))\n\nMath: $x^2$\n\n```mermaid\ngraph LR\nA[Notes] --> B[Links]\n```';
  await connect(page);
  await openNote(page);
  await expect(page.locator(".katex")).toBeVisible();
  await expect(page.locator(".diagram svg")).toBeVisible();
  expect(
    await page.evaluate(() => Object.hasOwn(window, "slateInjected")),
  ).toBe(false);
  await expect(
    page.locator('article script, article a[href^="javascript:"]'),
  ).toHaveCount(0);
});

test("clipboard images upload authenticated bytes and insert canonical asset links", async ({
  page,
}) => {
  const state = await mockSlate(page);
  await connect(page);
  await openNote(page);
  await page.getByRole("button", { name: "Write", exact: true }).click();
  const editor = page.getByRole("textbox", { name: "Markdown editor" });
  await editor.click();
  await page.keyboard.press("Control+End");
  await editor.evaluate((element) => {
    const transfer = new DataTransfer();
    transfer.items.add(
      new File([new Uint8Array([137, 80, 78, 71])], "pasted.png", {
        type: "image/png",
      }),
    );
    element.dispatchEvent(
      new ClipboardEvent("paste", {
        bubbles: true,
        cancelable: true,
        clipboardData: transfer,
      }),
    );
  });
  await expect.poll(() => state.uploads()).toBe(1);
  await expect(editor).toContainText(".assets/");
});

test("new folder and note use server operations and refresh the tree", async ({
  page,
}) => {
  const state = await mockSlate(page);
  await connect(page);
  await page.getByRole("button", { name: "New folder", exact: true }).click();
  await page.getByLabel("Name", { exact: true }).fill("Work");
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(
    page.getByRole("treeitem", { name: "Work", exact: true }).first(),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "New note", exact: true })
    .first()
    .click();
  await page.getByLabel("Name", { exact: true }).fill("Design decisions");
  await page.getByLabel("Destination folder", { exact: true }).fill("Work");
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Design decisions", exact: true }),
  ).toBeVisible();
  expect(
    Array.from(state.notes.values()).some(
      (note) => note.path === "Work/Design decisions.md",
    ),
  ).toBe(true);
});

test("capture retries retain the exact identity, timestamp and submitted text", async ({
  page,
}) => {
  const state = await mockSlate(page);
  const attempts: unknown[] = [];
  await page.route("**/api/slate/v1/captures", async (route) => {
    attempts.push(route.request().postDataJSON());
    if (attempts.length === 1) await route.fulfill({ status: 503 });
    else await route.fallback();
  });
  await connect(page);
  await page
    .getByRole("button", { name: "Quick Thought", exact: true })
    .first()
    .click();
  await page.getByLabel("Your thought").fill("A durable capture");
  await page.getByRole("button", { name: "Save to Inbox" }).click();
  await expect(page.getByLabel("Your thought")).toBeDisabled();
  await page.getByRole("button", { name: "Retry same capture" }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  expect(attempts).toHaveLength(2);
  expect(attempts[1]).toEqual(attempts[0]);
  expect(state.notes.size).toBe(3);
});

test("a failed background refresh keeps the open editor and its unsaved text", async ({
  page,
}) => {
  await mockSlate(page);
  await connect(page);
  await openNote(page);
  await page.getByRole("button", { name: "Write", exact: true }).click();
  const editor = page.getByRole("textbox", { name: "Markdown editor" });
  await editor.click();
  await page.keyboard.press("Control+End");
  await page.keyboard.type("\nKeep this editor open");
  await page.route(`**/api/slate/v1/notes/${first}`, (route) =>
    route.request().method() === "GET"
      ? route.fulfill({ status: 503 })
      : route.fallback(),
  );
  await page.getByRole("button", { name: "Sync library", exact: true }).click();
  await expect(
    page.getByRole("alert").filter({ hasText: "could not complete" }),
  ).toBeVisible();
  await expect(editor).toContainText("Keep this editor open");
});

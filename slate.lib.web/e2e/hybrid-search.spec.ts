import { test, expect } from "@playwright/test";
import { connect, first, mockSlate, openNote } from "./fixture";

async function openSearch(page: import("@playwright/test").Page) {
  await page.getByRole("button", { name: "Search", exact: true }).first().click();
  await expect(page.getByRole("heading", { name: "Search", exact: true })).toBeVisible();
}

test("keyword mode returns an exact title match", async ({ page }) => {
  await mockSlate(page); await connect(page); await openSearch(page);
  await page.getByRole("radio", { name: "Keyword" }).click();
  await page.getByLabel("Search library", { exact: true }).fill("Library architecture");
  await expect(page.getByRole("listitem").filter({ hasText: "Library architecture" }).first()).toBeVisible();
});

test("meaning mode retrieves a paraphrase", async ({ page }) => {
  await mockSlate(page); await connect(page); await openSearch(page);
  await page.getByRole("radio", { name: "Meaning" }).click();
  await page.getByLabel("Search library", { exact: true }).fill("durable knowledge design");
  await expect(page.getByRole("listitem").filter({ hasText: "Meaning match" })).toContainText("Library architecture");
});

test("all mode keeps exact titles above meaning-only candidates", async ({ page }) => {
  await mockSlate(page); await connect(page); await openSearch(page);
  await page.getByLabel("Search library", { exact: true }).fill("Library architecture");
  await expect(page.getByRole("listitem").first()).toContainText("Library architecture");
});

test("metadata syntax remains directly editable in meaning mode", async ({ page }) => {
  await mockSlate(page); await connect(page); await openSearch(page);
  await page.getByRole("radio", { name: "Meaning" }).click();
  const input = page.getByLabel("Search library", { exact: true });
  await input.fill('durable path:"Projects" type:reference');
  await expect(input).toHaveValue('durable path:"Projects" type:reference');
  await expect(page.getByRole("listitem").filter({ hasText: "Library architecture" }).first()).toBeVisible();
});

test("a saved edit becomes searchable", async ({ page }) => {
  await mockSlate(page); await connect(page); await openNote(page);
  await page.getByRole("button", { name: "Write", exact: true }).click();
  const editor = page.getByRole("textbox", { name: "Markdown editor" });
  await editor.click(); await page.keyboard.press("Control+End"); await page.keyboard.insertText("\nFreshness marker");
  await page.getByRole("button", { name: "Save note", exact: true }).click();
  await openSearch(page); await page.getByRole("radio", { name: "Keyword" }).click();
  await page.getByLabel("Search library", { exact: true }).fill("Freshness marker");
  await expect(page.getByRole("listitem").filter({ hasText: "Library architecture" })).toBeVisible();
});

test("renamed notes do not duplicate in search", async ({ page }) => {
  const state = await mockSlate(page); await connect(page);
  state.notes.set(first, { ...state.notes.get(first)!, title: "Architecture renamed", path: "Projects/Architecture renamed.md" });
  await openSearch(page); await page.getByRole("radio", { name: "Keyword" }).click();
  await page.getByLabel("Search library", { exact: true }).fill("Architecture renamed");
  await expect(page.getByRole("listitem").filter({ hasText: "Architecture renamed" })).toHaveCount(1);
});

test("deleted notes disappear from results", async ({ page }) => {
  const state = await mockSlate(page); await connect(page); state.notes.delete(first);
  await openSearch(page); await page.getByRole("radio", { name: "Keyword" }).click();
  await page.getByLabel("Search library", { exact: true }).fill("Make every change easy to review");
  await expect(page.getByText("No matching notes")).toBeVisible();
});

test("provider outage visibly falls back to keyword search", async ({ page }) => {
  await mockSlate(page);
  await page.route("**/api/intelligence/search", (route) => route.fulfill({ status: 503, contentType: "application/json", body: "{}" }));
  await connect(page); await openSearch(page);
  await page.getByLabel("Search library", { exact: true }).fill("Library architecture");
  await expect(page.getByRole("status")).toContainText("Keyword results are shown");
  await expect(page.getByRole("listitem").filter({ hasText: "Library architecture" }).first()).toBeVisible();
});

test("settings reports the derived index and confirms rebuild", async ({ page }) => {
  await mockSlate(page); await connect(page);
  await page.getByRole("button", { name: "Settings" }).first().click();
  await expect(page.getByText("fixture · fixture-v1")).toBeVisible();
  await page.getByRole("button", { name: "Rebuild derived index" }).click();
  await page.getByRole("button", { name: "Confirm rebuild" }).click();
  await expect(page.getByText("Ready", { exact: true })).toBeVisible();
});

test("command center free text includes semantic notes", async ({ page }) => {
  await mockSlate(page); await connect(page);
  await page.getByRole("button", { name: "Search your library" }).click();
  await page.getByLabel("Quick search for notes and commands").fill("durable knowledge design");
  await expect(page.getByRole("option").filter({ hasText: "Library architecture" })).toBeVisible();
});

test("command-only mode stays instant and does not call intelligence", async ({ page }) => {
  let calls = 0;
  await mockSlate(page);
  await page.route("**/api/intelligence/search", async (route) => { calls++; await route.continue(); });
  await connect(page); await page.getByRole("button", { name: "Search your library" }).click();
  await page.getByLabel("Quick search for notes and commands").fill("> settings");
  await expect(page.getByRole("option").filter({ hasText: "Settings" }).first()).toBeVisible();
  expect(calls).toBe(0);
});

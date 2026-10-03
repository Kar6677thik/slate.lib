import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { connect, first, mockSlate, openNote, scope, second } from "./fixture";

async function openCenter(page: import("@playwright/test").Page, commandOnly = false) {
  await page.keyboard.press(commandOnly ? "Control+Shift+P" : "Control+K");
  await expect(page.getByRole("combobox", { name: "Quick search for notes and commands" })).toBeVisible();
}

test("Ctrl+K opens blended results and restores focus when closed", async ({ page }) => {
  await mockSlate(page);
  await connect(page);
  const trigger = page.getByRole("button", { name: "Search your library" });
  await trigger.focus();
  await openCenter(page);
  await expect(page.getByRole("option", { name: /New note/ })).toBeVisible();
  expect((await new AxeBuilder({ page }).include("[role=dialog]").analyze()).violations).toEqual([]);
  await page.keyboard.press("Escape");
  await expect(trigger).toBeFocused();
});

test("normal text prioritizes server note search and Enter opens the active result", async ({ page }) => {
  await mockSlate(page);
  await connect(page);
  await openCenter(page);
  await page.getByRole("combobox").fill("distributed systems");
  await expect(page.getByRole("option", { name: /Reading list/ })).toBeVisible();
  await page.keyboard.press("End");
  await page.getByRole("option", { name: /Reading list/ }).hover();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: "Reading list", exact: true }).first()).toBeVisible();
});

test("Ctrl+Shift+P and the > prefix stay in command-only mode", async ({ page }) => {
  await mockSlate(page);
  await connect(page);
  await openCenter(page, true);
  await expect(page.getByText("Commands", { exact: true })).toBeVisible();
  await page.getByRole("combobox").fill("> library");
  await expect(page.getByRole("option", { name: /^Library/ })).toBeVisible();
  await expect(page.getByRole("option", { name: /Library architecture/ })).toHaveCount(0);
});

test("document commands are contextual and Save uses the same guarded editor action", async ({ page }) => {
  const state = await mockSlate(page);
  await connect(page);
  await openNote(page);
  await openCenter(page, true);
  await page.getByRole("combobox").fill("> save note");
  await expect(page.getByRole("option", { name: /^Save note/ })).toHaveCount(0);
  await page.keyboard.press("Escape");
  await page.getByRole("button", { name: "Write", exact: true }).click();
  const editor = page.getByRole("textbox", { name: "Markdown editor" });
  await editor.click();
  await page.keyboard.press("Control+End");
  await page.keyboard.insertText("\nCommand Center save marker");
  await openCenter(page, true);
  await page.getByRole("combobox").fill("> save note");
  await page.keyboard.press("Enter");
  await expect.poll(() => state.notes.get(first)?.markdown).toContain("Command Center save marker");
});

test("document actions open existing favorite, details, and delete safeguards", async ({ page }) => {
  await mockSlate(page);
  await connect(page);
  await openNote(page);
  await openCenter(page, true);
  await page.getByRole("combobox").fill("> add to favorites");
  await page.keyboard.press("Enter");
  await openCenter(page, true);
  await page.getByRole("combobox").fill("> open version history");
  await page.keyboard.press("Enter");
  await expect(page.getByRole("button", { name: /Document library structure/ })).toBeVisible();
  await openCenter(page, true);
  await page.getByRole("combobox").fill("> delete note");
  await page.keyboard.press("Enter");
  await expect(page.getByText(/This removes the item/)).toBeVisible();
  await expect(page.getByRole("button", { name: "Delete", exact: true })).toBeVisible();
});

test("workspace shortcuts switch and close tabs while editor Ctrl+K remains Markdown link", async ({ page }) => {
  await mockSlate(page);
  await connect(page);
  await openNote(page);
  await page.getByRole("button", { name: "Search your library" }).click();
  await page.getByRole("combobox").fill("Reading list");
  await page.getByRole("option", { name: /Reading list/ }).click();
  await expect(page.getByRole("tab", { name: "Reading list" })).toBeVisible();
  await page.keyboard.press("Control+Shift+Tab");
  expect(page.url()).toContain(first);
  await page.keyboard.press("Control+Tab");
  expect(page.url()).toContain(second);
  await page.keyboard.press("Control+W");
  await expect(page.getByRole("tab", { name: "Reading list" })).toHaveCount(0);
  await page.getByRole("button", { name: "Write", exact: true }).click();
  await page.getByRole("textbox", { name: "Markdown editor" }).click();
  await page.keyboard.press("Control+K");
  await expect(page.getByRole("combobox")).toHaveCount(0);
  await page.keyboard.press("Control+Shift+P");
  await expect(page.getByRole("combobox")).toBeVisible();
});

test("saved searches, smart views, Favorites, and create actions are reachable", async ({ page }) => {
  await mockSlate(page);
  await connect(page);
  await page.evaluate(({ scope }) => {
    localStorage.setItem(`slate.workspace-preferences.${scope}`, JSON.stringify({
      schemaVersion: 1,
      favorites: [],
      pins: [{ path: "Projects", label: "Projects" }],
      searches: [{ id: "saved-1", name: "Architecture search", query: "architecture" }],
    }));
  }, { scope });
  await page.reload();
  await page.getByRole("heading", { name: "Library", exact: true }).waitFor();
  await openCenter(page);
  await page.getByRole("combobox").fill("Architecture search");
  await page.keyboard.press("Enter");
  await expect(page.getByLabel("Search library", { exact: true })).toHaveValue("architecture");
  await openCenter(page, true);
  await page.getByRole("combobox").fill("> unanswered questions");
  await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: "Unanswered questions" })).toBeVisible();
  await openCenter(page, true);
  await page.getByRole("combobox").fill("> new folder");
  await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: "New folder" })).toBeVisible();
});

test("mobile uses a large touch-friendly bottom sheet", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockSlate(page);
  await connect(page);
  await openCenter(page);
  const dialog = page.getByRole("dialog");
  const box = await dialog.boundingBox();
  expect(box).not.toBeNull();
  expect(box!.width).toBeGreaterThan(380);
  expect(box!.y).toBeGreaterThan(50);
  await expect(page.getByRole("option", { name: /Quick Thought/ })).toBeVisible();
});

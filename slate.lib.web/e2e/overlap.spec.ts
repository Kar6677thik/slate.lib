import { test, expect, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { connect, mockSlate } from "./fixture";

async function openOverlap(page: Page, generationEnabled = true) {
  await mockSlate(page, { generationEnabled }); await connect(page);
  await page.keyboard.press("Control+Shift+P"); await page.getByRole("combobox").fill("> knowledge overlap"); await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: "Knowledge Overlap" })).toBeVisible();
}
async function compareFirst(page: Page) {
  await page.getByRole("button", { name: /Library architecture and Reading list/ }).first().click();
  return page.getByRole("complementary", { name: "Knowledge overlap comparison" });
}

test("opens Knowledge Overlap from Command Center with exact, near, and partial findings", async ({ page }) => {
  await openOverlap(page);
  await expect(page.getByText("Exact duplicate", { exact: true })).toBeVisible();
  await expect(page.getByText("Near duplicate", { exact: true })).toBeVisible();
  await expect(page.getByText("Partial overlap", { exact: true })).toBeVisible();
});

test("opens Note A and Note B from comparison", async ({ page }) => {
  await openOverlap(page); let comparison = await compareFirst(page);
  await comparison.getByRole("button", { name: "Open full note" }).first().click();
  await expect(page.getByRole("heading", { name: "Library architecture", exact: true }).first()).toBeVisible();
  await page.getByRole("button", { name: "Knowledge Overlap", exact: true }).first().click(); comparison = await compareFirst(page);
  await comparison.getByRole("button", { name: "Open full note" }).nth(1).click();
  await expect(page.getByRole("heading", { name: "Reading list", exact: true }).first()).toBeVisible();
});

test("comparison exposes shared and unique knowledge modes", async ({ page }) => {
  await openOverlap(page); const comparison = await compareFirst(page);
  await expect(comparison.getByRole("region", { name: "Note A: Library architecture" })).toBeVisible();
  await expect(comparison.getByRole("region", { name: "Note B: Reading list" })).toBeVisible();
  await comparison.getByRole("tab", { name: "Shared" }).click(); await expect(comparison.getByRole("region", { name: "Shared knowledge" })).toBeVisible();
  await comparison.getByRole("tab", { name: "Only A" }).click(); await expect(comparison.getByRole("region", { name: "Unique to A" })).toBeVisible();
  await comparison.getByRole("tab", { name: "Only B" }).click(); await expect(comparison.getByRole("region", { name: "Unique to B" })).toBeVisible();
});

test("Keep Separate persists after reload", async ({ page }) => {
  await openOverlap(page); const comparison = await compareFirst(page); await comparison.getByRole("button", { name: "Keep separate" }).click();
  await page.reload(); await expect(page.getByText("Exact duplicate", { exact: true })).toHaveCount(0);
  await page.getByText("Review state").locator("..").getByRole("combobox").selectOption("keep-separate"); await expect(page.getByText("Exact duplicate", { exact: true })).toBeVisible();
});

test("manual merge preview creates only a browser-local editable draft", async ({ page }) => {
  await openOverlap(page); const comparison = await compareFirst(page); await comparison.getByText("Manual merge preview").click();
  await comparison.getByRole("button", { name: "Create manual merge draft" }).click();
  await expect(comparison.getByText("This draft has not been saved to either note.")).toBeVisible();
  await expect(comparison.getByRole("textbox")).toHaveValue(/Current decision/);
});

test("possibly absorbed finding opens Evolution", async ({ page }) => {
  await openOverlap(page); await page.getByText("Relationship").locator("..").getByRole("combobox").selectOption("possibly-absorbed");
  const comparison = await compareFirst(page); await comparison.getByRole("button", { name: "Evolution" }).click();
  await expect(page.getByText("Evolution of Thought", { exact: true })).toBeVisible();
});

test("Inbox shows Similar knowledge indicator and opens comparison workspace", async ({ page }) => {
  await mockSlate(page); await connect(page); await page.getByRole("button", { name: "Inbox", exact: true }).first().click();
  await expect(page.getByText("Similar knowledge exists", { exact: true })).toBeVisible(); await page.getByText("Similar knowledge exists", { exact: true }).click();
  await expect(page.getByRole("heading", { name: "Knowledge Overlap" })).toBeVisible(); await compareFirst(page);
});

test("Ask Slate is scoped to the selected pair", async ({ page }) => {
  await openOverlap(page); const comparison = await compareFirst(page); await comparison.getByRole("button", { name: "Ask" }).click();
  await expect(page.getByLabel("Question", { exact: true })).toHaveValue(/How are these notes different/);
});

test("generation-disabled mode retains deterministic overlap", async ({ page }) => {
  await openOverlap(page, false); await expect(page.getByText(/Deterministic overlap analysis is active/)).toBeVisible(); await expect(page.getByText("Exact duplicate", { exact: true })).toBeVisible();
});

test("Project Brain includes project-scoped overlap", async ({ page }) => {
  await mockSlate(page); await connect(page); await page.getByRole("button", { name: "Actions for Projects" }).first().click(); await page.getByRole("menuitem", { name: "Open Project Brain" }).click();
  await expect(page.getByRole("heading", { name: "Overlap / Consolidation" })).toBeVisible(); await page.getByRole("button", { name: "Review overlap" }).click(); await expect(page.getByRole("heading", { name: "Knowledge Overlap" })).toBeVisible();
});

test("mobile list and comparison stack without overflow and pass axe", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 }); await openOverlap(page); const comparison = await compareFirst(page); await comparison.getByRole("tab", { name: "Shared" }).click();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect((await new AxeBuilder({ page }).include(".overlap-workspace").analyze()).violations).toEqual([]);
});

test("desktop overlap workspace passes axe", async ({ page }) => {
  await openOverlap(page); expect((await new AxeBuilder({ page }).include(".overlap-workspace").analyze()).violations).toEqual([]);
});

import { test, expect, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { connect, mockSlate, openNote } from "./fixture";

async function openHealth(page: Page, options: { generationEnabled?: boolean; healthPartialFailure?: boolean } = {}) {
  await mockSlate(page, options); await connect(page);
  await page.keyboard.press("Control+Shift+P");
  await page.getByRole("combobox").fill("> library health");
  await page.getByRole("option").filter({ hasText: "Library Health" }).first().click();
  await expect(page.getByRole("heading", { name: "Library Health", exact: true })).toBeVisible();
}
function details(page: Page) { return page.getByRole("complementary", { name: "Maintenance item details" }); }
function findings(page: Page) { return page.locator(".health-list"); }
async function select(page: Page, name: RegExp) { await findings(page).getByRole("button", { name }).first().click(); await expect(details(page)).toBeVisible(); }

test("opens Library Health from Command Center", async ({ page }) => { await openHealth(page); });
test("shows a compact priority summary", async ({ page }) => { await openHealth(page); await expect(page.getByLabel("Library Health summary").getByText("Needs attention")).toBeVisible(); await expect(page.getByLabel("Library Health summary").getByText("Worth reviewing")).toBeVisible(); });
test("shows needs-attention findings before lower priorities", async ({ page }) => { await openHealth(page); const headings = page.locator(".health-list section > h2"); await expect(headings.first()).toContainText("Needs attention"); });
test("routes a broken link to canonical Link Health", async ({ page }) => { await openHealth(page); await select(page, /Broken link/); await details(page).getByRole("button", { name: /Review/ }).click(); await expect(page.getByRole("heading", { name: "Link health" })).toBeVisible(); });
test("routes a contradiction to Knowledge Issues", async ({ page }) => { await openHealth(page); await select(page, /Current contradiction/); await details(page).getByRole("button", { name: /Review/ }).click(); await expect(page.getByRole("heading", { name: "Knowledge Issues" })).toBeVisible(); });
test("routes a duplicate to Knowledge Overlap", async ({ page }) => { await openHealth(page); await select(page, /Exact duplicate/); await details(page).getByRole("button", { name: /Review/ }).click(); await expect(page.getByRole("heading", { name: "Knowledge Overlap" })).toBeVisible(); });
test("routes a missing relationship to Link Opportunities", async ({ page }) => { await openHealth(page); await select(page, /Missing relationship/); await details(page).getByRole("button", { name: /Review/ }).click(); await expect(page.getByRole("heading", { name: "Link Opportunities" })).toBeVisible(); });
test("surfaces a possible answered question", async ({ page }) => { await openHealth(page); await expect(page.getByRole("button", { name: /Possible answer found/ })).toBeVisible(); });
test("surfaces an orphan note as informational", async ({ page }) => { await openHealth(page); await expect(findings(page).getByRole("button", { name: /Informational.*Orphan note/i })).toBeVisible(); });
test("surfaces asset maintenance", async ({ page }) => { await openHealth(page); await expect(page.getByRole("button", { name: /Unreferenced asset/ })).toBeVisible(); });
test("surfaces persistent intelligence failures", async ({ page }) => { await openHealth(page); await expect(page.getByRole("button", { name: /Indexing needs attention/ })).toBeVisible(); });
test("filters by project", async ({ page }) => { await openHealth(page); await page.getByLabel("Health project").selectOption("Research"); await expect(findings(page).getByRole("button", { name: /Orphan note/ })).toBeVisible(); await expect(findings(page).getByRole("button", { name: /Broken link/ })).toHaveCount(0); });
test("filters by concept", async ({ page }) => { await openHealth(page); await page.getByLabel("Health concept").selectOption("concept-slate"); await expect(findings(page).getByRole("button", { name: /Concept identity needs review/ })).toBeVisible(); await expect(findings(page).getByRole("button", { name: /Orphan note/ })).toHaveCount(0); });
test("filters by category", async ({ page }) => { await openHealth(page); await page.getByLabel("Health category").selectOption("assets"); await expect(page.getByRole("button", { name: /Unreferenced asset/ })).toBeVisible(); await expect(page.getByRole("button", { name: /Broken link/ })).toHaveCount(0); });
test("Review next opens a focused maintenance detail", async ({ page }) => { await openHealth(page); await page.getByRole("button", { name: "Review next" }).click(); await expect(details(page).getByRole("heading", { level: 2 })).toBeVisible(); await expect(details(page).getByRole("button", { name: "Next" })).toBeVisible(); });
test("dismisses a health-only finding without editing a note", async ({ page }) => { await openHealth(page); await select(page, /Orphan note/); await details(page).getByRole("button", { name: "Dismiss", exact: true }).click(); await expect(findings(page).getByRole("button", { name: /Orphan note/ })).toHaveCount(0); });
test("an underlying resolved issue disappears from the open health queue", async ({ page }) => {
  await openHealth(page); await select(page, /Current contradiction/); await details(page).getByRole("button", { name: /Review/ }).click();
  await page.getByRole("button", { name: /Compare Library architecture and Reading list/ }).first().click(); await page.getByRole("button", { name: "Resolve", exact: true }).click();
  await page.keyboard.press("Control+Shift+P"); await page.getByRole("combobox").fill("> library health"); await page.getByRole("option").filter({ hasText: "Library Health" }).first().click();
  await expect(page.getByRole("button", { name: /Current contradiction/ })).toHaveCount(0);
});
test("Project Brain includes a scoped maintenance summary", async ({ page }) => { await mockSlate(page); await connect(page); await page.getByRole("button", { name: "Actions for Projects" }).first().click(); await page.getByRole("menuitem", { name: "Open Project Brain" }).click(); await expect(page.getByRole("heading", { name: "Library Health / Maintenance" })).toBeVisible(); await expect(page.getByRole("button", { name: /worth reviewing in this project/ })).toBeVisible(); });
test("current-note details include scoped maintenance", async ({ page }) => { await mockSlate(page); await connect(page); await openNote(page); await page.getByRole("button", { name: "Info" }).click(); await expect(page.getByRole("heading", { name: /Maintenance/ })).toBeVisible(); });
test("works when optional generation is disabled", async ({ page }) => { await openHealth(page, { generationEnabled: false }); await expect(page.getByRole("button", { name: /Broken link/ })).toBeVisible(); });
test("shows partial results when a source fails", async ({ page }) => { await openHealth(page, { healthPartialFailure: true }); await expect(page.getByRole("status")).toContainText("Unavailable sources: assets"); await expect(page.getByRole("button", { name: /Broken link/ })).toBeVisible(); });
test("mobile health list has no horizontal overflow", async ({ page }) => { await page.setViewportSize({ width: 390, height: 844 }); await openHealth(page); await expect(page.getByRole("button", { name: /Broken link/ })).toBeVisible(); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true); });
test("mobile Review next opens a full-height detail", async ({ page }) => { await page.setViewportSize({ width: 390, height: 844 }); await openHealth(page); await page.getByRole("button", { name: "Review next" }).click(); await expect(details(page)).toBeVisible(); await expect(details(page).getByRole("button", { name: "Close maintenance details" })).toBeVisible(); });
test("desktop Library Health passes axe", async ({ page }) => { await openHealth(page); expect((await new AxeBuilder({ page }).include(".health-workspace").analyze()).violations).toEqual([]); });
test("mobile Library Health passes axe", async ({ page }) => { await page.setViewportSize({ width: 390, height: 844 }); await openHealth(page); expect((await new AxeBuilder({ page }).include(".health-workspace").analyze()).violations).toEqual([]); });

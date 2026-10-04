import { test, expect, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { connect, mockSlate } from "./fixture";

async function openIssues(page: Page, generationEnabled = true) {
  await mockSlate(page, { generationEnabled }); await connect(page);
  await page.keyboard.press("Control+Shift+P");
  await page.getByRole("combobox").fill("> knowledge issues");
  await page.keyboard.press("Enter");
  await expect(page.getByRole("heading", { name: "Knowledge Issues" })).toBeVisible();
}

test("opens Knowledge Issues from Command Center and shows a current contradiction", async ({ page }) => {
  await openIssues(page);
  await expect(page.getByText("Architecture conflict", { exact: true })).toBeVisible();
  await expect(page.getByLabel("Knowledge issue summary").getByText("2", { exact: true })).toBeVisible();
});

test("opens Source A and Source B", async ({ page }) => {
  await openIssues(page); await page.getByRole("button", { name: /Compare Library architecture and Reading list/ }).first().click();
  await page.getByRole("button", { name: "Open source" }).first().click();
  await expect(page.getByRole("heading", { name: "Library architecture", exact: true }).first()).toBeVisible();
  await page.getByRole("button", { name: "Knowledge Issues", exact: true }).first().click();
  await page.getByRole("button", { name: /Compare Library architecture and Reading list/ }).first().click();
  await page.getByRole("button", { name: "Open source" }).nth(1).click();
  await expect(page.getByRole("heading", { name: "Reading list", exact: true }).first()).toBeVisible();
});

test("side-by-side comparison labels both sources", async ({ page }) => {
  await openIssues(page); await page.getByRole("button", { name: /Compare Library architecture and Reading list/ }).first().click();
  const comparison = page.getByRole("complementary", { name: "Knowledge issue comparison" });
  await expect(comparison.getByRole("region", { name: /Source A/ })).toBeVisible();
  await expect(comparison.getByRole("region", { name: /Source B/ })).toBeVisible();
});

test("dismissed issue remains hidden after reload", async ({ page }) => {
  await openIssues(page); await page.getByRole("button", { name: /Compare Library architecture and Reading list/ }).first().click();
  await page.getByRole("button", { name: "Dismiss", exact: true }).click();
  await page.reload();
  await expect(page.getByText("Architecture conflict", { exact: true })).toHaveCount(0);
  await page.getByText("Review state").locator("..").getByRole("combobox").selectOption("dismissed");
  await expect(page.getByText("Architecture conflict", { exact: true })).toBeVisible();
});

test("resolves an issue", async ({ page }) => {
  await openIssues(page); await page.getByRole("button", { name: /Compare Library architecture and Reading list/ }).first().click();
  await page.getByRole("button", { name: "Resolve", exact: true }).click();
  await page.getByText("Review state").locator("..").getByRole("combobox").selectOption("resolved");
  await expect(page.getByText("Architecture conflict", { exact: true })).toBeVisible();
});

test("filters version conflicts, superseded notes, and possible answers", async ({ page }) => {
  await openIssues(page);
  const type = page.getByText("Type", { exact: true }).locator("..").getByRole("combobox");
  await type.selectOption("version"); await expect(page.getByText("Version conflict", { exact: true })).toBeVisible();
  await type.selectOption("supersession"); await expect(page.getByRole("button", { name: /Compare Library architecture and Reading list/ })).toBeVisible();
  await type.selectOption("answered-question"); await expect(page.getByRole("button", { name: /Compare Library architecture and Reading list/ })).toBeVisible();
});

test("opens Evolution and Ask Slate from an issue", async ({ page }) => {
  await openIssues(page); await page.getByRole("button", { name: /Compare Library architecture and Reading list/ }).first().click();
  await page.getByRole("complementary", { name: "Knowledge issue comparison" }).getByRole("button", { name: "Evolution", exact: true }).click();
  await expect(page.getByText("Evolution of Thought", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Knowledge Issues", exact: true }).first().click();
  await page.getByRole("button", { name: /Compare Library architecture and Reading list/ }).first().click();
  await page.getByRole("complementary", { name: "Knowledge issue comparison" }).getByRole("button", { name: "Ask", exact: true }).click();
  await expect(page.getByLabel("Question", { exact: true })).toHaveValue(/Explain this knowledge issue/);
});

test("Project Brain includes Knowledge Issues", async ({ page }) => {
  await mockSlate(page); await connect(page);
  await page.getByRole("button", { name: "Actions for Projects" }).first().click();
  await page.getByRole("menuitem", { name: "Open Project Brain" }).click();
  await expect(page.getByRole("heading", { name: "Knowledge Issues" })).toBeVisible();
  await page.getByRole("button", { name: "Review knowledge issues" }).click();
  await expect(page.getByRole("heading", { name: "Knowledge Issues" })).toBeVisible();
});

test("generation-disabled mode keeps deterministic findings", async ({ page }) => {
  await openIssues(page, false);
  await expect(page.getByText(/Deterministic analysis is active/)).toBeVisible();
  await expect(page.getByText("Architecture conflict", { exact: true })).toBeVisible();
});

test("mobile issue list and comparison are stacked and accessible", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 }); await openIssues(page);
  await page.getByRole("button", { name: /Compare Library architecture and Reading list/ }).first().click();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect((await new AxeBuilder({ page }).include(".knowledge-workspace").analyze()).violations).toEqual([]);
});

test("desktop Knowledge Issues passes axe", async ({ page }) => {
  await openIssues(page);
  expect((await new AxeBuilder({ page }).include(".knowledge-workspace").analyze()).violations).toEqual([]);
});

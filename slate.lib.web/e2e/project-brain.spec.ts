import { test, expect, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { connect, mockSlate } from "./fixture";

async function openProjectBrain(page: Page) {
  await page.getByRole("button", { name: "Actions for Projects" }).first().click();
  await page.getByRole("menuitem", { name: "Open Project Brain" }).click();
  await page.getByRole("heading", { name: "Projects", exact: true }).waitFor();
}

test("opens Project Brain from a folder context action", async ({ page }) => {
  await mockSlate(page); await connect(page); await openProjectBrain(page);
  await expect(page.getByText("Project Brain", { exact: true }).first()).toBeVisible();
  expect(page.url()).toContain("project=Projects");
});

test("loads deterministic and generated overview independently", async ({ page }) => {
  await mockSlate(page); await connect(page); await openProjectBrain(page);
  await expect(page.getByRole("heading", { name: "Overview sources" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Overview", exact: true })).toBeVisible();
  await expect(page.getByText(/private Markdown knowledge workspace/)).toBeVisible();
});

test("shows fixture open questions", async ({ page }) => {
  await mockSlate(page); await connect(page); await openProjectBrain(page);
  await expect(page.getByRole("heading", { name: "Open Questions" })).toBeVisible();
  await expect(page.getByRole("button", { name: /How should sync retry/ })).toBeVisible();
});

test("shows deterministic recent changes and time windows", async ({ page }) => {
  await mockSlate(page); await connect(page); await openProjectBrain(page);
  await expect(page.locator("#project-recent").getByText("Document library structure")).toBeVisible();
  await page.getByRole("button", { name: "All", exact: true }).click();
  await expect(page.locator("#project-recent").getByText("Document library structure")).toBeVisible();
});

test("decision evidence opens its canonical source", async ({ page }) => {
  await mockSlate(page); await connect(page); await openProjectBrain(page);
  await page.getByRole("button", { name: /Use Markdown storage/ }).click();
  await expect(page.getByRole("heading", { name: "Library architecture", exact: true }).first()).toBeVisible();
});

test("Resume Project produces a grounded context pack", async ({ page }) => {
  await mockSlate(page); await connect(page); await openProjectBrain(page);
  await page.getByRole("button", { name: "Resume Project", exact: true }).click();
  const resume = page.getByRole("complementary", { name: "Resume Project context pack" });
  await expect(resume.getByRole("heading", { name: "Where Things Stand" })).toBeVisible();
  await expect(resume.getByRole("heading", { name: "What to Read First" })).toBeVisible();
});

test("a Resume citation opens the correct source note", async ({ page }) => {
  await mockSlate(page); await connect(page); await openProjectBrain(page);
  await page.getByRole("button", { name: "Resume Project", exact: true }).click();
  const resume = page.getByRole("complementary", { name: "Resume Project context pack" });
  await resume.getByRole("link", { name: "S1" }).first().click();
  await expect(page.getByRole("heading", { name: "Library architecture", exact: true }).first()).toBeVisible();
});

test("Ask this project launches Ask Slate with project scope", async ({ page }) => {
  await mockSlate(page); await connect(page); await openProjectBrain(page);
  await page.getByRole("button", { name: "Ask this project", exact: true }).click();
  await expect(page.getByLabel("Ask scope")).toHaveValue("project");
  await expect(page.getByText("Project: Projects", { exact: true })).toBeVisible();
});

test("project search reuses search with a folder path filter", async ({ page }) => {
  await mockSlate(page); await connect(page); await openProjectBrain(page);
  await page.locator(".project-actions").getByRole("button", { name: "Search", exact: true }).click();
  await expect(page.getByLabel("Search library", { exact: true })).toHaveValue('path:"Projects" ');
});

test("timeline events navigate to their source note", async ({ page }) => {
  await mockSlate(page); await connect(page); await openProjectBrain(page);
  await page.locator("#project-timeline").getByText("Timeline").click();
  await page.locator("#project-timeline").getByRole("button", { name: /Document library structure/ }).click();
  await expect(page.getByRole("heading", { name: "Library architecture", exact: true }).first()).toBeVisible();
});

test("project graph has an accessible list fallback", async ({ page }) => {
  await mockSlate(page); await connect(page); await openProjectBrain(page);
  await page.locator("#project-graph").getByText("Project Graph").click();
  await expect(page.locator("#project-graph").getByText(/accessible list is the primary fallback/)).toBeVisible();
  await expect(page.locator("#project-graph").getByRole("button", { name: /Library architecture/ })).toBeVisible();
});

test("generation-disabled Project Brain remains useful", async ({ page }) => {
  await mockSlate(page, { generationEnabled: false }); await connect(page); await openProjectBrain(page);
  await expect(page.getByRole("heading", { name: "Open Questions" })).toBeVisible();
  await expect(page.locator(".project-ai-off")).toContainText("AI synthesis isn’t configured");
  await expect(page.getByRole("button", { name: "Resume Project", exact: true })).toBeDisabled();
});

test("mobile Project Brain stacks without horizontal overflow", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockSlate(page); await connect(page);
  await page.getByRole("button", { name: "Toggle library" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Actions for Projects" }).click();
  await page.getByRole("menuitem", { name: "Open Project Brain" }).click();
  await expect(page.getByRole("heading", { name: "Projects", exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test("mobile Resume Project uses a full-width context surface", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockSlate(page); await connect(page);
  await page.getByRole("button", { name: "Toggle library" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Actions for Projects" }).click();
  await page.getByRole("menuitem", { name: "Open Project Brain" }).click();
  await page.getByRole("button", { name: "Resume Project", exact: true }).click();
  await expect(page.getByRole("complementary", { name: "Resume Project context pack" })).toBeVisible();
  expect(await page.locator(".project-resume").evaluate((element) => Math.abs(element.getBoundingClientRect().width - innerWidth) < 2)).toBe(true);
});

test("desktop Project Brain passes axe", async ({ page }) => {
  await mockSlate(page); await connect(page); await openProjectBrain(page);
  await expect(page.getByRole("heading", { name: "Overview sources" })).toBeVisible();
  await page.mouse.move(700, 500);
  await page.waitForTimeout(250);
  expect((await new AxeBuilder({ page }).include(".project-brain").analyze()).violations).toEqual([]);
});

test("mobile Project Brain passes axe", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockSlate(page); await connect(page);
  await page.getByRole("button", { name: "Toggle library" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Actions for Projects" }).click();
  await page.getByRole("menuitem", { name: "Open Project Brain" }).click();
  await expect(page.getByRole("heading", { name: "Projects", exact: true })).toBeVisible();
  expect((await new AxeBuilder({ page }).include(".project-brain").analyze()).violations).toEqual([]);
});


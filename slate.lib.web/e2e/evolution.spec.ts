import { test, expect, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { connect, mockSlate, openNote } from "./fixture";

async function openNoteEvolution(page: Page) {
  await openNote(page);
  await page.getByRole("button", { name: "Evolution of this note" }).click();
  await page.getByRole("heading", { name: "Library architecture", exact: true }).waitFor();
}

async function openTopicEvolution(page: Page) {
  await page.keyboard.press("Control+Shift+P");
  await page.getByRole("combobox").fill("> evolution of thought");
  await page.keyboard.press("Enter");
  const input = page.getByLabel("Topic to trace");
  await input.fill("storage architecture");
  await page.getByRole("button", { name: "Trace", exact: true }).click();
  await page.getByRole("heading", { name: "storage architecture", exact: true }).waitFor();
}

test("opens Evolution of Thought from the active note", async ({ page }) => {
  await mockSlate(page); await connect(page); await openNoteEvolution(page);
  await expect(page.getByText("Evolution of Thought", { exact: true })).toBeVisible();
});

test("opens topic evolution from the Command Center", async ({ page }) => {
  const state = await mockSlate(page); await connect(page); await openTopicEvolution(page);
  expect(state.evolutionRequests.at(-1)?.scope).toMatchObject({ kind: "topic", topic: "storage architecture" });
});

test("opens project evolution from Project Brain", async ({ page }) => {
  const state = await mockSlate(page); await connect(page);
  await page.getByRole("button", { name: "Actions for Projects" }).first().click();
  await page.getByRole("menuitem", { name: "Open Project Brain" }).click();
  await page.locator(".project-actions").getByRole("button", { name: "Evolution", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Library architecture", exact: true })).toBeVisible();
  expect(state.evolutionRequests.at(-1)?.scope).toMatchObject({ kind: "project", path: "Projects" });
});

test("search results expose a scoped evolution action", async ({ page }) => {
  const state = await mockSlate(page); await connect(page);
  await page.getByRole("button", { name: "Search", exact: true }).first().click();
  await page.getByLabel("Search library").fill("architecture");
  await page.getByRole("button", { name: "Trace evolution of Library architecture" }).click();
  await expect(page.getByRole("heading", { name: "Library architecture", exact: true })).toBeVisible();
  expect(state.evolutionRequests.at(-1)?.scope.kind).toBe("note");
});

test("timeline separates documented and possible changes", async ({ page }) => {
  await mockSlate(page); await connect(page); await openNoteEvolution(page);
  await expect(page.getByText("Changed implementation", { exact: true }).first()).toBeVisible();
  await page.getByRole("button", { name: "Possible", exact: true }).click();
  await expect(page.getByRole("button", { name: /Possible shift in emphasis/ })).toBeVisible();
});

test("decision filter shows documented decisions", async ({ page }) => {
  await mockSlate(page); await connect(page); await openNoteEvolution(page);
  await page.getByRole("button", { name: "Decisions", exact: true }).click();
  await expect(page.getByRole("button", { name: /Decision recorded/ })).toBeVisible();
});

test("question filter shows answered question transitions", async ({ page }) => {
  await mockSlate(page); await connect(page); await openNoteEvolution(page);
  await page.getByRole("button", { name: "Questions", exact: true }).click();
  await expect(page.getByRole("button", { name: /Question answered/ })).toBeVisible();
});

test("superseded ideas remain explicit in the timeline", async ({ page }) => {
  await mockSlate(page); await connect(page); await openNoteEvolution(page);
  await expect(page.getByRole("button", { name: /Earlier idea superseded/ })).toBeVisible();
});

test("event detail provides before, after, rationale, and date authority", async ({ page }) => {
  await mockSlate(page); await connect(page); await openNoteEvolution(page);
  const comparison = page.getByRole("article", { name: "Before and after comparison" });
  await expect(comparison.getByText("The first design used a local polling loop.")).toBeVisible();
  await expect(comparison.getByText("The implementation changed to a durable event queue.")).toBeVisible();
  await expect(comparison.getByText("The queue preserves drafts during retries.")).toBeVisible();
  await expect(comparison.getByText("Git history", { exact: true })).toBeVisible();
});

test("historical evidence opens without pretending it is current", async ({ page }) => {
  await mockSlate(page); await connect(page); await openNoteEvolution(page);
  await page.getByRole("button", { name: /Open historical source/ }).first().click();
  await expect(page.getByRole("dialog").getByText("The first design used a local polling loop.")).toBeVisible();
});

test("current view opens the newest canonical note", async ({ page }) => {
  await mockSlate(page); await connect(page); await openNoteEvolution(page);
  const current = page.getByRole("complementary", { name: "Current view" });
  await expect(current.getByText("Markdown is the source of truth and a durable event queue handles retries.")).toBeVisible();
  await current.getByRole("button", { name: /Library architecture/ }).click();
  await expect(page.getByRole("heading", { name: "Library architecture", exact: true }).first()).toBeVisible();
});

test("grounded synthesis keeps the required evolution sections", async ({ page }) => {
  await mockSlate(page); await connect(page); await openNoteEvolution(page);
  await page.getByRole("button", { name: "Synthesize", exact: true }).click();
  const result = page.getByRole("region", { name: "Grounded evolution synthesis" });
  for (const heading of ["Early View", "What Changed", "Current View", "Key Turning Points", "Unresolved Questions"]) await expect(result.getByRole("heading", { name: heading })).toBeVisible();
});

test("Ask shortcut opens a grounded follow-up with the evolution question", async ({ page }) => {
  await mockSlate(page); await connect(page); await openTopicEvolution(page);
  await page.getByRole("button", { name: "Ask about this evolution" }).click();
  await expect(page.getByLabel("Question", { exact: true })).toHaveValue("How has storage architecture evolved?");
});

test("generation-disabled mode preserves deterministic history", async ({ page }) => {
  await mockSlate(page, { generationEnabled: false }); await connect(page); await openNoteEvolution(page);
  await expect(page.getByText(/deterministic timeline, comparisons, and current view remain available/)).toBeVisible();
  await expect(page.getByRole("button", { name: "Synthesize", exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: /Implementation changed/ })).toBeVisible();
});

test("mobile evolution remains stacked, readable, and accessible", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockSlate(page); await connect(page); await openNoteEvolution(page);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect((await new AxeBuilder({ page }).include(".evolution-workspace").analyze()).violations).toEqual([]);
});

test("desktop evolution passes axe", async ({ page }) => {
  await mockSlate(page); await connect(page); await openNoteEvolution(page);
  await page.mouse.move(700, 500); await page.waitForTimeout(150);
  expect((await new AxeBuilder({ page }).include(".evolution-workspace").analyze()).violations).toEqual([]);
});


import { test, expect, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { connect, mockSlate } from "./fixture";

async function browse(page: Page, generationEnabled = true) {
  await mockSlate(page, { generationEnabled }); await connect(page);
  await page.keyboard.press("Control+Shift+P"); await page.getByRole("combobox").fill("> browse concepts");
  await page.getByRole("option").filter({ hasText: "Browse Concepts" }).click();
  await expect(page.getByRole("heading", { name: "Concepts", exact: true })).toBeVisible();
}
async function openKubernetes(page: Page, generationEnabled = true) {
  await browse(page, generationEnabled); await page.getByPlaceholder(/Find Kubernetes/).fill("Kubernetes");
  await page.getByRole("button", { name: /Kubernetes Technology/ }).click();
  await expect(page.getByRole("heading", { name: "Kubernetes", exact: true })).toBeVisible();
}
async function openFirstNote(page: Page) {
  await page.getByRole("treeitem", { name: "Projects", exact: true }).first().click();
  await page.getByRole("treeitem", { name: "Library architecture", exact: true }).first().click();
}

test("opens Concepts from Command Center", async ({ page }) => { await browse(page); });
test("searches the Kubernetes concept", async ({ page }) => { await browse(page); await page.getByPlaceholder(/Find Kubernetes/).fill("Kubernetes"); await expect(page.getByRole("button", { name: /Kubernetes Technology/ })).toBeVisible(); });
test("opens a Concept Page", async ({ page }) => { await openKubernetes(page); await expect(page.getByText("Technology", { exact: true }).first()).toBeVisible(); });
test("shows ranked Key Notes", async ({ page }) => { await openKubernetes(page); await expect(page.getByRole("heading", { name: "Key Notes" })).toBeVisible(); await expect(page.getByText(/Dedicated concept note/).first()).toBeVisible(); });
test("shows canonical project usage", async ({ page }) => { await openKubernetes(page); await expect(page.getByRole("heading", { name: "Used In" })).toBeVisible(); await expect(page.getByRole("button", { name: /Projects 1 source/ })).toBeVisible(); });
test("shows related concepts with explanation", async ({ page }) => { await openKubernetes(page); await expect(page.getByRole("heading", { name: "Related Concepts" })).toBeVisible(); await expect(page.getByText(/deployment notes use PostgreSQL/)).toBeVisible(); });
test("opens a related Concept Page", async ({ page }) => { await openKubernetes(page); await page.getByRole("button", { name: /PostgreSQL used with/ }).click(); await expect(page.getByRole("heading", { name: "PostgreSQL", exact: true })).toBeVisible(); });
test("opens project usage in Project Brain", async ({ page }) => { await openKubernetes(page); await page.getByRole("button", { name: /Projects 1 source/ }).click(); await expect(page.getByRole("heading", { name: "Projects", exact: true })).toBeVisible(); });
test("shows concept decisions", async ({ page }) => { await openKubernetes(page); await expect(page.getByRole("heading", { name: "Decisions" })).toBeVisible(); await expect(page.getByText("Use Kubernetes for Slate deployment.")).toBeVisible(); });
test("shows open questions", async ({ page }) => { await openKubernetes(page); await expect(page.getByRole("heading", { name: "Open Questions" })).toBeVisible(); await expect(page.getByText(/local embeddings/).first()).toBeVisible(); });
test("opens Evolution scoped to the concept", async ({ page }) => { await openKubernetes(page); await page.locator(".concept-actions").getByRole("button", { name: "Evolution" }).click(); await expect(page.getByText("Evolution of Thought", { exact: true })).toBeVisible(); await expect(page.getByRole("heading", { name: "Kubernetes", exact: true })).toBeVisible(); });
test("integrates Knowledge Issues", async ({ page }) => { await openKubernetes(page); await page.getByRole("button", { name: "Review concept issues" }).click(); await expect(page.getByRole("heading", { name: "Knowledge Issues" })).toBeVisible(); });
test("integrates Knowledge Overlap", async ({ page }) => { await openKubernetes(page); await page.getByRole("heading", { name: "Overlap" }).click(); await page.getByRole("button", { name: "Review overlapping notes" }).click(); await expect(page.getByRole("heading", { name: "Knowledge Overlap" })).toBeVisible(); });
test("integrates Link Opportunities", async ({ page }) => { await openKubernetes(page); await page.getByRole("heading", { name: "Link Opportunities" }).click(); await page.getByRole("button", { name: "Review missing links" }).click(); await expect(page.getByRole("heading", { name: "Link Opportunities" })).toBeVisible(); });
test("asks about a concept with selected member sources", async ({ page }) => { await openKubernetes(page); await page.getByRole("button", { name: "Ask", exact: true }).click(); await expect(page.getByLabel("Question", { exact: true })).toHaveValue(/library say about Kubernetes/); });
test("shows a concept result beside note search", async ({ page }) => { await mockSlate(page); await connect(page); await page.keyboard.press("Control+K"); await page.getByRole("combobox").fill("Kubernetes"); await expect(page.getByRole("option").filter({ hasText: "Concept · Kubernetes" })).toBeVisible(); });
test("shows concepts in current-note details", async ({ page }) => { await mockSlate(page); await connect(page); await openFirstNote(page); await page.getByRole("button", { name: "Info" }).click(); await expect(page.getByRole("heading", { name: "Concepts" })).toBeVisible(); await expect(page.getByRole("button", { name: /Kubernetes Technology/ })).toBeVisible(); });
test("Project Brain includes Key Concepts", async ({ page }) => { await mockSlate(page); await connect(page); await page.getByRole("button", { name: "Actions for Projects" }).first().click(); await page.getByRole("menuitem", { name: "Open Project Brain" }).click(); await expect(page.getByRole("heading", { name: "Key Concepts" })).toBeVisible(); await expect(page.getByRole("button", { name: /Kubernetes/ }).last()).toBeVisible(); });
test("merges a derived alias without editing notes", async ({ page }) => { await openKubernetes(page); page.once("dialog", (dialog) => dialog.accept("Kube")); await page.getByRole("button", { name: "Merge identity" }).click(); const stored = await page.evaluate(() => Object.keys(localStorage).find((key) => key.startsWith("slate.concept-reviews"))); expect(stored).toBeTruthy(); });
test("keeps concepts separate as derived review state", async ({ page }) => { await openKubernetes(page); page.once("dialog", (dialog) => dialog.accept("Kubernetes Service")); await page.getByRole("button", { name: "Keep separate" }).click(); const value = await page.evaluate(() => Object.entries(localStorage).find(([key]) => key.startsWith("slate.concept-reviews"))?.[1] ?? ""); expect(value).toContain("kubernetes service"); });
test("works with generation disabled", async ({ page }) => { await openKubernetes(page, false); await expect(page.getByText(/generation is optional/)).toBeVisible(); await expect(page.getByRole("heading", { name: "Key Notes" })).toBeVisible(); });
test("mobile Concept Page uses stacked actions", async ({ page }) => { await page.setViewportSize({ width: 390, height: 844 }); await openKubernetes(page); await expect(page.getByRole("button", { name: "Ask", exact: true })).toBeVisible(); expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true); });
test("mobile related-concept navigation works", async ({ page }) => { await page.setViewportSize({ width: 390, height: 844 }); await openKubernetes(page); await page.getByRole("button", { name: /PostgreSQL used with/ }).click(); await expect(page.getByRole("heading", { name: "PostgreSQL", exact: true })).toBeVisible(); });
test("desktop Concept Page passes axe", async ({ page }) => { await openKubernetes(page); expect((await new AxeBuilder({ page }).include(".concept-workspace").analyze()).violations).toEqual([]); });
test("mobile Concept Page passes axe", async ({ page }) => { await page.setViewportSize({ width: 390, height: 844 }); await openKubernetes(page); expect((await new AxeBuilder({ page }).include(".concept-workspace").analyze()).violations).toEqual([]); });

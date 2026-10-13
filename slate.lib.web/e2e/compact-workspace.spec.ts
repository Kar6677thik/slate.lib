import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { connect, first, mockSlate, openNote } from "./fixture";

test("one top tab bar, one global search, and contextual close buttons", async ({
  page,
}) => {
  await mockSlate(page);
  await connect(page);
  await openNote(page);
  const header = page.locator(".workspace-topbar");
  expect((await header.boundingBox())?.y).toBe(0);
  await expect(
    header.getByRole("tablist", { name: "Open notes" }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Search your library", exact: true }),
  ).toHaveCount(1);
  await expect(
    page.getByRole("button", { name: "New note", exact: true }),
  ).toHaveCount(1);
  await expect(
    page.locator(".global-search, .pane-toggles, .topbar"),
  ).toHaveCount(0);
  await expect(
    page.getByRole("complementary", { name: "Note details" }),
  ).toHaveCount(0);
  await page.getByRole("button", { name: "Search your library" }).click();
  await page.getByRole("combobox").fill("Reading list");
  await page.getByRole("option", { name: /Reading list/ }).click();
  await expect(header.getByRole("tab", { name: "Reading list" })).toBeVisible();
  await page
    .getByRole("button", { name: "Close Library architecture", exact: true })
    .click();
  await expect(
    header.getByRole("tab", { name: "Library architecture" }),
  ).toHaveCount(0);
  await expect(page.getByRole("article")).toBeVisible();
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
});

test("AI visibility persists, disables automatic analysis, and keeps keyword search usable", async ({
  page,
}) => {
  await mockSlate(page);
  await connect(page);
  await openNote(page);
  await page.getByRole("switch", { name: "AI features", exact: true }).click();
  await expect(
    page.getByRole("switch", { name: "AI features" }),
  ).not.toBeChecked();
  await expect(page.getByRole("button", { name: "AI tools" })).toHaveCount(0);
  await expect(
    page.getByRole("button", { name: "Ask about selection" }),
  ).toHaveCount(0);
  const calls: string[] = [];
  page.on("request", (request) => {
    if (new URL(request.url()).pathname.startsWith("/api/intelligence/"))
      calls.push(new URL(request.url()).pathname);
  });
  await page.reload();
  await expect(page.getByRole("article")).toBeVisible();
  await expect(
    page.getByRole("switch", { name: "AI features" }),
  ).not.toBeChecked();
  await page.getByRole("button", { name: "Search your library" }).click();
  await page.getByRole("combobox").fill("Library architecture");
  await expect(
    page.getByRole("option", { name: /Library architecture/ }).first(),
  ).toBeVisible();
  await page.getByRole("combobox").fill("> ask slate");
  await expect(page.getByRole("option", { name: /Ask Slate/ })).toHaveCount(0);
  expect(calls).toEqual([]);
  await page.keyboard.press("Escape");
  await page.goto("/?view=knowledge-issues");
  await expect(
    page.getByRole("heading", { name: "Library", exact: true }),
  ).toBeVisible();
  expect(calls).toEqual([]);
  await page.getByRole("switch", { name: "AI features" }).click();
  await expect(page.getByRole("button", { name: "AI tools" })).toBeVisible();
  await page.getByRole("button", { name: "AI tools" }).click();
  await page.getByRole("menuitem", { name: "Ask Slate", exact: true }).click();
  await expect(page.getByLabel("Question", { exact: true })).toBeVisible();
});

test("floating search preserves the document and restores keyboard focus", async ({
  page,
}) => {
  await mockSlate(page);
  await connect(page);
  await openNote(page);
  const trigger = page.getByRole("button", { name: "Search your library" });
  await trigger.click();
  const input = page.getByRole("combobox");
  await expect(input).toBeFocused();
  await page.getByRole("button", { name: "Filters & saved searches" }).click();
  await expect(
    page.getByLabel("Search library", { exact: true }),
  ).toBeFocused();
  await page.getByLabel("Search library", { exact: true }).fill("Library");
  await expect(
    page
      .getByRole("listitem")
      .filter({ hasText: "Library architecture" })
      .first(),
  ).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(trigger).toBeFocused();
  await expect(page.getByRole("article")).toBeVisible();
});

for (const width of [320, 390, 768, 1024, 1440, 1920])
  test(`compact workspace and search fit ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: width < 768 ? 844 : 900 });
    await mockSlate(page);
    await connect(page);
    if (width < 768)
      await page.getByRole("button", { name: "Toggle library" }).click();
    await openNote(page);
    await expect(page.getByRole("article")).toBeVisible();
    await expect(
      page.getByRole("tablist", { name: "Open notes" }),
    ).toBeVisible();
    await expect(
      page
        .locator(".workspace-topbar")
        .getByRole("tab", { name: "Library architecture" }),
    ).toBeVisible();
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBe(true);
    if (width < 768) {
      await expect(
        page
          .getByRole("navigation", { name: "Main navigation" })
          .getByRole("button"),
      ).toHaveCount(5);
      await page.getByRole("button", { name: "Workspace menu" }).click();
      await page
        .getByRole("menuitem", { name: "Document details", exact: true })
        .click();
      await expect(page.getByRole("dialog")).toBeVisible();
      await page.getByRole("button", { name: "Close dialog" }).click();
    }
    await page.screenshot({
      path: `output/playwright/compact-workspace-${width}.png`,
    });
    await page.getByRole("button", { name: "Search your library" }).click();
    await page.getByRole("combobox").fill("Library");
    await expect(
      page.getByRole("option", { name: /Library architecture/ }).first(),
    ).toBeVisible();
    const bounds = await page.getByRole("dialog").boundingBox();
    expect(bounds?.x).toBeGreaterThanOrEqual(0);
    expect((bounds?.x ?? 0) + (bounds?.width ?? 0)).toBeLessThanOrEqual(width);
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBe(true);
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
    await page.screenshot({
      path: `output/playwright/compact-search-${width}.png`,
    });
  });

test("turning AI off preserves a note opened from an intelligence workspace", async ({
  page,
}) => {
  await mockSlate(page);
  await connect(page);
  await page.goto(`/?view=evolution&note=${first}`);
  await expect(page.getByRole("article")).toBeVisible();
  await page.getByRole("switch", { name: "AI features" }).click();
  await expect(
    page.getByRole("switch", { name: "AI features" }),
  ).not.toBeChecked();
  await expect(page.getByRole("article")).toBeVisible();
  await expect(
    page.getByRole("tab", { name: "Library architecture" }),
  ).toHaveAttribute("aria-selected", "true");
});

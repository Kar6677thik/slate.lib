import { expect, test } from "@playwright/test";
import { connect, first, mockSlate, openNote } from "./fixture";

test("smart views, rediscovery, daily notes and related knowledge use canonical APIs", async ({
  page,
}) => {
  const state = await mockSlate(page);
  await connect(page);

  await page.getByRole("button", { name: "Unanswered questions" }).click();
  await expect(
    page.getByRole("heading", { name: "Unanswered questions" }),
  ).toBeVisible();
  await expect(page.getByText("An open question from the library.")).toBeVisible();

  await page.getByRole("button", { name: "Rediscover", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Rediscover" })).toBeVisible();
  await expect(
    page.getByText("You have not opened this note recently."),
  ).toBeVisible();

  await page.getByRole("button", { name: "Daily note", exact: true }).click();
  await expect(page.getByRole("tab", { name: /2026-/ })).toBeVisible();
  expect(
    state.requests.some((request) => request.path === "v1/workflows/daily"),
  ).toBe(true);

  await page.evaluate((noteId) => {
    window.history.replaceState({}, "", `/?note=${noteId}`);
  }, first);
  await page.reload();
  await page.getByRole("button", { name: "Related notes" }).click();
  await expect(page.getByText("Links to this note")).toBeVisible();
  await page.getByRole("button", { name: "Note graph" }).click();
  await expect(
    page.getByRole("button", { name: /Reading list Research\/Reading list\.md/ }),
  ).toBeVisible();
});

test("multi-select previews and applies one server-owned bulk operation", async ({
  page,
}) => {
  const state = await mockSlate(page);
  await connect(page);

  await page.getByRole("button", { name: "Select items" }).click();
  await page.getByRole("checkbox", { name: "Select Projects" }).check();
  await page.getByRole("checkbox", { name: "Select Research" }).check();
  await page.getByRole("button", { name: "Duplicate" }).click();
  await page.getByRole("button", { name: "Review plan" }).click();
  await expect(page.getByText("Projects copy", { exact: true })).toBeVisible();
  await expect(page.getByText("Research copy", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Apply reviewed plan" }).click();

  await expect.poll(() => state.notes.size).toBe(4);
  expect(
    state.requests.some(
      (request) => request.path === "v1/library/bulk/preview",
    ),
  ).toBe(true);
  expect(
    state.requests.some((request) => request.path === "v1/library/bulk/apply"),
  ).toBe(true);
});

test("wiki export previews portable Markdown before revision-checked apply", async ({
  page,
}) => {
  const state = await mockSlate(page);
  await connect(page);
  await openNote(page);

  await page.getByRole("button", { name: "Export wiki links" }).click();
  await expect(page.getByRole("heading", { name: "Export wiki links" })).toBeVisible();
  await expect(page.getByText("Portable Markdown", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Apply reviewed conversion" }).click();

  await expect.poll(() => state.notes.get(first)?.markdown).toContain(
    "[Reading list](../Research/Reading%20list.md)",
  );
});

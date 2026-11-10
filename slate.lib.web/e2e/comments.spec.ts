import { test, expect, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mockSlate, connect, openNote, first } from "./fixture";

async function selectInNote(page: Page, text: string) {
  await page.evaluate((needle) => {
    const article = document.querySelector("article.markdown")!;
    const walker = document.createTreeWalker(article, NodeFilter.SHOW_TEXT);
    for (let node = walker.nextNode() as Text | null; node; node = walker.nextNode() as Text | null) {
      const at = node.data.indexOf(needle);
      if (at < 0) continue;
      const range = document.createRange();
      range.setStart(node, at);
      range.setEnd(node, at + needle.length);
      const selection = getSelection()!;
      selection.removeAllRanges();
      selection.addRange(range);
      return;
    }
    throw new Error("Text not found: " + needle);
  }, text);
}

test("asks about a passage, sees an agent answer with a linked page, and resolves it", async ({ page }) => {
  const state = await mockSlate(page);
  await connect(page);
  await openNote(page);

  await selectInNote(page, "stable identities");
  await page.getByRole("button", { name: "Ask about this" }).click();
  const drawer = page.getByRole("dialog", { name: "Passage comments" });
  await expect(drawer.locator(".comment-quote")).toHaveText("stable identities");
  await drawer.getByPlaceholder(/What would you like to know/).fill("Which identity do links use?");
  await drawer.getByRole("button", { name: "Comment", exact: true }).click();
  await expect(drawer.getByText("Waiting for an answer")).toBeVisible();

  const created = state.requests.find((request) => request.method === "POST" && request.path === `v1/notes/${first}/comments`);
  expect(created?.body).toMatchObject({
    anchor: { kind: "text", exact: "stable identities", suffix: expect.stringMatching(/^ for links/) },
    body: "Which identity do links use?",
    author: { kind: "user" },
  });
  expect((await new AxeBuilder({ page }).include(".comment-drawer").analyze()).violations).toEqual([]);

  // An agent answers through MCP while the note is open; the reader picks it up on its next refresh.
  const thread = state.comments.get(first)![0];
  thread.comments.push({
    id: crypto.randomUUID(),
    author: { kind: "agent", name: "Claude" },
    body: "Links use the note's **UUID**, so moves never break them.",
    createdAt: new Date().toISOString(),
    linkedNote: { noteId: "00000000-0000-4000-8000-000000000002", title: "Reading list", path: "Research/Reading list.md" },
  });
  await page.keyboard.press("Escape");
  await expect(drawer).toBeHidden();
  await page.evaluate(() => window.dispatchEvent(new Event("focus")));
  await page.reload();
  await openNote(page);
  const bubble = page.getByRole("button", { name: "1 comment on this paragraph" });
  await expect(bubble).toBeVisible();
  await bubble.click();
  await expect(drawer.getByText("Claude")).toBeVisible();
  await expect(drawer.getByText("Answered")).toBeVisible();
  await drawer.getByRole("button", { name: /Wrote a page for this/ }).click();
  await expect(page.getByRole("tab", { name: "Reading list" })).toHaveAttribute("aria-selected", "true");

  await page.getByRole("tab", { name: "Library architecture" }).click();
  await page.getByRole("button", { name: /open passage comment/ }).click();
  await drawer.getByRole("button", { name: "Resolve" }).click();
  await expect(drawer.getByText("No passage comments open.", { exact: false })).toBeVisible();
  expect(thread.status).toBe("resolved");
});

test("page discussion takes questions and replies, on desktop and phone", async ({ page }) => {
  const state = await mockSlate(page);
  await connect(page);
  await openNote(page);
  const discussion = page.getByRole("region", { name: /Discussion/ });
  await discussion.getByPlaceholder(/Ask a question or leave a note/).fill("Future me: why Git and not a database?");
  await discussion.getByRole("button", { name: "Comment", exact: true }).click();
  await expect(discussion.getByText("Future me: why Git and not a database?")).toBeVisible();
  await expect(discussion.getByText("Waiting for an answer")).toBeVisible();
  await discussion.getByRole("button", { name: "Reply" }).click();
  await discussion.getByPlaceholder("Add a reply…").fill("Because history matters.");
  await discussion.getByRole("button", { name: "Reply", exact: true }).last().click();
  await expect(discussion.getByText("Because history matters.")).toBeVisible();
  const thread = state.comments.get(first)![0];
  expect(thread.comments).toHaveLength(2);
  expect(thread.anchor).toEqual({ kind: "page" });
  expect((await new AxeBuilder({ page }).include(".note-discussion").analyze()).violations).toEqual([]);

  await page.setViewportSize({ width: 390, height: 844 });
  await discussion.scrollIntoViewIfNeeded();
  await expect(discussion.getByText("Because history matters.")).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0);
});

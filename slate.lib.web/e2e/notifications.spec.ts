import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import { mockSlate, connect, first, second } from "./fixture";

const now = () => new Date().toISOString();

test("notifications page lists replies and bundled new pages and leads to them", async ({ page }) => {
  const state = await mockSlate(page);
  const threadId = crypto.randomUUID();
  state.comments.set(first, [
    {
      id: threadId,
      anchor: { kind: "text", exact: "stable identities", prefix: "readable. Use ", suffix: " for links" },
      status: "open",
      createdAt: now(),
      updatedAt: now(),
      comments: [
        { id: crypto.randomUUID(), author: { kind: "user", name: "You" }, body: "Which identity?", createdAt: now() },
        { id: crypto.randomUUID(), author: { kind: "agent", name: "Claude" }, body: "The note **UUID**.", createdAt: now() },
      ],
    },
  ]);
  state.notifications.push(
    {
      fromThisDevice: false,
      notification: {
        id: crypto.randomUUID(), kind: "comment", createdAt: now(), updatedAt: now(), readAt: null,
        actor: { kind: "agent", name: "Claude" },
        comment: {
          noteId: first, noteTitle: "Library architecture", notePath: "Projects/Library architecture.md", threadId,
          commentId: crypto.randomUUID(), anchorKind: "text", quote: "stable identities", excerpt: "The note **UUID**.", isReply: true,
          linkedNote: { noteId: second, title: "Reading list", path: "Research/Reading list.md" },
        },
      },
    },
    {
      fromThisDevice: false,
      notification: {
        id: crypto.randomUUID(), kind: "notes", createdAt: now(), updatedAt: now(), readAt: null,
        actor: { kind: "device", name: "Android" },
        notes: {
          source: "captured",
          count: 120,
          notes: Array.from({ length: 50 }, (_, index) => ({ noteId: crypto.randomUUID(), title: `Capture ${index + 1}`, path: `inbox/capture-${index + 1}.md` })),
        },
      },
    },
  );

  await connect(page);
  const bell = page.getByRole("button", { name: "Notifications, 2 unread" }).first();
  await expect(bell).toBeVisible();
  await bell.click();
  await expect(page.getByRole("heading", { name: "Notifications", exact: true })).toBeVisible();
  const feed = page.locator(".notifications-list");
  await expect(feed.getByText("Android")).toBeVisible();
  await expect(feed.getByText(/captured 120 pages in/)).toBeVisible();
  await expect(feed.getByRole("button", { name: /Capture/ })).toHaveCount(5);
  await feed.getByRole("button", { name: "Show all 50" }).click();
  await expect(feed.getByRole("button", { name: /Capture/ })).toHaveCount(50);
  await expect(feed.getByText("…and 70 more.", { exact: false })).toBeVisible();
  expect((await new AxeBuilder({ page }).include(".notifications-view").analyze()).violations).toEqual([]);

  // Viewing the page marks everything read, which clears the navigation badge.
  await expect(page.getByRole("button", { name: "Notifications", exact: true }).first()).toBeVisible();
  expect(state.requests.some((request) => request.path === "v1/notifications/read")).toBe(true);

  await page.getByRole("tab", { name: "Comments" }).click();
  await expect(feed.getByText("Android")).toBeHidden();
  await feed.getByText("The note UUID.").click();
  await expect(page.getByRole("tab", { name: "Library architecture" })).toHaveAttribute("aria-selected", "true");
  const drawer = page.getByRole("dialog", { name: "Passage comments" });
  await expect(drawer.getByText("Claude")).toBeVisible();
  await expect(drawer.locator(".comment-quote")).toHaveText("stable identities");
});

test("notifications page fits a phone and links answer pages", async ({ page }) => {
  const state = await mockSlate(page);
  state.notifications.push({
    fromThisDevice: false,
    notification: {
      id: crypto.randomUUID(), kind: "comment", createdAt: now(), updatedAt: now(), readAt: null,
      actor: { kind: "agent", name: "ChatGPT" },
      comment: {
        noteId: first, noteTitle: "Library architecture", notePath: "Projects/Library architecture.md", threadId: crypto.randomUUID(),
        commentId: crypto.randomUUID(), anchorKind: "page", quote: null, excerpt: "I wrote a page.", isReply: true,
        linkedNote: { noteId: second, title: "Reading list", path: "Research/Reading list.md" },
      },
    },
  });
  await page.setViewportSize({ width: 390, height: 844 });
  await connect(page);
  await page.getByRole("button", { name: /^Notifications/ }).last().click();
  await expect(page.getByText("ChatGPT")).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0);
  await page.getByRole("button", { name: /Wrote a page for this/ }).click();
  await expect(page.getByRole("heading", { name: "Reading list" }).first()).toBeVisible();
});

test("long page titles and deep folders never overflow the page", async ({ page }) => {
  const state = await mockSlate(page);
  const deep = "Learning/Question Deep Dives/AI Foundations";
  const long = (topic: string) => `${topic} — A Deep Dive Into Every Technique, the Theory Behind It, Real Applications and Interview Questions`;
  state.notifications.push(
    {
      fromThisDevice: false,
      notification: {
        id: crypto.randomUUID(), kind: "notes", createdAt: now(), updatedAt: now(), readAt: null,
        actor: { kind: "device", name: "Slate-MCP" },
        notes: { source: "created", count: 3, notes: ["Prompting", "Softmax", "Frontier models"].map((topic) => ({ noteId: crypto.randomUUID(), title: long(topic), path: `${deep}/${topic}.md` })) },
      },
    },
    {
      fromThisDevice: false,
      notification: {
        id: crypto.randomUUID(), kind: "notes", createdAt: now(), updatedAt: now(), readAt: null,
        actor: { kind: "device", name: "Slate-MCP" },
        notes: {
          source: "created", count: 2,
          notes: [
            { noteId: crypto.randomUUID(), title: long("Forward deployed engineering"), path: "Learning/Question Deep Dives/Careers/fde.md" },
            { noteId: crypto.randomUUID(), title: long("Git and GitHub"), path: "Learning/Question Deep Dives/Developer Tools/git.md" },
          ],
        },
      },
    },
  );
  for (const width of [1280, 390, 320]) {
    await page.setViewportSize({ width, height: 844 });
    if (width === 1280) {
      await connect(page);
      await page.getByRole("button", { name: /^Notifications/ }).first().click();
    }
    const feed = page.locator(".notifications-list");
    // One shared folder is named once in the heading; mixed folders get a short label per row.
    await expect(feed.locator(".notification-folder")).toHaveText("AI Foundations");
    await expect(feed.locator(".notification-notes small")).toHaveText(["Careers", "Developer Tools"]);
    const overflow = await page.evaluate(() => {
      const view = document.querySelector(".notifications-view")!;
      return { page: document.documentElement.scrollWidth - window.innerWidth, view: view.scrollWidth - view.clientWidth };
    });
    expect(overflow, `width ${width}`).toEqual({ page: 0, view: 0 });
    await page.screenshot({ path: `output/playwright/notifications-${width}.png` });
  }
});

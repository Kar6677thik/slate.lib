import { it, expect } from "vitest";
import { drafts } from "@/lib/storage/drafts";
it("serializes rapid edits and clearing after acknowledged save", async () => {
  const draft = {
    key: "library:note",
    id: "note",
    path: "A.md",
    baseRevision: "old",
    baseSource: "base",
    source: "first",
    updatedAt: new Date().toISOString(),
  };
  await Promise.all([
    drafts.put(draft),
    drafts.put({ ...draft, source: "second" }),
  ]);
  expect((await drafts.get(draft.key))?.source).toBe("second");
  await Promise.all([
    drafts.put({ ...draft, source: "third" }),
    drafts.remove(draft.key),
  ]);
  expect(await drafts.get(draft.key)).toBeUndefined();
});

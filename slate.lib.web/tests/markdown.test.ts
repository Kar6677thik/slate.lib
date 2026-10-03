import { it, expect } from "vitest";
import { slateMarkdown, headingSlug } from "@/lib/markdown/slate-markdown";
it("transforms wiki text without touching code", () => {
  const tree = {
    type: "root",
    children: [
      {
        type: "paragraph",
        children: [{ type: "text", value: "See [[Folder/Note|note]]" }],
      },
      { type: "code", value: "[[literal]]" },
    ],
  };
  slateMarkdown()(tree);
  expect(JSON.stringify(tree)).toContain("/__slate/wiki?target=Folder%2FNote");
  expect(tree.children[1].value).toBe("[[literal]]");
  expect(headingSlug("Hello, world!")).toBe("hello-world");
});

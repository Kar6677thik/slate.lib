import { describe, expect, it } from "vitest";
import { resolveNoteHref } from "@/lib/markdown/links";

describe("relative note links", () => {
  const from = "Learning/System Design/Case Studies/Flux.md";
  it("resolves parent-relative links against the note's folder", () => {
    expect(resolveNoteHref(from, "../../../Projects/Flux/00-Start-Here.md")).toEqual({ path: "Projects/Flux/00-Start-Here.md", heading: null });
  });
  it("resolves sibling links, decodes spaces and keeps the heading", () => {
    expect(resolveNoteHref(from, "./Other%20Study.md#Key%20choices")).toEqual({ path: "Learning/System Design/Case Studies/Other Study.md", heading: "Key choices" });
    expect(resolveNoteHref("Root.md", "Child.md")).toEqual({ path: "Child.md", heading: null });
  });
  it("ignores URLs, anchors, absolute paths, non-notes and escapes above the root", () => {
    for (const href of ["https://example.com/a.md", "mailto:x@y.z", "#section", "/abs/note.md", "../image.png", "../../../../../../outside.md", undefined])
      expect(resolveNoteHref(from, href)).toBeNull();
  });
});

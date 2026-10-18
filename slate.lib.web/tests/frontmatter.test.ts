import { describe, expect, it } from "vitest";
import { frontmatterField, setFrontmatterField } from "@/lib/markdown/frontmatter";

describe("frontmatter fields", () => {
  it("adds a field to existing frontmatter without touching other fields or the body", () => {
    const source = "---\nid: abc\ntags: [a, b]\n---\n\n# Title\n\nBody";
    expect(setFrontmatterField(source, "status", "learning")).toBe("---\nid: abc\ntags: [a, b]\nstatus: learning\n---\n\n# Title\n\nBody");
  });
  it("replaces an existing field case-insensitively", () => {
    expect(setFrontmatterField("---\nStatus: draft\n---\nBody", "status", "needs-review")).toBe("---\nstatus: needs-review\n---\nBody");
  });
  it("creates a frontmatter block when the note has none", () => {
    expect(setFrontmatterField("# Title", "status", "learning")).toBe("---\nstatus: learning\n---\n\n# Title");
  });
  it("removes a field, and the whole block when it was the only field", () => {
    expect(setFrontmatterField("---\nid: abc\nstatus: learning\n---\nBody", "status", null)).toBe("---\nid: abc\n---\nBody");
    expect(setFrontmatterField("---\nstatus: learning\n---\nBody", "status", null)).toBe("Body");
    expect(setFrontmatterField("Body", "status", null)).toBe("Body");
  });
  it("preserves Windows line endings and quotes values that need it", () => {
    expect(setFrontmatterField("---\r\nid: abc\r\n---\r\nBody", "status", "in progress")).toBe('---\r\nid: abc\r\nstatus: "in progress"\r\n---\r\nBody');
  });
  it("reads a field value", () => {
    expect(frontmatterField("---\nstatus: 'learning'\n---\nBody", "Status")).toBe("learning");
    expect(frontmatterField("# No frontmatter", "status")).toBeNull();
  });
});

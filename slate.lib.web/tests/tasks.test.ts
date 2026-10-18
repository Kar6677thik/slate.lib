import { describe, expect, it } from "vitest";
import { toggleTaskAt } from "@/lib/markdown/tasks";

describe("task toggling", () => {
  const source = "# List\n\n- [ ] first\n- [x] second\n  1. [ ] nested\n";
  it("checks and unchecks the task at an offset", () => {
    expect(toggleTaskAt(source, source.indexOf("- [ ] first"))).toBe("# List\n\n- [x] first\n- [x] second\n  1. [ ] nested\n");
    expect(toggleTaskAt(source, source.indexOf("- [x] second"))).toBe("# List\n\n- [ ] first\n- [ ] second\n  1. [ ] nested\n");
  });
  it("handles ordered and indented items", () => {
    expect(toggleTaskAt(source, source.indexOf("1. [ ]"))).toContain("1. [x] nested");
  });
  it("refuses offsets that do not start a task", () => {
    expect(toggleTaskAt(source, 0)).toBeNull();
    expect(toggleTaskAt(source, -1)).toBeNull();
    expect(toggleTaskAt("- plain item [ ] not a task", 0)).toBeNull();
  });
});

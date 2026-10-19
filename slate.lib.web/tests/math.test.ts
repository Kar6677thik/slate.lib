import { describe, expect, it } from "vitest";
import { normalizeMathDelimiters } from "@/lib/markdown/math";

describe("math delimiters", () => {
  it("converts \\( \\) to inline and \\[ \\] to display math", () => {
    expect(normalizeMathDelimiters("Target is \\(y = 10\\) and \\(\\hat y = w\\).")).toBe("Target is $y = 10$ and $\\hat y = w$.");
    expect(normalizeMathDelimiters("Loss:\n\n\\[ L(w)=\\frac{1}{2}(w-10)^2 \\]\n")).toBe("Loss:\n\n\n$$\nL(w)=\\frac{1}{2}(w-10)^2\n$$\n\n");
  });
  it("leaves code and plain escaped brackets alone", () => {
    const code = "```\nprintf(\"\\(x\\)\");\n```\nand `\\(y\\)`";
    expect(normalizeMathDelimiters(code)).toBe(code);
    expect(normalizeMathDelimiters("No math here.")).toBe("No math here.");
  });
});

import { describe, expect, it } from "vitest";
import { buildTextIndex, locateQuote, quoteFromRange, rangeFor } from "@/lib/comments/anchoring";

function article(html: string) {
  const root = document.createElement("article");
  root.innerHTML = html;
  document.body.replaceChildren(root);
  return root;
}

describe("comment anchoring", () => {
  it("captures a quote across inline formatting and ignores reader chrome", () => {
    const root = article(
      '<h2><a class="heading-anchor">#</a>MVCC</h2><p>PostgreSQL keeps <strong>old row versions</strong> until VACUUM runs.</p>',
    );
    const index = buildTextIndex(root);
    expect(index.text).toBe("MVCCPostgreSQL keeps old row versions until VACUUM runs.");
    const strong = root.querySelector("strong")!;
    const range = document.createRange();
    range.setStart(root.querySelector("p")!.firstChild!, "PostgreSQL ".length);
    range.setEnd(strong.firstChild!, "old row".length);
    expect(quoteFromRange(index, range)).toEqual({
      kind: "text",
      exact: "keeps old row",
      prefix: "MVCCPostgreSQL ",
      suffix: " versions until VACUUM runs.",
    });
  });

  it("trims selected edge whitespace and rejects empty selections", () => {
    const root = article("<p>alpha beta gamma</p>");
    const index = buildTextIndex(root);
    const text = root.querySelector("p")!.firstChild!;
    const range = document.createRange();
    range.setStart(text, 5);
    range.setEnd(text, 11);
    expect(quoteFromRange(index, range)?.exact).toBe("beta");
    range.setStart(text, 5);
    range.setEnd(text, 6);
    expect(quoteFromRange(index, range)).toBeNull();
  });

  it("re-anchors with prefix and suffix, tolerating whitespace changes", () => {
    const text = "row versions matter.\nEvery  row versions table needs care.";
    const located = locateQuote(text, { kind: "text", exact: "row versions", prefix: "Every ", suffix: " table" });
    expect(located).toEqual({ start: text.indexOf("row versions table"), end: text.indexOf(" table") });
    expect(locateQuote(text, { kind: "text", exact: "Every row versions" })).toEqual({
      start: text.indexOf("Every"),
      end: text.indexOf(" table"),
    });
    expect(locateQuote(text, { kind: "text", exact: "vacuum" })).toBeNull();
    expect(locateQuote(text, { kind: "page" })).toBeNull();
  });

  it("builds a DOM range that spans elements", () => {
    const root = article("<p>one <em>two</em> three</p>");
    const index = buildTextIndex(root);
    const span = locateQuote(index.text, { kind: "text", exact: "two three" })!;
    expect(rangeFor(index, span.start, span.end)?.toString()).toBe("two three");
  });
});

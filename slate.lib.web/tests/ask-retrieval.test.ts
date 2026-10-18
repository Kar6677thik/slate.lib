import { describe, expect, it } from "vitest";
import { keywordQueries, validateCitations } from "@/lib/intelligence/ask-context";

describe("Ask Slate keyword retrieval", () => {
  it("drops question words that Slate's all-terms keyword search would require", () => {
    const queries = keywordQueries("How does Flux handle Redis outages?");
    expect(queries[0]).toBe("flux redis outages");
    for (const query of queries) expect(query).not.toMatch(/\b(how|does|handle)\b/);
  });
  it("adds looser pair and single-word fallbacks", () => {
    const queries = keywordQueries("Why did we choose PostgreSQL over SQLite for derived intelligence?");
    expect(queries.length).toBeGreaterThan(2);
    expect(queries.some((query) => query.split(" ").length === 2)).toBe(true);
    expect(queries).toContain("postgresql");
  });
  it("keeps technical tokens and returns nothing for a question with no subject", () => {
    expect(keywordQueries("What is ASP.NET Core?")[0]).toBe("asp.net core");
    expect(keywordQueries("What does this do?")).toEqual([]);
  });
});

describe("citation validation", () => {
  const sources = ["S1", "S2", "S3"].map((citationId, ordinal) => ({ citationId, noteId: `n${ordinal}`, title: "t", path: "p.md", heading: null, ordinal, revision: "r", excerpt: "e", score: 1 }));
  it("splits grouped citations and drops unknown ones", () => {
    const result = validateCitations("Uses Redis [S1, S3] and Kafka [S2; S9].", sources);
    expect(result.text).toBe("Uses Redis [S1][S3] and Kafka [S2].");
    expect(result.citations.sort()).toEqual(["S1", "S2", "S3"]);
  });
});

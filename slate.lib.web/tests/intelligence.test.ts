import { afterEach, describe, expect, it, vi } from "vitest";
import { chunkMarkdown } from "@/lib/intelligence/chunking";
import { DeterministicEmbeddingProvider, OpenAICompatibleEmbeddingProvider } from "@/lib/intelligence/provider";
import { fuseResults } from "@/lib/intelligence/ranking";
import { parseSemanticQuery } from "@/lib/intelligence/query";
import { embedChangedChunks } from "@/lib/intelligence/service";
import type { Note, SearchHit } from "@/lib/api/contracts";
import type { SemanticHit } from "@/lib/intelligence/types";

const note: Note = {
  id: "00000000-0000-4000-8000-000000000001",
  path: "Research/retrieval.md",
  title: "Retrieval notes",
  revision: "r1",
  markdown: `---\ntags: [search, research]\ntype: reference\nstatus: active\n---\n# Ranking\nReciprocal rank fusion combines independent result lists.\n\n- Keyword retrieval stays precise.\n- Semantic retrieval finds related language.\n\n## Example\n\`\`\`ts\n${"const score = rank();\n".repeat(80)}\`\`\``,
};

afterEach(() => vi.unstubAllGlobals());

describe("Markdown intelligence chunks", () => {
  it("is deterministic, bounded, heading aware, and preserves metadata", () => {
    const first = chunkMarkdown(note);
    const second = chunkMarkdown(note);
    expect(second).toEqual(first);
    expect(first.length).toBeGreaterThan(1);
    expect(first.every((chunk) => chunk.text.length <= 1600)).toBe(true);
    expect(first[0]).toMatchObject({ heading: "Ranking", tags: ["search", "research"], type: "reference", status: "active" });
    expect(first.some((chunk) => chunk.text.includes("code truncated for retrieval"))).toBe(true);
  });

  it("reuses content hashes across path-only renames", () => {
    const renamed = { ...note, path: "Archive/retrieval.md", revision: "r2" };
    expect(chunkMarkdown(renamed).map((chunk) => chunk.contentHash)).toEqual(chunkMarkdown(note).map((chunk) => chunk.contentHash));
  });
});

describe("embedding provider contract", () => {
  it("returns stable normalized vectors without network access", async () => {
    const provider = new DeterministicEmbeddingProvider(24);
    const [a, b] = await provider.embedDocuments(["semantic retrieval", "semantic retrieval"]);
    expect(a).toEqual(b);
    expect(a).toHaveLength(24);
    expect(Math.hypot(...a)).toBeCloseTo(1, 6);
  });

  it("embeds only changed chunks and reuses unchanged vectors", async () => {
    const provider = new DeterministicEmbeddingProvider(16);
    const initial = await embedChangedChunks(note, provider, new Map());
    const reused = new Map(initial.chunks.map((chunk) => [chunk.contentHash, chunk.embedding]));
    const unchanged = await embedChangedChunks({ ...note, revision: "r2" }, provider, reused);
    expect(unchanged.embedded).toBe(0);
    expect(unchanged.reused).toBe(initial.chunks.length);
    const changed = await embedChangedChunks({ ...note, revision: "r3", markdown: `${note.markdown}\n\nA new retrieval paragraph.` }, provider, reused);
    expect(changed.embedded).toBeGreaterThan(0);
    expect(changed.reused).toBeGreaterThan(0);
  });

  it("rejects malformed provider vectors", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(Response.json({ data: [{ index: 0, embedding: [1, "secret"] }] })));
    const provider = new OpenAICompatibleEmbeddingProvider("fixture", 2, new URL("https://provider.example/embeddings"), "private-key");
    await expect(provider.embedQuery("query")).rejects.toThrow("invalid response");
  });
});

it("keeps the existing filter syntax out of embedding input", () => {
  expect(parseSemanticQuery('vector databases path:"Research Notes" tag:search type:reference status:active')).toEqual({
    text: "vector databases",
    filters: { path: "Research Notes", tag: "search", type: "reference", status: "active" },
    requiresLexicalFilter: false,
  });
});

it("keeps canonical-only filters out of provider input", () => {
  expect(parseSemanticQuery("migration modified:2026-10-01 has:code")).toEqual({
    text: "migration",
    filters: {},
    requiresLexicalFilter: true,
  });
});

describe("hybrid rank fusion", () => {
  const lexical: SearchHit[] = [
    { id: "a", title: "Exact title", path: "a.md", snippet: "lexical", revision: "1" },
    { id: "b", title: "Other", path: "b.md", snippet: "other", revision: "1" },
  ];
  const semantic: SemanticHit[] = [
    { id: "b", title: "Other", path: "b.md", snippet: "meaning", revision: "1", heading: "Idea", semanticRank: 1, similarity: .9, chunkId: "b:1" },
    { id: "c", title: "Concept", path: "c.md", snippet: "concept", revision: "1", heading: null, semanticRank: 2, similarity: .8, chunkId: "c:1" },
  ];

  it("keeps exact titles dominant and marks fused matches", () => {
    const result = fuseResults("Exact title", lexical, semantic, true);
    expect(result[0].id).toBe("a");
    expect(result.find((hit) => hit.id === "b")?.match).toBe("both");
    expect(result.find((hit) => hit.id === "c")?.match).toBe("meaning");
    expect(result.find((hit) => hit.id === "b")?.explanation).toMatchObject({ lexicalRank: 2, semanticRank: 1 });
  });

  it("keeps title boosts when canonical filters are present", () => {
    const result = fuseResults('Exact title path:"Research"', lexical, semantic, true);
    expect(result[0].id).toBe("a");
    expect(result[0].explanation?.fusedScore).toBeGreaterThan(2);
  });

  it("is deterministic for identical inputs", () => {
    expect(fuseResults("query", lexical, semantic)).toEqual(fuseResults("query", lexical, semantic));
  });
});

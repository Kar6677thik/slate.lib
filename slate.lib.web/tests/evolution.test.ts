import { describe, expect, it } from "vitest";
import type { HistoricalNote, HistoryEntry, Note } from "@/lib/api/contracts";
import type { GenerationProvider } from "@/lib/intelligence/ask-types";
import { buildEvolutionPrompt, deriveEvolutionEvent, normalizedMarkdown } from "@/lib/intelligence/evolution";
import { buildEvolution, synthesizeEvolution, type EvolutionCanonical } from "@/lib/intelligence/evolution-service";

const id = "00000000-0000-4000-8000-000000000001";
const note: Note = { id, title: "Sync architecture", path: "Projects/Slate/architecture.md", revision: "r3", markdown: "---\ntype: architecture\nstatus: active\nupdated: 2026-10-03\n---\n# Sync architecture\n\n## Implementation\nWe now use a durable queue because retries must preserve drafts.\n" };
function version(markdown: string, commit: string, timestamp: string, path = note.path) {
  return { noteId: id, title: note.title, path, markdown, revision: commit, commit, timestamp, dateSource: "git" as const, state: "historical" as const };
}

describe("Evolution change extraction", () => {
  it("normalizes line endings, whitespace, and frontmatter ordering", () => {
    expect(normalizedMarkdown("---\nb: 2\na: 1\n---\r\n# A  \r\n\r\nText")).toBe(normalizedMarkdown("---\na: 1\nb: 2\n---\n# A\n\nText\n"));
  });

  it("suppresses cosmetic and typo-sized changes", () => {
    const before = version("# Plan\n\nUse the stable queue.", "a", "2026-09-01T10:00:00Z");
    const after = version("# Plan\n\nUse the stable queues.", "b", "2026-09-02T10:00:00Z");
    expect(deriveEvolutionEvent(before, after)).toBeNull();
  });

  it("detects a documented question answer with a Git-backed date", () => {
    const before = version("---\ntype: question\nstatus: open\n---\n# Retry policy\n\nWhich queue?", "a", "2026-09-01T10:00:00Z");
    const after = version("---\ntype: question\nstatus: answered\n---\n# Retry policy\n\n## Answer\nUse the durable queue.", "b", "2026-09-05T10:00:00Z");
    expect(deriveEvolutionEvent(before, after)).toMatchObject({ type: "QuestionAnswered", confidence: "documented", timestamp: "2026-09-05T10:00:00.000Z", dateSource: "git" });
  });

  it("keeps interpretive changes visibly cautious", () => {
    const before = version("# Direction\n\nFocus on local editing and simple folders.", "a", "2026-09-01T10:00:00Z", "Projects/Slate/direction.md");
    const after = version("# Direction\n\nFocus on collaborative retrieval and shared research workflows.", "b", "2026-09-08T10:00:00Z", "Projects/Slate/direction.md");
    expect(deriveEvolutionEvent(before, after)).toMatchObject({ type: "PossibleShift", label: "Possible shift", confidence: "possible" });
  });

  it("uses explicit rationale only when the note documents it", () => {
    const before = version("---\ntype: architecture\n---\n# Sync\n\n## Implementation\nUse polling.", "a", "2026-09-01T10:00:00Z");
    const after = version("---\ntype: architecture\n---\n# Sync\n\n## Implementation\nWe changed to events.\n\n## Rationale\nEvents reduce unnecessary network work.", "b", "2026-09-08T10:00:00Z");
    expect(deriveEvolutionEvent(before, after)).toMatchObject({ type: "ArchitectureChanged", rationale: "Events reduce unnecessary network work." });
  });
});

describe("bounded retrieval and grounded synthesis", () => {
  const history: HistoryEntry[] = [
    { commit: "b", timestamp: "2026-10-03T10:00:00Z", message: "Use queue", author: "K" },
    { commit: "a", timestamp: "2026-09-01T10:00:00Z", message: "Initial design", author: "K" },
  ];
  const historical = new Map<string, HistoricalNote>([
    ["a", { ...note, commit: "a", timestamp: history[1].timestamp, message: history[1].message, markdown: "---\ntype: architecture\n---\n# Sync architecture\n\n## Implementation\nUse polling." }],
    ["b", { ...note, commit: "b", timestamp: history[0].timestamp, message: history[0].message, markdown: "---\ntype: architecture\n---\n# Sync architecture\n\n## Implementation\nWe changed to a durable queue." }],
  ]);
  const canonical: EvolutionCanonical = {
    async list() { return { path: "Projects/Slate", entries: [{ id, name: "architecture.md", path: note.path, title: note.title, isDirectory: false }], nextPage: null }; },
    async search() { return { query: "sync", page: 0, pageSize: 20, total: 1, results: [{ id, title: note.title, path: note.path, snippet: "sync", revision: note.revision }] }; },
    async note() { return note; },
    async history() { return history; },
    async historical(_id, commit) { return historical.get(commit)!; },
    async semantic() { return [id]; },
  };
  const provider: GenerationProvider = {
    name: "fixture", model: "grounded", available: true, maxInputTokens: 20_000,
    async generate() { return { text: "## Early View\nPolling was used. [S1]\n\n## What Changed\nA durable queue replaced it. [S2]\n\n## Current View\nThe queue is current. [S3]\n\n## Key Turning Points\nThe implementation changed. [S2]\n\n## Unresolved Questions\nReason not documented. [S2]" }; },
    async *streamGenerate() { yield { text: "unused" }; },
  };

  it("builds a bounded snapshot with historical and current evidence", async () => {
    const result = await buildEvolution(canonical, "library", { kind: "topic", topic: "sync" }, provider, new AbortController().signal);
    expect(result.noteCount).toBe(1);
    expect(result.revisionCount).toBe(3);
    expect(result.events.some((event) => event.type === "ArchitectureChanged")).toBe(true);
    expect(result.sources.some((source) => source.state === "historical")).toBe(true);
    expect(result.current[0].revision).toBe("r3");
  });

  it("keeps documents inside untrusted evidence boundaries", async () => {
    const result = await buildEvolution(canonical, "library", { kind: "note", noteId: id }, provider, new AbortController().signal);
    const prompt = buildEvolutionPrompt(result);
    expect(prompt.system).toContain("untrusted evidence");
    expect(prompt.prompt).toContain("BEGIN_UNTRUSTED_DOCUMENT_CONTENT");
  });

  it("caches unchanged synthesis and separates provider identity", async () => {
    const result = await buildEvolution(canonical, "library-cache", { kind: "note", noteId: id }, provider, new AbortController().signal);
    const first = await synthesizeEvolution("library-cache", result, provider, new AbortController().signal);
    const second = await synthesizeEvolution("library-cache", result, provider, new AbortController().signal);
    expect(first.cached).toBe(false);
    expect(second.cached).toBe(true);
    const changedProvider = { ...provider, model: "new-model" };
    expect((await synthesizeEvolution("library-cache", result, changedProvider, new AbortController().signal)).cached).toBe(false);
  });

  it("preserves a useful deterministic result when generation is disabled", async () => {
    const disabled = { ...provider, available: false };
    const result = await buildEvolution(canonical, "library-disabled", { kind: "note", noteId: id }, disabled, new AbortController().signal);
    expect(result.events.length).toBeGreaterThan(0);
    expect(result.degraded).toContain("deterministic timeline");
  });
});


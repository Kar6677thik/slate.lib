import { beforeEach, describe, expect, it } from "vitest";
import type { Links, Note } from "@/lib/api/contracts";
import type { KnowledgeIssue } from "@/lib/intelligence/knowledge-issues";
import { analyzeSmartLinks, applyLinkSuggestionDraft, SMART_LINK_LIMITS } from "@/lib/intelligence/smart-links";
import { readLinkReviews } from "@/lib/storage/link-reviews";

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, "0")}`;
const note = (n: number, title: string, markdown: string, path = `Projects/Slate/${title}.md`): Note => ({ id: id(n), title, markdown, path, revision: `r${n}` });
const emptyLinks = (n: number): Links => ({ noteId: id(n), outgoing: [], backlinks: [] });
const link = (source: number, target: number, title: string): Links => ({ noteId: id(source), outgoing: [{ raw: `[[${title}]]`, target: title, state: "resolved", targetId: id(target), targetPath: `Projects/Slate/${title}.md`, targetTitle: title, kind: "wiki" }], backlinks: [] });
const snapshot = (notes: Note[], links = new Map<string, Links>(), issues: KnowledgeIssue[] = []) => analyzeSmartLinks(notes, { kind: "library" }, { links, issues });

describe("smart link mention extraction and resolution", () => {
  it("finds an exact title mention and preserves visible prose", () => {
    const source = note(1, "Operations", "# Operations\n\nReview Kafka Consumer Groups before rollout."); const target = note(2, "Kafka Consumer Groups", "# Kafka Consumer Groups\n\nRebalancing changes ownership.");
    const item = snapshot([source, target]).suggestions.find((value) => value.suggestionType === "mention")!;
    expect(item.existingText).toBe("Kafka Consumer Groups"); expect(item.suggestedMarkdown).toBe("[[Kafka Consumer Groups]]"); expect(source.markdown.slice(item.sourceRange!.from, item.sourceRange!.to)).toBe(item.existingText);
  });
  it("resolves aliases and generates an alias-preserving wiki link", () => {
    const source = note(3, "Search", "# Search\n\nLucene.NET ranks the results."); const target = note(4, "Slate Search", "---\naliases: [Lucene.NET, Search engine]\n---\n# Slate Search\n\nRanking notes.");
    const item = snapshot([source, target]).suggestions[0]; expect(item.signals).toContain("Exact alias match"); expect(item.suggestedMarkdown).toBe("[[Slate Search|Lucene.NET]]");
  });
  it("normalizes case and spacing for titles", () => { expect(snapshot([note(5, "A", "# A\n\nThe kafka   consumer groups subsystem matters."), note(6, "Kafka Consumer Groups", "# Kafka Consumer Groups")]).suggestions[0]?.target.noteId).toBe(id(6)); });
  it("can resolve a deterministic heading-level target", () => {
    const result = snapshot([note(7, "Runbook", "# Runbook\n\nConsumer Group Rebalancing can interrupt processing."), note(8, "Kafka", "# Kafka\n\n## Consumer Group Rebalancing\n\nMembership changes ownership.")]);
    expect(result.suggestions[0].target.heading).toBe("Consumer Group Rebalancing"); expect(result.suggestions[0].suggestedMarkdown).toContain("#Consumer Group Rebalancing|");
  });
  it("marks multiple exact targets ambiguous and prefers the project-local target", () => {
    const source = note(9, "Deploy", "# Deploy\n\nReview the Platform Guide."); const local = note(10, "Local", "---\nalias: Platform Guide\n---\n# Local", "Projects/Slate/Local.md"); const remote = note(11, "Remote", "---\nalias: Platform Guide\n---\n# Remote", "Reference/Remote.md");
    const item = snapshot([source, remote, local]).suggestions[0]; expect(item.ambiguous).toBe(true); expect(item.target.noteId).toBe(local.id); expect(item.alternateTargets).toHaveLength(1);
  });
  it("suppresses authored links regardless of syntax identity", () => {
    const source = note(12, "Source", "# Source\n\nKafka Consumer Groups are documented here.\n\n[[Kafka Consumer Groups]]"); const target = note(13, "Kafka Consumer Groups", "# Kafka Consumer Groups");
    expect(snapshot([source, target], new Map([[source.id, link(12, 13, target.title)]])).suggestions).toHaveLength(0);
  });
  it("suppresses code, quotes, URLs, frontmatter, and generic terms", () => {
    const source = note(14, "Source", "---\nrelated: Kafka Consumer Groups\n---\n# Source\n\n```txt\nKafka Consumer Groups\n```\n\n> Kafka Consumer Groups\n\nhttps://example.com/Kafka-Consumer-Groups"); const target = note(15, "Kafka Consumer Groups", "# Kafka Consumer Groups"); const generic = note(16, "Server", "# Server");
    expect(snapshot([source, target, generic]).suggestions).toHaveLength(0);
  });
});

describe("relationship and safety signals", () => {
  it("finds a bounded graph triangle only with content evidence", () => {
    const a = note(20, "Consumer pipeline", "# Consumer pipeline\n\n## Rebalancing\nConsumer group membership changes partition ownership.\n\n## Monitoring\nTrack lag and throughput during deployments."); const b = note(21, "Kafka overview", "# Kafka overview\n\nLinks the system."); const c = note(22, "Rebalance recovery", "# Rebalance recovery\n\n## Rebalancing\nConsumer group membership changes partition ownership safely.\n\n## Recovery\nRestart unhealthy consumers and verify assignments.");
    const links = new Map([[a.id, link(20, 21, b.title)], [b.id, link(21, 22, c.title)], [c.id, emptyLinks(22)]]); const item = snapshot([a, b, c], links).suggestions.find((value) => value.target.noteId === c.id);
    expect(item?.suggestionType).toBe("graph_bridge"); expect(item?.signals).toContain("Two-hop authored graph path");
  });
  it("finds same-project relationships with shared claims", () => {
    const a = note(23, "Architecture", "# Architecture\n\n## Architecture\nSlate uses PostgreSQL for derived metadata and Markdown for canonical notes.\n\n## Boundaries\nThe canonical service owns revision checks and Git history."); const b = note(24, "Deployment", "# Deployment\n\n## Architecture\nSlate uses PostgreSQL for derived metadata and Markdown for canonical notes.\n\n## Runtime\nKubernetes runs the service behind Cloudflare.");
    const item = snapshot([a, b]).suggestions.find((value) => value.suggestionType === "project_relationship" || value.suggestionType === "related"); expect(item).toBeTruthy();
  });
  it("does not treat weak semantic-looking proximity as a link", () => { expect(snapshot([note(25, "Storage", "# Storage\n\nFiles are durable."), note(26, "Search", "# Search\n\nIndexes find text.")]).suggestions).toHaveLength(0); });
  it("uses a bounded semantic neighbor only when project and content evidence agree", () => {
    const source = note(251, "Ranking pipeline", "# Ranking pipeline\n\n## Lucene retrieval\nBM25 ranks search results."); const target = note(252, "Search relevance", "# Search relevance\n\n## Lucene scoring\nBM25 ranks candidate documents.");
    const result = analyzeSmartLinks([source, target], { kind: "library" }, { semantic: new Map([[source.id, [target.id]]]) });
    const item = result.suggestions.find((value) => value.sourceNoteId === source.id && value.target.noteId === target.id); expect(item?.suggestionType).toBe("related"); expect(item?.signals).toContain("Strong semantic relation");
  });
  it("recognizes a semantic answer candidate for a project question", () => {
    const source = note(253, "How does ranking work", "---\ntype: question\n---\n# How does ranking work\n\n## Lucene question\nHow are candidate documents ranked?"); const target = note(254, "Ranking explanation", "# Ranking explanation\n\n## Lucene scoring\nBM25 ranks candidate documents.");
    const result = analyzeSmartLinks([source, target], { kind: "library" }, { semantic: new Map([[source.id, [target.id]]]) });
    expect(result.suggestions.find((value) => value.sourceNoteId === source.id && value.target.noteId === target.id)?.suggestionType).toBe("concept_reference");
  });
  it("suppresses ordinary suggestions for duplicate and absorbed pairs", () => {
    const body = "# Architecture\n\n## Storage\nMarkdown is canonical and Git stores history."; expect(snapshot([note(27, "Architecture", body), note(28, "Architecture copy", body)]).suggestions).toHaveLength(0);
  });
  it("retains partial-overlap context on an otherwise useful link", () => {
    const a = note(29, "Network operations", "# Network operations\n\nKafka Deployment Architecture matters.\n\n## Shared\nServices route traffic to pods.\n\n## Runbook\nRestart safely."); const b = note(30, "Kafka Deployment Architecture", "# Kafka Deployment Architecture\n\n## Shared\nServices route traffic to pods.\n\n## Design\nBrokers use persistent disks.");
    const item = snapshot([a, b]).suggestions[0]; expect(item.overlapKind).toBe("partial-overlap");
  });
  it("warns when a suggested pair has a Knowledge Issue", () => {
    const a = note(31, "Runbook", "# Runbook\n\nKafka Architecture is current."); const b = note(32, "Kafka Architecture", "# Kafka Architecture"); const claim = (n: number, text: string) => ({ claimId: `c${n}`, noteId: id(n), revision: `r${n}`, path: n === 31 ? a.path : b.path, title: n === 31 ? a.title : b.title, heading: null, text, normalizedSubject: "kafka", normalizedPredicate: "uses", normalizedObject: text, claimType: "architecture" as const, timestamp: null, current: true, projectPath: "Projects/Slate", component: null, tags: [], contentHash: `h${n}`, extractionMethod: "heading" as const, confidence: "strong" as const, negative: false });
    const issue: KnowledgeIssue = { issueId: "i", fingerprint: "f", kind: "architecture", evidence: "possible-conflict", title: "Conflict", explanation: "differs", state: "open", sourceA: claim(31, "a"), sourceB: claim(32, "b"), staleClaimId: null, projectPath: "Projects/Slate", currentCurrent: true, createdAt: "2026-01-01" };
    const item = snapshot([a, b], new Map(), [issue]).suggestions[0]; expect(item.hasKnowledgeIssue).toBe(true); expect(item.explanation).toContain("currently disagree");
  });
  it("creates a review-only better-target suggestion for superseded authored links", () => {
    const source = note(33, "Index", "# Index\n\nSee [[Original Architecture]]."); const old = note(34, "Original Architecture", "# Original Architecture"); const current = note(35, "Slate Architecture", "# Slate Architecture");
    const claim = (n: number, claimId: string) => ({ claimId, noteId: id(n), revision: `r${n}`, path: n === 34 ? old.path : current.path, title: n === 34 ? old.title : current.title, heading: null, text: "architecture", normalizedSubject: "architecture", normalizedPredicate: "supersedes", normalizedObject: "architecture", claimType: "supersession" as const, timestamp: n === 34 ? "2025-01-01" : "2026-01-01", current: n === 35, projectPath: "Projects/Slate", component: null, tags: [], contentHash: `h${n}`, extractionMethod: "pattern" as const, confidence: "strong" as const, negative: false });
    const issue: KnowledgeIssue = { issueId: "sup", fingerprint: "sup", kind: "supersession", evidence: "likely-superseded", title: "Superseded", explanation: "newer", state: "open", sourceA: claim(34, "old"), sourceB: claim(35, "new"), staleClaimId: "old", projectPath: "Projects/Slate", currentCurrent: false, createdAt: "2026-01-01" };
    const item = snapshot([source, old, current], new Map([[source.id, link(33, 34, old.title)]]), [issue]).suggestions.find((value) => value.suggestionType === "better_target"); expect(item?.target.noteId).toBe(current.id); expect(item?.suggestedMarkdown).toContain("Slate Architecture");
  });
});

describe("insertion, review identity, bounds, and degraded behavior", () => {
  beforeEach(() => localStorage.clear());
  it("applies only when revision, hash, range, and text still match", () => {
    const source = note(40, "Source", "# Source\n\nRead Kafka Consumer Groups."); const target = note(41, "Kafka Consumer Groups", "# Kafka Consumer Groups"); const item = snapshot([source, target]).suggestions[0];
    const applied = applyLinkSuggestionDraft(source.markdown, source.revision, item); expect(applied.ok && applied.markdown).toContain("[[Kafka Consumer Groups]]"); expect(applyLinkSuggestionDraft(source.markdown + " changed", source.revision, item)).toEqual({ ok: false, reason: "Text changed since this suggestion was generated. Refresh suggestions." }); expect(applyLinkSuggestionDraft(source.markdown, "new-revision", item).ok).toBe(false);
  });
  it("produces an unsaved string and never calls a canonical writer", () => { const source = note(42, "Source", "# Source\n\nOpen Slate Search."); const item = snapshot([source, note(43, "Slate Search", "# Slate Search")]).suggestions[0]; expect(applyLinkSuggestionDraft(source.markdown, source.revision, item)).toMatchObject({ ok: true }); expect(source.markdown).not.toContain("[["); });
  it("uses material source context in review fingerprints", () => { const target = note(45, "Slate Search", "# Slate Search"); const one = snapshot([note(44, "A", "# A\n\nOpen Slate Search."), target]).suggestions[0]; const two = snapshot([note(44, "A", "# A\n\nPlease open Slate Search."), target]).suggestions[0]; expect(one.fingerprint).not.toBe(two.fingerprint); });
  it("ignores malformed persisted review values", () => { localStorage.setItem("slate.link-reviews.v1.scope", JSON.stringify({ bad: { status: "insert-all" } })); expect(readLinkReviews("scope")).toEqual({}); });
  it("caps notes, candidates, mentions, and findings without an all-pairs path", () => {
    const notes = Array.from({ length: SMART_LINK_LIMITS.maxNotes + 12 }, (_, index) => note(100 + index, `Component ${index}`, `# Component ${index}\n\nShared architecture component ${index}.`)); const result = snapshot(notes); expect(result.diagnostics.notesAnalyzed).toBe(SMART_LINK_LIMITS.maxNotes); expect(result.diagnostics.bounded).toBe(true); expect(result.diagnostics.candidatesCheckedThisProcess).toBeLessThanOrEqual(SMART_LINK_LIMITS.maxNotes * (SMART_LINK_LIMITS.maxCandidateNeighbors + SMART_LINK_LIMITS.maxMentionsPerNote * SMART_LINK_LIMITS.maxTargetCandidatesPerMention));
  });
  it("is fully useful without a generation provider", () => { const result = snapshot([note(500, "A", "# A\n\nRead Slate Search."), note(501, "Slate Search", "# Slate Search")]); expect(result.suggestions).toHaveLength(1); expect(result.degraded).toContain("no generation provider is required"); expect(result.diagnostics.modelClassifications).toBe(0); });
});

import type { AssetListing, IntelligenceStatus, LinkIssue, Links, Note } from "@/lib/api/contracts";
import type { KnowledgeIssue, KnowledgeReview } from "./knowledge-issues";
import type { KnowledgeOverlap } from "./overlap";
import type { LinkSuggestion } from "./smart-links";
import type { KnowledgeConcept } from "./concepts";
import type { KnowledgeGap } from "./knowledge-gaps";

export const HEALTH_SCHEMA_VERSION = 1;
export const HEALTH_LIMITS = { maxNotes: 120, maxFolders: 120, maxFolderPages: 240, maxLinksPerNote: 120, maxLinkIssues: 80, maxAssets: 80, maxDerivedPerSource: 360, maxItems: 800, defaultPageSize: 25, maxPageSize: 50, sourceConcurrency: 6, cacheMs: 30_000 } as const;

export type HealthCategory = "links" | "consistency" | "overlap" | "relationships" | "structure" | "coverage" | "assets" | "metadata" | "concepts" | "intelligence";
export type HealthPriority = "needs-attention" | "worth-reviewing" | "informational";
export type HealthEvidence = "documented" | "strongly-indicated" | "possible";
export type HealthReviewState = "open" | "resolved" | "dismissed" | "not-relevant";
export type HealthTargetWorkspace = "link-health" | "knowledge-issues" | "knowledge-overlap" | "link-opportunities" | "knowledge-gaps" | "concepts" | "note" | "project-brain" | "assets" | "diagnostics";

export interface LibraryHealthItem {
  id: string; libraryId: string; category: HealthCategory; kind: string; sourceSystem: string; title: string; summary: string;
  noteIds: string[]; noteTitles: string[]; paths: string[]; projectPath: string | null; conceptIds: string[];
  priority: HealthPriority; evidenceLevel: HealthEvidence; createdAt: string | null; updatedAt: string | null;
  reviewState: HealthReviewState; healthOnly: boolean; targetWorkspace: HealthTargetWorkspace;
  targetPayload: Record<string, string>; fingerprint: string; schemaVersion: number; reasons: string[];
}
export interface HealthFilters { page?: number; limit?: number; category?: HealthCategory; priority?: HealthPriority; status?: HealthReviewState; project?: string; concept?: string; noteId?: string; search?: string; }
export interface HealthReviewInput {
  health?: Record<string, { state: HealthReviewState }>;
  knowledge?: Record<string, KnowledgeReview>;
  overlap?: Record<string, { state: string }>;
  links?: Record<string, { status: string }>;
  gaps?: Record<string, { state: string }>;
}
export interface HealthSources {
  libraryId?: string;
  linkIssues?: LinkIssue[]; knowledgeIssues?: KnowledgeIssue[]; overlaps?: KnowledgeOverlap[]; linkSuggestions?: LinkSuggestion[];
  concepts?: KnowledgeConcept[]; knowledgeGaps?: KnowledgeGap[]; notes?: Array<{ note: Note; links: Links }>; assets?: AssetListing[]; assetInventoryComplete?: boolean; intelligence?: IntelligenceStatus;
  failedSources?: string[]; generatedAt?: string;
}
export interface LibraryHealthResponse {
  schemaVersion: number; generatedAt: string; page: number; pageSize: number; total: number; items: LibraryHealthItem[];
  summary: { open: number; priorities: Record<HealthPriority, number>; categories: Record<HealthCategory, number> };
  projects: string[]; concepts: Array<{ id: string; name: string }>;
  diagnostics: { sourcesQueried: number; itemsAggregated: number; openItems: number; needsAttention: number; worthReviewing: number; informational: number; healthOnlyFindings: number; aggregationDurationMs: number; failedSourceQueries: string[]; lastRefresh: string; bounded: boolean };
  degraded?: string;
}

const categories: HealthCategory[] = ["links", "consistency", "overlap", "relationships", "structure", "coverage", "assets", "metadata", "concepts", "intelligence"];
const priorities: HealthPriority[] = ["needs-attention", "worth-reviewing", "informational"];
const priorityRank: Record<HealthPriority, number> = { "needs-attention": 0, "worth-reviewing": 1, informational: 2 };
function hash(value: string) { let h = 2166136261; for (let i = 0; i < value.length; i++) { h ^= value.charCodeAt(i); h = Math.imul(h, 16777619); } return (h >>> 0).toString(36); }
function project(path: string) { const parts = path.replace(/\\/g, "/").split("/"); return parts.length > 1 ? parts.slice(0, Math.min(2, parts.length - 1)).join("/") : null; }
function item(input: Omit<LibraryHealthItem, "id" | "libraryId" | "schemaVersion">): LibraryHealthItem { return { ...input, id: `health-${hash(`${input.sourceSystem}:${input.fingerprint}`)}`, libraryId: "session", schemaVersion: HEALTH_SCHEMA_VERSION }; }
function knowledgeState(issue: KnowledgeIssue, reviews: HealthReviewInput) { const state = reviews.knowledge?.[issue.fingerprint]?.state ?? issue.state; return state === "resolved" ? "resolved" : state === "dismissed" ? "dismissed" : "open"; }
function overlapState(finding: KnowledgeOverlap, reviews: HealthReviewInput) { const state = reviews.overlap?.[finding.fingerprint]?.state ?? finding.state; return state === "resolved" ? "resolved" : state === "dismissed" ? "dismissed" : state === "keep-separate" ? "not-relevant" : "open"; }
function linkState(suggestion: LinkSuggestion, reviews: HealthReviewInput) { const state = reviews.links?.[suggestion.fingerprint]?.status ?? suggestion.status; return state === "inserted" ? "resolved" : state === "dismissed" ? "dismissed" : state === "not-relevant" ? "not-relevant" : "open"; }
function gapState(gap: KnowledgeGap, reviews: HealthReviewInput): HealthReviewState { const state = reviews.gaps?.[gap.fingerprint]?.state ?? gap.reviewState; return state === "addressed" ? "resolved" : state === "dismissed" ? "dismissed" : state === "not-relevant" || state === "intentionally-fragmented" ? "not-relevant" : "open"; }
function healthState(fingerprint: string, reviews: HealthReviewInput): HealthReviewState { return reviews.health?.[fingerprint]?.state ?? "open"; }
function excludedOrphan(note: Note) { const path = note.path.replace(/\\/g, "/").toLowerCase(); const meta = note.markdown.match(/^---\s*\r?\n([\s\S]*?)\r?\n---/)?.[1] ?? ""; return /(^|\/)(daily|inbox|templates?|\.slate)(\/|$)/.test(path) || /(^|\/)(readme|index)\.md$/.test(path) || /^type:\s*(daily|template|index|reference|system)\s*$/im.test(meta) || /^standalone:\s*true\s*$/im.test(meta); }
function body(markdown: string) { return markdown.replace(/^---\s*\r?\n[\s\S]*?\r?\n---\s*/m, "").replace(/^#{1,6}\s+.*$/gm, "").trim(); }
function assetIds(markdown: string) { return [...markdown.matchAll(/(?:asset:\/\/|(?:^|\/)\.assets\/)([a-f0-9-]{36})(?:\.[a-z0-9]+)?/gim)].map((match) => match[1].toLowerCase()); }
function frontmatter(markdown: string) {
  if (!/^---\s*\r?\n/.test(markdown)) return { present: false, valid: true, value: "", id: null as string | null };
  const match = markdown.match(/^---\s*\r?\n([\s\S]*?)\r?\n---(?:\r?\n|$)/);
  if (!match) return { present: true, valid: false, value: "", id: null as string | null };
  const value = match[1];
  const malformed = value.split(/\r?\n/).some((line) => line.trim() && !/^\s*(?:#|[-\w][\w .-]*\s*:|-\s+|\s+)/.test(line));
  const id = value.match(/^id:\s*["']?([^\s"']+)["']?\s*$/im)?.[1] ?? null;
  return { present: true, valid: !malformed, value, id };
}

export function aggregateLibraryHealth(sources: HealthSources, filters: HealthFilters = {}, reviews: HealthReviewInput = {}, startedAt = Date.now()): LibraryHealthResponse {
  const generatedAt = sources.generatedAt ?? new Date().toISOString(); const results: LibraryHealthItem[] = [];
  const frontmatterIds = new Map<string, Array<{ note: Note }>>();
  for (const issue of (sources.linkIssues ?? []).slice(0, HEALTH_LIMITS.maxLinkIssues)) {
    const ambiguous = Boolean(issue.link.candidates?.length); const fingerprint = `canonical-link:${issue.sourceId}:${issue.link.raw}:${[...(issue.link.candidates ?? [])].sort().join("|")}`;
    results.push(item({ category: "links", kind: ambiguous ? "ambiguous-link" : "broken-link", sourceSystem: "canonical-links", title: ambiguous ? "Ambiguous link" : "Broken link", summary: `${issue.sourceTitle} contains ${issue.link.raw}.`, noteIds: [issue.sourceId], noteTitles: [issue.sourceTitle], paths: [issue.sourcePath], projectPath: project(issue.sourcePath), conceptIds: [], priority: "needs-attention", evidenceLevel: "documented", createdAt: null, updatedAt: null, reviewState: healthState(fingerprint, reviews), healthOnly: true, targetWorkspace: "link-health", targetPayload: { noteId: issue.sourceId }, fingerprint, reasons: [ambiguous ? "Multiple deterministic targets match" : "Canonical link target is unresolved"] }));
  }
  for (const issue of (sources.knowledgeIssues ?? []).slice(0, HEALTH_LIMITS.maxDerivedPerSource)) {
    const answered = issue.kind === "answered-question"; const stale = issue.evidence === "possible-stale" || issue.evidence === "likely-superseded" || issue.kind === "supersession";
    const priority: HealthPriority = issue.currentCurrent && ["documented-reversal", "strong-conflict"].includes(issue.evidence) ? "needs-attention" : "worth-reviewing";
    results.push(item({ category: "consistency", kind: answered ? "answered-question" : stale ? "stale-knowledge" : "contradiction", sourceSystem: "knowledge-issues", title: answered ? "Possible answer found" : stale ? "Possibly stale knowledge" : issue.title || "Current contradiction", summary: issue.explanation, noteIds: [issue.sourceA.noteId, issue.sourceB.noteId], noteTitles: [issue.sourceA.title, issue.sourceB.title], paths: [issue.sourceA.path, issue.sourceB.path], projectPath: issue.projectPath, conceptIds: [], priority, evidenceLevel: issue.evidence === "documented-reversal" ? "documented" : issue.evidence === "strong-conflict" || issue.evidence === "likely-superseded" ? "strongly-indicated" : "possible", createdAt: issue.createdAt, updatedAt: null, reviewState: knowledgeState(issue, reviews), healthOnly: false, targetWorkspace: "knowledge-issues", targetPayload: { noteId: issue.sourceA.noteId, fingerprint: issue.fingerprint }, fingerprint: issue.fingerprint, reasons: [issue.evidence.replaceAll("-", " ")] }));
  }
  for (const finding of (sources.overlaps ?? []).slice(0, HEALTH_LIMITS.maxDerivedPerSource)) {
    const exact = finding.kind === "exact-duplicate" || finding.kind === "duplicate-capture"; const priority: HealthPriority = exact ? "needs-attention" : finding.kind === "partial-overlap" || finding.kind === "fragmented" ? "informational" : "worth-reviewing";
    results.push(item({ category: "overlap", kind: finding.kind, sourceSystem: "knowledge-overlap", title: finding.label, summary: finding.explanation, noteIds: [finding.noteA.noteId, finding.noteB.noteId], noteTitles: [finding.noteA.title, finding.noteB.title], paths: [finding.noteA.path, finding.noteB.path], projectPath: finding.projectPath, conceptIds: [], priority, evidenceLevel: finding.classification === "deterministic" && exact ? "documented" : finding.classification === "deterministic" ? "strongly-indicated" : "possible", createdAt: finding.createdAt, updatedAt: null, reviewState: overlapState(finding, reviews), healthOnly: false, targetWorkspace: "knowledge-overlap", targetPayload: { noteId: finding.noteA.noteId, fingerprint: finding.fingerprint }, fingerprint: finding.fingerprint, reasons: finding.deterministicSignals.slice(0, 4) }));
  }
  for (const suggestion of (sources.linkSuggestions ?? []).slice(0, HEALTH_LIMITS.maxDerivedPerSource)) {
    const better = suggestion.suggestionType === "better_target"; const priority: HealthPriority = better || ["mention", "project_relationship", "graph_bridge"].includes(suggestion.suggestionType) ? "worth-reviewing" : "informational";
    results.push(item({ category: better ? "links" : "relationships", kind: better ? "better-target" : "missing-link", sourceSystem: "smart-linking", title: better ? "Better target available" : "Missing relationship", summary: suggestion.explanation, noteIds: [suggestion.sourceNoteId, suggestion.target.noteId], noteTitles: [suggestion.sourceTitle, suggestion.target.title], paths: [suggestion.sourcePath, suggestion.target.path], projectPath: suggestion.projectPath, conceptIds: [], priority, evidenceLevel: suggestion.ambiguous ? "possible" : "strongly-indicated", createdAt: suggestion.createdAt, updatedAt: null, reviewState: linkState(suggestion, reviews), healthOnly: false, targetWorkspace: "link-opportunities", targetPayload: { noteId: suggestion.sourceNoteId, fingerprint: suggestion.fingerprint }, fingerprint: suggestion.fingerprint, reasons: suggestion.signals.slice(0, 4) }));
  }
  for (const gap of (sources.knowledgeGaps ?? []).slice(0, 40)) {
    const operational = gap.kind === "missing-failure-recovery-knowledge" && gap.importanceLevel === "high";
    results.push(item({ category: "coverage", kind: gap.kind, sourceSystem: "knowledge-gaps", title: gap.title, summary: gap.summary, noteIds: gap.noteIds, noteTitles: gap.evidence.map((source) => source.title), paths: gap.evidence.map((source) => source.path), projectPath: gap.projectPath, conceptIds: gap.sourceConceptIds, priority: operational ? "needs-attention" : "worth-reviewing", evidenceLevel: gap.importanceLevel === "high" ? "strongly-indicated" : "possible", createdAt: null, updatedAt: null, reviewState: gapState(gap, reviews), healthOnly: false, targetWorkspace: "knowledge-gaps", targetPayload: { fingerprint: gap.fingerprint, ...(gap.conceptId ? { concept: gap.conceptId } : {}), ...(gap.projectPath ? { project: gap.projectPath } : {}) }, fingerprint: gap.fingerprint, reasons: gap.signals.slice(0, 4) }));
  }
  const knownAssets = new Set((sources.assets ?? []).map((asset) => asset.metadata.id.toLowerCase()));
  for (const { note, links } of (sources.notes ?? []).slice(0, HEALTH_LIMITS.maxNotes)) {
    const metadata = frontmatter(note.markdown);
    if (metadata.id) frontmatterIds.set(metadata.id, [...(frontmatterIds.get(metadata.id) ?? []), { note }]);
    if (metadata.present && !metadata.valid) { const fingerprint = `invalid-frontmatter:${note.id}`; results.push(item({ category: "metadata", kind: "invalid-frontmatter", sourceSystem: "canonical-notes", title: "Invalid note metadata", summary: `${note.title} has malformed or unterminated frontmatter.`, noteIds: [note.id], noteTitles: [note.title], paths: [note.path], projectPath: project(note.path), conceptIds: [], priority: "needs-attention", evidenceLevel: "documented", createdAt: null, updatedAt: null, reviewState: healthState(fingerprint, reviews), healthOnly: true, targetWorkspace: "note", targetPayload: { noteId: note.id }, fingerprint, reasons: ["Canonical Markdown frontmatter could not be parsed safely"] })); }
    if (!excludedOrphan(note) && !links.backlinks.length && !links.outgoing.some((link) => link.state === "resolved")) { const fingerprint = `orphan:${note.id}`; results.push(item({ category: "structure", kind: "orphan-note", sourceSystem: "canonical-graph", title: "Orphan note", summary: `${note.title} has no meaningful incoming or outgoing authored links.`, noteIds: [note.id], noteTitles: [note.title], paths: [note.path], projectPath: project(note.path), conceptIds: [], priority: "informational", evidenceLevel: "documented", createdAt: null, updatedAt: null, reviewState: healthState(fingerprint, reviews), healthOnly: true, targetWorkspace: "note", targetPayload: { noteId: note.id }, fingerprint, reasons: ["No authored backlinks", "No resolved outgoing links"] })); }
    if (!body(note.markdown)) { const fingerprint = `empty:${note.id}`; results.push(item({ category: "structure", kind: "empty-note", sourceSystem: "canonical-notes", title: "Empty or heading-only note", summary: `${note.title} contains no prose after metadata and headings.`, noteIds: [note.id], noteTitles: [note.title], paths: [note.path], projectPath: project(note.path), conceptIds: [], priority: "worth-reviewing", evidenceLevel: "documented", createdAt: null, updatedAt: null, reviewState: healthState(fingerprint, reviews), healthOnly: true, targetWorkspace: "note", targetPayload: { noteId: note.id }, fingerprint, reasons: ["No meaningful body content"] })); }
    if (sources.assetInventoryComplete !== false) for (const assetId of assetIds(note.markdown)) if (!knownAssets.has(assetId)) { const fingerprint = `missing-asset:${note.id}:${assetId}`; results.push(item({ category: "assets", kind: "missing-asset", sourceSystem: "canonical-assets", title: "Missing asset", summary: `${note.title} references an asset that is not present.`, noteIds: [note.id], noteTitles: [note.title], paths: [note.path], projectPath: project(note.path), conceptIds: [], priority: "needs-attention", evidenceLevel: "documented", createdAt: null, updatedAt: null, reviewState: healthState(fingerprint, reviews), healthOnly: true, targetWorkspace: "assets", targetPayload: { noteId: note.id, assetId }, fingerprint, reasons: ["Referenced asset ID is absent from current metadata"] })); }
  }
  for (const [frontmatterId, notes] of frontmatterIds) if (notes.length > 1) {
    const ordered = notes.map(({ note }) => note).sort((a, b) => a.id.localeCompare(b.id)); const fingerprint = `duplicate-metadata-id:${frontmatterId}:${ordered.map((note) => note.id).join("|")}`;
    results.push(item({ category: "metadata", kind: "duplicate-note-id", sourceSystem: "canonical-notes", title: "Duplicate note metadata ID", summary: `${ordered.length} notes declare the same frontmatter ID, ${frontmatterId}.`, noteIds: ordered.map((note) => note.id), noteTitles: ordered.map((note) => note.title), paths: ordered.map((note) => note.path), projectPath: project(ordered[0].path), conceptIds: [], priority: "needs-attention", evidenceLevel: "documented", createdAt: null, updatedAt: null, reviewState: healthState(fingerprint, reviews), healthOnly: true, targetWorkspace: "note", targetPayload: { noteId: ordered[0].id }, fingerprint, reasons: ["A canonical metadata identifier must identify one note"] }));
  }
  for (const asset of (sources.assets ?? []).slice(0, HEALTH_LIMITS.maxAssets)) {
    const meta = asset.metadata; const invalid = !meta.originalFilename || !meta.contentType || !meta.sha256 || !meta.extension; const fingerprint = `${invalid ? "invalid" : "unused"}-asset:${meta.id}:${meta.sha256}`;
    if (invalid || asset.referenceCount === 0) results.push(item({ category: "assets", kind: invalid ? "invalid-asset-metadata" : "unreferenced-asset", sourceSystem: "canonical-assets", title: invalid ? "Invalid asset metadata" : "Unreferenced asset", summary: invalid ? `${meta.originalFilename || meta.id} has incomplete metadata.` : `${meta.originalFilename} has no current canonical note references.`, noteIds: [], noteTitles: [], paths: [], projectPath: null, conceptIds: [], priority: invalid ? "needs-attention" : "worth-reviewing", evidenceLevel: "documented", createdAt: meta.createdAt, updatedAt: null, reviewState: healthState(fingerprint, reviews), healthOnly: true, targetWorkspace: "assets", targetPayload: { assetId: meta.id }, fingerprint, reasons: [invalid ? "Required asset metadata is incomplete" : "Reference count is zero"] }));
  }
  for (const concept of (sources.concepts ?? []).filter((candidate) => candidate.aliases.length > 1).slice(0, 40)) { const fingerprint = `concept-alias:${concept.id}:${concept.contentHash}:${[...concept.aliases].sort().join("|")}`; results.push(item({ category: "concepts", kind: "ambiguous-concept-identity", sourceSystem: "concepts", title: "Concept identity needs review", summary: `${concept.canonicalName} has several derived aliases: ${concept.aliases.join(", ")}.`, noteIds: concept.members.slice(0, 8).map((member) => member.noteId), noteTitles: concept.members.slice(0, 8).map((member) => member.title), paths: concept.members.slice(0, 8).map((member) => member.path), projectPath: concept.projectPaths[0] ?? null, conceptIds: [concept.id], priority: "worth-reviewing", evidenceLevel: "possible", createdAt: null, updatedAt: concept.lastSeen, reviewState: healthState(fingerprint, reviews), healthOnly: true, targetWorkspace: "concepts", targetPayload: { concept: concept.canonicalName }, fingerprint, reasons: ["Multiple aliases require human identity review"] })); }
  if (sources.intelligence?.failedJobs || sources.intelligence?.failedChunksThisRun) { const status = sources.intelligence, fingerprint = `intelligence:${status.failedJobs}:${status.failedChunksThisRun}:${status.lastIndexedAt ?? "never"}`; results.push(item({ category: "intelligence", kind: "failed-indexing", sourceSystem: "intelligence-jobs", title: "Indexing needs attention", summary: `${status.failedJobs} persistent job${status.failedJobs === 1 ? "" : "s"} and ${status.failedChunksThisRun} failed chunk${status.failedChunksThisRun === 1 ? "" : "s"} are reported.`, noteIds: [], noteTitles: [], paths: [], projectPath: null, conceptIds: [], priority: "needs-attention", evidenceLevel: "documented", createdAt: null, updatedAt: status.lastIndexedAt, reviewState: "open", healthOnly: false, targetWorkspace: "diagnostics", targetPayload: {}, fingerprint, reasons: [status.lastError ?? "Persistent indexing failure"] })); }

  for (const healthItem of results) healthItem.libraryId = sources.libraryId ?? "session";
  const conceptMembership = new Map<string, string[]>();
  for (const concept of sources.concepts ?? []) for (const member of concept.members) conceptMembership.set(member.noteId, [...(conceptMembership.get(member.noteId) ?? []), concept.id]);
  for (const healthItem of results) healthItem.conceptIds = [...new Set([...healthItem.conceptIds, ...healthItem.noteIds.flatMap((noteId) => conceptMembership.get(noteId) ?? [])])];
  const ordered = results.sort((a, b) => priorityRank[a.priority] - priorityRank[b.priority] || (a.createdAt ?? "").localeCompare(b.createdAt ?? "") || a.title.localeCompare(b.title) || a.fingerprint.localeCompare(b.fingerprint));
  const bounded = ordered.length > HEALTH_LIMITS.maxItems; const inventory = ordered.slice(0, HEALTH_LIMITS.maxItems);
  const visible = inventory.filter((value) => !filters.category || value.category === filters.category).filter((value) => !filters.priority || value.priority === filters.priority).filter((value) => !filters.status || value.reviewState === filters.status).filter((value) => !filters.project || value.projectPath === filters.project || value.paths.some((path) => path === filters.project || path.startsWith(`${filters.project}/`))).filter((value) => !filters.concept || value.conceptIds.includes(filters.concept) || value.summary.toLowerCase().includes(filters.concept.toLowerCase())).filter((value) => !filters.noteId || value.noteIds.includes(filters.noteId)).filter((value) => !filters.search || `${value.title} ${value.summary} ${value.noteTitles.join(" ")} ${value.paths.join(" ")} ${value.projectPath ?? ""}`.toLowerCase().includes(filters.search.toLowerCase()));
  const open = visible.filter((value) => value.reviewState === "open"); const page = Math.max(0, Math.trunc(filters.page ?? 0)); const pageSize = Math.min(HEALTH_LIMITS.maxPageSize, Math.max(1, Math.trunc(filters.limit ?? HEALTH_LIMITS.defaultPageSize))); const start = page * pageSize;
  const count = <T extends string>(values: readonly T[], pick: (value: LibraryHealthItem) => T) => Object.fromEntries(values.map((key) => [key, open.filter((value) => pick(value) === key).length])) as Record<T, number>;
  const failed = sources.failedSources ?? [];
  return { schemaVersion: HEALTH_SCHEMA_VERSION, generatedAt, page, pageSize, total: visible.length, items: visible.slice(start, start + pageSize), summary: { open: open.length, priorities: count(priorities, (value) => value.priority), categories: count(categories, (value) => value.category) }, projects: [...new Set(inventory.flatMap((value) => value.projectPath ? [value.projectPath] : []))].sort(), concepts: (sources.concepts ?? []).slice(0, 100).map((concept) => ({ id: concept.id, name: concept.canonicalName })), diagnostics: { sourcesQueried: 9, itemsAggregated: inventory.length, openItems: open.length, needsAttention: open.filter((value) => value.priority === "needs-attention").length, worthReviewing: open.filter((value) => value.priority === "worth-reviewing").length, informational: open.filter((value) => value.priority === "informational").length, healthOnlyFindings: inventory.filter((value) => value.healthOnly).length, aggregationDurationMs: Math.max(0, Date.now() - startedAt), failedSourceQueries: failed, lastRefresh: generatedAt, bounded }, degraded: failed.length ? `Partial Library Health is shown. Unavailable sources: ${failed.join(", ")}.` : undefined };
}

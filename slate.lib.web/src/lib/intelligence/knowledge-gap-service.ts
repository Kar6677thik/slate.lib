import type { Links } from "@/lib/api/contracts";
import { buildConceptSnapshot } from "./concepts";
import { analyzeKnowledgeGaps, KNOWLEDGE_GAP_LIMITS, orderKnowledgeGaps, pageKnowledgeGaps, type GapFilters, type GapReviews, type KnowledgeGap } from "./knowledge-gaps";
import { buildKnowledgeSnapshot } from "./knowledge-issues";
import { collectKnowledgeNotes, type KnowledgeCanonical } from "./knowledge-issues-service";
import { analyzeOverlaps } from "./overlap";
import { PostgresDerivedStore } from "./postgres-store";
import { analyzeSmartLinks } from "./smart-links";

export interface GapCanonical extends KnowledgeCanonical { links(id: string, signal: AbortSignal): Promise<Links>; }
const store = new PostgresDerivedStore();
const session = new Map<string, { expires: number; hits: number; analysis: ReturnType<typeof analyzeKnowledgeGaps>; dirtyNoteIds: Set<string>; dirtyPaths: Set<string> }>();

function projectOf(path: string) { const parts = path.replace(/\\/g, "/").split("/"); return parts.length > 1 ? parts.slice(0, Math.min(2, parts.length - 1)).join("/") : null; }
function findingCounts(gaps: KnowledgeGap[]) { return { thinCoverage: gaps.filter((gap) => gap.kind === "thin-coverage").length, fragmentedCoverage: gaps.filter((gap) => gap.kind === "fragmented-coverage").length, missingOverview: gaps.filter((gap) => gap.kind === "missing-overview").length, bridgeGaps: gaps.filter((gap) => gap.kind === "missing-bridge-knowledge" || gap.kind === "integration-gap").length, recurringQuestions: gaps.filter((gap) => gap.kind === "recurring-unanswered-question").length, deterministicFindings: gaps.length }; }

async function mapLimited<T, R>(values: T[], concurrency: number, operation: (value: T) => Promise<R>) {
  const results: R[] = new Array(values.length); let cursor = 0;
  await Promise.all(Array.from({ length: Math.min(concurrency, values.length) }, async () => { while (cursor < values.length) { const index = cursor++; results[index] = await operation(values[index]); } }));
  return results;
}

export async function knowledgeGapSnapshot(libraryId: string, canonical: GapCanonical, filters: GapFilters, reviews: GapReviews, signal: AbortSignal, options: { rebuild?: boolean; semanticAvailable?: boolean; pending?: number; failed?: number } = {}) {
  const cached = session.get(libraryId);
  const hasDirtyScope = !!cached && (cached.dirtyNoteIds.size > 0 || cached.dirtyPaths.size > 0);
  if (!options.rebuild && cached && !hasDirtyScope && cached.expires > Date.now()) { cached.hits++; return pageKnowledgeGaps(cached.analysis, filters, reviews, cached.hits); }
  const notes = (await collectKnowledgeNotes(canonical, { kind: "library" }, signal)).slice(0, KNOWLEDGE_GAP_LIMITS.maxNotes);
  let postgresAvailable = true; let derived: Awaited<ReturnType<PostgresDerivedStore["readHealthDerived"]>> | null = null;
  try { derived = await store.readHealthDerived(libraryId); } catch { postgresAvailable = false; }
  const knowledge = buildKnowledgeSnapshot(notes, { kind: "library" });
  const issues = derived?.knowledgeIssues.length ? derived.knowledgeIssues : knowledge.issues;
  const overlaps = derived?.overlaps.length ? derived.overlaps : analyzeOverlaps(notes, { kind: "library" }).findings;
  let links = new Map<string, Links>(), suggestions = derived?.linkSuggestions ?? [];
  if (!suggestions.length) {
    const values = await mapLimited(notes, KNOWLEDGE_GAP_LIMITS.sourceConcurrency, (note) => canonical.links(note.id, signal).catch(() => ({ noteId: note.id, outgoing: [], backlinks: [] })));
    links = new Map(notes.map((note, index) => [note.id, values[index]]));
  }
  const concepts = derived?.concepts.length ? derived.concepts : buildConceptSnapshot(notes, { links, issues }).concepts;
  if (!suggestions.length) suggestions = analyzeSmartLinks(notes, { kind: "library" }, { links, issues, overlaps }).suggestions;
  let analysis: ReturnType<typeof analyzeKnowledgeGaps>;
  if (!options.rebuild && cached && hasDirtyScope) {
    const dirtyNoteIds = cached.dirtyNoteIds, affectedProjects = new Set(cached.dirtyPaths); for (const note of notes) if (dirtyNoteIds.has(note.id)) { const project = projectOf(note.path); if (project) affectedProjects.add(project); }
    const affectedConcepts = new Set(concepts.filter((concept) => concept.members.some((member) => dirtyNoteIds.has(member.noteId))).map((concept) => concept.id));
    for (const gap of cached.analysis.gaps) if (gap.noteIds.some((id) => dirtyNoteIds.has(id))) { if (gap.projectPath) affectedProjects.add(gap.projectPath); for (const id of gap.sourceConceptIds) affectedConcepts.add(id); for (const source of gap.evidence) if (dirtyNoteIds.has(source.noteId)) { const project = projectOf(source.path); if (project) affectedProjects.add(project); } }
    for (const concept of concepts) if (affectedConcepts.has(concept.id) || concept.relationships.some((relationship) => affectedConcepts.has(relationship.targetConceptId))) { affectedConcepts.add(concept.id); for (const relationship of concept.relationships) affectedConcepts.add(relationship.targetConceptId); }
    const partial = analyzeKnowledgeGaps({ libraryId, notes, concepts, issues, overlaps, linkSuggestions: suggestions, semanticAvailable: options.semanticAvailable, postgresAvailable, pending: options.pending, failed: options.failed, conceptScopeIds: [...affectedConcepts], projectScopePaths: [...affectedProjects] });
    const retained = cached.analysis.gaps.filter((gap) => !gap.noteIds.some((id) => dirtyNoteIds.has(id)) && (!gap.projectPath || !affectedProjects.has(gap.projectPath)) && !gap.sourceConceptIds.some((id) => affectedConcepts.has(id)));
    const gaps = orderKnowledgeGaps([...retained, ...partial.gaps]).slice(0, KNOWLEDGE_GAP_LIMITS.maxFindings);
    analysis = { ...partial, gaps, diagnostics: { ...partial.diagnostics, ...findingCounts(gaps), boundsReached: partial.diagnostics.boundsReached || cached.analysis.diagnostics.boundsReached } };
  } else analysis = analyzeKnowledgeGaps({ libraryId, notes, concepts, issues, overlaps, linkSuggestions: suggestions, semanticAvailable: options.semanticAvailable, postgresAvailable, pending: options.pending, failed: options.failed });
  session.set(libraryId, { expires: Date.now() + KNOWLEDGE_GAP_LIMITS.cacheMs, hits: 0, analysis, dirtyNoteIds: new Set(), dirtyPaths: new Set() });
  try { await store.replaceKnowledgeGaps(libraryId, analysis.gaps); }
  catch { analysis.degraded = "Knowledge gaps are available for this session, but PostgreSQL persistence is unavailable."; analysis.diagnostics.postgresAvailable = false; }
  return pageKnowledgeGaps(analysis, filters, reviews, 0);
}

export function invalidateKnowledgeGapCache(libraryId: string) { session.delete(libraryId); }
export async function invalidateKnowledgeGapNote(libraryId: string, noteId?: string, path?: string) { const cached = session.get(libraryId); if (!cached || (!noteId && !path)) session.delete(libraryId); else { if (noteId) cached.dirtyNoteIds.add(noteId); const project = path ? projectOf(path) : null; if (project) cached.dirtyPaths.add(project); cached.expires = 0; } if (noteId) try { await store.invalidateKnowledgeGapNote(libraryId, noteId); } catch {} }

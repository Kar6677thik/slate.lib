import type { Links, Note } from "@/lib/api/contracts";
import { collectKnowledgeNotes, type KnowledgeCanonical } from "./knowledge-issues-service";
import { buildKnowledgeSnapshot } from "./knowledge-issues";
import { analyzeOverlaps } from "./overlap";
import { analyzeSmartLinks, SMART_LINK_LIMITS, type LinkOpportunitySnapshot } from "./smart-links";
import { PostgresDerivedStore } from "./postgres-store";

export interface SmartLinkCanonical extends KnowledgeCanonical { links(id: string, signal: AbortSignal): Promise<Links>; semantic?(query: string, path: string): Promise<string[]>; }
const store = new PostgresDerivedStore();

async function mapLimited<T, R>(values: T[], concurrency: number, operation: (value: T) => Promise<R>) {
  const results: R[] = new Array(values.length); let cursor = 0;
  await Promise.all(Array.from({ length: Math.min(concurrency, values.length) }, async () => { while (cursor < values.length) { const index = cursor++; results[index] = await operation(values[index]); } }));
  return results;
}

export async function smartLinkSnapshot(libraryId: string, canonical: SmartLinkCanonical, scope: LinkOpportunitySnapshot["scope"], signal: AbortSignal) {
  const notes = (await collectKnowledgeNotes(canonical, scope, signal)).slice(0, SMART_LINK_LIMITS.maxNotes);
  const linkValues = await mapLimited(notes, 5, async (note) => canonical.links(note.id, signal).catch(() => ({ noteId: note.id, outgoing: [], backlinks: [] })));
  const links = new Map(notes.map((note, index) => [note.id, linkValues[index]]));
  const semantic = new Map<string, string[]>();
  if (canonical.semantic) {
    const sources = (scope.kind === "note" ? notes.filter((note) => note.id === scope.noteId) : notes).slice(0, SMART_LINK_LIMITS.maxSemanticSources);
    const values = await mapLimited(sources, 3, async (note) => canonical.semantic!(`${note.title}\n${note.markdown.slice(0, 700)}`, note.path.split("/").slice(0, -1).join("/")).catch(() => []));
    sources.forEach((note, index) => semantic.set(note.id, values[index].filter((id) => id !== note.id).slice(0, SMART_LINK_LIMITS.maxSemanticNeighbors)));
  }
  const knowledge = buildKnowledgeSnapshot(notes, scope); const overlaps = analyzeOverlaps(notes, scope).findings;
  const snapshot = analyzeSmartLinks(notes, scope, { links, issues: knowledge.issues, overlaps, semantic });
  try { await store.replaceLinkSuggestions(libraryId, notes.map((note) => note.id), snapshot.suggestions); }
  catch { snapshot.degraded = "Link discovery is available for this session, but the derived database is unavailable."; }
  return snapshot;
}

export async function invalidateSmartLinks(libraryId: string, note: Note) {
  try { await store.invalidateLinkSuggestions(libraryId, note.id); } catch { /* Derived relationship state is optional. */ }
}

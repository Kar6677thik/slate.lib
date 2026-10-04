import type { Links, Note } from "@/lib/api/contracts";
import { collectKnowledgeNotes, type KnowledgeCanonical } from "./knowledge-issues-service";
import { buildKnowledgeSnapshot } from "./knowledge-issues";
import { analyzeOverlaps } from "./overlap";
import { analyzeSmartLinks } from "./smart-links";
import { buildConceptPage, buildConceptSnapshot, CONCEPT_LIMITS, type ConceptIdentityReview } from "./concepts";
import { PostgresDerivedStore } from "./postgres-store";

export interface ConceptCanonical extends KnowledgeCanonical { links(id: string, signal: AbortSignal): Promise<Links>; }
const store = new PostgresDerivedStore();
const session = new Map<string, ReturnType<typeof buildConceptSnapshot>>();

async function mapLimited<T, R>(values: T[], concurrency: number, operation: (value: T) => Promise<R>) {
  const results: R[] = new Array(values.length); let cursor = 0;
  await Promise.all(Array.from({ length: Math.min(concurrency, values.length) }, async () => { while (cursor < values.length) { const index = cursor++; results[index] = await operation(values[index]); } }));
  return results;
}

export async function conceptSnapshot(libraryId: string, canonical: ConceptCanonical, signal: AbortSignal, reviews: ConceptIdentityReview = {}) {
  const notes = (await collectKnowledgeNotes(canonical, { kind: "library" }, signal)).slice(0, CONCEPT_LIMITS.maxNotes);
  const linkValues = await mapLimited(notes, 5, (note) => canonical.links(note.id, signal).catch(() => ({ noteId: note.id, outgoing: [], backlinks: [] })));
  const links = new Map(notes.map((note, index) => [note.id, linkValues[index]]));
  const knowledge = buildKnowledgeSnapshot(notes, { kind: "library" });
  const snapshot = buildConceptSnapshot(notes, { links, issues: knowledge.issues, reviews });
  session.set(libraryId, snapshot);
  try { await store.replaceConceptIndex(libraryId, snapshot.concepts); }
  catch { snapshot.degraded = "Concept pages are available for this session, but the derived database is unavailable."; }
  return { snapshot, notes, links };
}

export async function conceptPage(libraryId: string, canonical: ConceptCanonical, identity: string, signal: AbortSignal, reviews: ConceptIdentityReview = {}) {
  const { snapshot, notes, links } = await conceptSnapshot(libraryId, canonical, signal, reviews);
  const knowledge = buildKnowledgeSnapshot(notes, { kind: "library" });
  const overlaps = analyzeOverlaps(notes, { kind: "library" }).findings;
  const linkOpportunities = analyzeSmartLinks(notes, { kind: "library" }, { links, issues: knowledge.issues, overlaps }).suggestions;
  return buildConceptPage(snapshot, identity, { issues: knowledge.issues, overlaps, linkOpportunities }) ;
}

export async function persistConceptNote(libraryId: string, note: Note) {
  session.delete(libraryId);
  try { await store.replaceConceptNote(libraryId, note.id, buildConceptSnapshot([note]).concepts); }
  catch { /* Derived concept state is optional. */ }
}

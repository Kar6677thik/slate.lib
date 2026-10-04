import type { Note } from "@/lib/api/contracts";
import { buildConceptSnapshot } from "./concepts";
import { analyzeKnowledgeGaps } from "./knowledge-gaps";
import { buildKnowledgeSnapshot } from "./knowledge-issues";
import { collectKnowledgeNotes, type KnowledgeCanonical } from "./knowledge-issues-service";
import { analyzeOverlaps } from "./overlap";
import { analyzeSmartLinks } from "./smart-links";
import type { SmartLinkCanonical } from "./smart-links-service";
import { analyzeInboxCapture, INBOX_TRIAGE_LIMITS, INBOX_TRIAGE_SCHEMA_VERSION, inboxTriageHash, orderInboxTriage, type InboxTriageAnalysis, type InboxTriageSnapshot } from "./inbox-triage";
import { PostgresDerivedStore } from "./postgres-store";

const cache = new Map<string, { key: string; value: InboxTriageAnalysis; expires: number }>();
const store = new PostgresDerivedStore();

async function mapLimited<T, R>(values: T[], concurrency: number, operation: (value: T) => Promise<R>) {
  const results: R[] = new Array(values.length); let cursor = 0;
  await Promise.all(Array.from({ length: Math.min(concurrency, values.length) }, async () => { while (cursor < values.length) { const index = cursor++; results[index] = await operation(values[index]); } }));
  return results;
}

export async function inboxTriageSnapshot(libraryId: string, canonical: KnowledgeCanonical & Partial<Pick<SmartLinkCanonical,"semantic">>, signal: AbortSignal): Promise<InboxTriageSnapshot> {
  const notes = (await collectKnowledgeNotes(canonical, { kind: "library" }, signal)).slice(0, INBOX_TRIAGE_LIMITS.candidateNotes);
  const captures = notes.filter((note) => /(^|\/)inbox\//i.test(note.path.replace(/\\/g, "/"))).slice(0, INBOX_TRIAGE_LIMITS.capturesPerPass);
  const knowledge = buildKnowledgeSnapshot(notes, { kind: "library" });
  const overlaps = analyzeOverlaps(notes, { kind: "project", path: "inbox" }).findings;
  const concepts = buildConceptSnapshot(notes, { issues: knowledge.issues, overlaps }).concepts;
  const links = analyzeSmartLinks(notes, { kind: "project", path: "inbox" }, { issues: knowledge.issues, overlaps }).suggestions;
  const semantic = new Map<string,string[]>();
  if (canonical.semantic) {
    const sources = captures.slice(0,INBOX_TRIAGE_LIMITS.semanticSources);
    const values = await mapLimited(sources,Math.min(2,INBOX_TRIAGE_LIMITS.concurrency),async (capture) => canonical.semantic!(`${capture.title}\n${capture.markdown.slice(0,700)}`,"").catch(() => []));
    sources.forEach((capture,index) => semantic.set(capture.id,values[index].filter((id) => id !== capture.id).slice(0,INBOX_TRIAGE_LIMITS.semanticNeighbors)));
  }
  const semanticAvailable=[...semantic.values()].some((ids) => ids.length > 0);
  const gaps = analyzeKnowledgeGaps({ libraryId, notes, concepts, issues: knowledge.issues, overlaps, linkSuggestions: links, semanticAvailable, postgresAvailable: false }).gaps;
  let cacheHits = 0;
  const items = captures.map((capture: Note) => {
    const neighborhood = [
      ...overlaps.filter((item) => item.noteA.noteId === capture.id || item.noteB.noteId === capture.id).map((item) => item.fingerprint),
      ...links.filter((item) => item.sourceNoteId === capture.id).map((item) => item.fingerprint),
      ...concepts.filter((concept) => concept.members.some((member) => member.noteId === capture.id)).map((concept) => concept.contentHash),
      ...(semantic.get(capture.id) ?? []),
    ].sort().join("|");
    const key = `v${INBOX_TRIAGE_SCHEMA_VERSION}:${inboxTriageHash(capture.markdown)}:${inboxTriageHash(neighborhood)}`;
    const previous = cache.get(`${libraryId}:${capture.id}`);
    if (previous?.key === key && previous.expires > Date.now()) { cacheHits++; return previous.value; }
    const value = analyzeInboxCapture({ libraryId, capture, notes, concepts, overlaps, links, gaps, semanticNoteIds:semantic.get(capture.id) });
    cache.set(`${libraryId}:${capture.id}`, { key, value, expires: Date.now() + 5 * 60_000 });
    return value;
  });
  const ordered = orderInboxTriage(items);
  let postgresAvailable=true;
  try { await store.replaceInboxTriage(libraryId,ordered); } catch { postgresAvailable=false; }
  return {
    schemaVersion: INBOX_TRIAGE_SCHEMA_VERSION,
    generatedAt: new Date().toISOString(),
    items: ordered,
    diagnostics: {
      inboxItems: captures.length,
      analyzed: ordered.length,
      pending: Math.max(0, captures.length - ordered.length),
      failed: 0,
      strongProjectSuggestions: ordered.filter((item) => item.possibleProjects[0]?.score >= 70).length,
      overlapMatches: ordered.reduce((sum, item) => sum + item.overlapFindings.length, 0),
      questionSuggestions: ordered.filter((item) => item.suggestedType === "Question").length,
      appendOpportunities: ordered.filter((item) => item.suggestedActions.some((action) => action.action === "append")).length,
      deterministicClassifications: ordered.length,
      optionalModelClassifications: 0,
      cacheHits,
      boundsReached: notes.length >= INBOX_TRIAGE_LIMITS.candidateNotes || captures.length >= INBOX_TRIAGE_LIMITS.capturesPerPass,
    },
    degraded: postgresAvailable ? semanticAvailable ? "Deterministic Inbox triage is active with bounded semantic retrieval. Remote generation is not used when Inbox opens." : "Deterministic Inbox triage is active. Remote generation is not used when Inbox opens; semantic-only matches are unavailable." : "Deterministic Inbox triage is active for this session. PostgreSQL persistence and semantic-only matches are unavailable.",
  };
}

export function invalidateInboxTriage(libraryId: string, noteId?: string) {
  if (noteId) cache.delete(`${libraryId}:${noteId}`);
  else for (const key of cache.keys()) if (key.startsWith(`${libraryId}:`)) cache.delete(key);
}

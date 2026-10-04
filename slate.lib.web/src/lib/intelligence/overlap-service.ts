import type { Note } from "@/lib/api/contracts";
import { analyzeOverlaps, buildOverlapNote, OVERLAP_LIMITS, type OverlapSnapshot } from "./overlap";
import { collectKnowledgeNotes, type KnowledgeCanonical } from "./knowledge-issues-service";
import { buildKnowledgeSnapshot } from "./knowledge-issues";
import { classifyOverlapFindings } from "./overlap-classifier";
import { getGenerationProvider } from "./generation-provider";
import { PostgresDerivedStore } from "./postgres-store";

const store = new PostgresDerivedStore();

export async function overlapSnapshot(libraryId: string, canonical: KnowledgeCanonical, scope: OverlapSnapshot["scope"], signal: AbortSignal) {
  const notes = await collectKnowledgeNotes(canonical, scope, signal);
  const snapshot = analyzeOverlaps(notes.slice(0, OVERLAP_LIMITS.maxNotes), scope);
  const model = await classifyOverlapFindings(libraryId, snapshot.findings, getGenerationProvider(), signal);
  snapshot.findings = model.findings;
  snapshot.diagnostics.modelClassifications = model.classified;
  snapshot.diagnostics.cacheHits = model.cached;
  snapshot.diagnostics.failedJobs = model.failed;
  snapshot.degraded = model.unavailable ? "Deterministic overlap analysis is active. Optional model classification is unavailable." : model.failed ? "Deterministic overlap analysis is active. Some optional classifications could not be validated." : undefined;
  const knowledge = buildKnowledgeSnapshot(notes, scope);
  const conflictPairs = new Set(knowledge.issues.map((issue) => [issue.sourceA.noteId, issue.sourceB.noteId].sort().join("|")));
  snapshot.findings = snapshot.findings.map((item) => ({ ...item, hasKnowledgeIssue: conflictPairs.has([item.noteA.noteId, item.noteB.noteId].sort().join("|")) }));
  const signatures = notes.map(buildOverlapNote);
  try {
    await store.replaceOverlapNotes(libraryId, notes.map((note) => note.id), signatures);
    await store.replaceOverlapFindings(libraryId, snapshot.findings);
  } catch { snapshot.degraded = "Overlap analysis is available for this session, but the derived database is unavailable."; }
  return snapshot;
}

export async function persistOverlapNote(libraryId: string, note: Note) {
  try { await store.replaceOverlapNotes(libraryId, [note.id], [buildOverlapNote(note)]); } catch { /* Derived overlap storage is optional. */ }
}

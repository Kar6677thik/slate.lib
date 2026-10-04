import type { FolderPage, Note } from "@/lib/api/contracts";
import { buildKnowledgeSnapshot, extractKnowledgeClaims, KNOWLEDGE_LIMITS, type KnowledgeSnapshot } from "./knowledge-issues";
import { PostgresDerivedStore } from "./postgres-store";
import { getGenerationProvider } from "./generation-provider";
import { classifyKnowledgePairs } from "./knowledge-classifier";

export interface KnowledgeCanonical {
  list(path: string, page: number, signal: AbortSignal): Promise<FolderPage>;
  note(id: string, signal: AbortSignal): Promise<Note>;
}

const store = new PostgresDerivedStore();

export async function collectKnowledgeNotes(canonical: KnowledgeCanonical, scope: KnowledgeSnapshot["scope"], signal: AbortSignal) {
  const focused = scope.kind === "note" && scope.noteId ? await canonical.note(scope.noteId, signal) : null;
  const pending = [scope.kind === "project" ? scope.path ?? "" : ""];
  const notes: Note[] = [];
  while (pending.length && notes.length < KNOWLEDGE_LIMITS.maxNotes) {
    const folder = pending.shift()!;
    let page = 0;
    do {
      const listing = await canonical.list(folder, page, signal);
      for (const entry of listing.entries) {
        if (entry.isDirectory) pending.push(entry.path);
        else if (entry.id && notes.length < KNOWLEDGE_LIMITS.maxNotes) notes.push(await canonical.note(entry.id, signal));
      }
      if (listing.nextPage === null) break;
      page = listing.nextPage;
    } while (notes.length < KNOWLEDGE_LIMITS.maxNotes);
  }
  if (focused && !notes.some((note) => note.id === focused.id)) notes.unshift(focused);
  return notes;
}

export async function knowledgeSnapshot(libraryId: string, canonical: KnowledgeCanonical, scope: KnowledgeSnapshot["scope"], signal: AbortSignal) {
  const notes = await collectKnowledgeNotes(canonical, scope, signal);
  const snapshot = buildKnowledgeSnapshot(notes, scope);
  const claims = notes.flatMap((note) => extractKnowledgeClaims(note));
  const model = await classifyKnowledgePairs(libraryId, claims, snapshot.issues, getGenerationProvider(), signal);
  snapshot.issues = [...snapshot.issues, ...model.issues].slice(0, KNOWLEDGE_LIMITS.maxIssues);
  snapshot.diagnostics.aiClassifiedPairs = model.classified;
  snapshot.diagnostics.cachedClassifications = model.cached;
  snapshot.diagnostics.failedAnalysis = model.failed;
  snapshot.diagnostics.openIssues = snapshot.issues.filter((issue) => issue.state === "open" || issue.state === "snoozed").length;
  snapshot.degraded = model.unavailable
    ? "Deterministic analysis is active. Optional model classification is unavailable."
    : model.failed
      ? "Deterministic analysis is active. Some optional model classifications could not be validated."
      : undefined;
  if (scope.kind === "note") snapshot.issues = snapshot.issues.filter((issue) => issue.sourceA.noteId === scope.noteId || issue.sourceB.noteId === scope.noteId);
  try {
    await store.replaceKnowledgeClaims(libraryId, notes.map((note) => note.id), claims);
    await store.replaceKnowledgeIssues(libraryId, snapshot.issues);
  } catch {
    snapshot.degraded = "Knowledge analysis is available for this session, but the derived database is unavailable.";
  }
  return snapshot;
}

export async function persistNoteClaims(libraryId: string, note: Note) {
  try { await store.replaceKnowledgeClaims(libraryId, [note.id], extractKnowledgeClaims(note)); } catch { /* Search can keep working when derived analysis is unavailable. */ }
}

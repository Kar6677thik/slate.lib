import type { Note, SearchHit } from "@/lib/api/contracts";
import { chunkMarkdown } from "./chunking";
import { getEmbeddingProvider } from "./provider";
import { PostgresDerivedStore } from "./postgres-store";
import { fuseResults } from "./ranking";
import { parseSemanticQuery } from "./query";
import type { EmbeddingProvider, HybridSearchResponse, IndexEvent, SearchMode } from "./types";
import type { SourceCandidate } from "./ask-context";

const provider = getEmbeddingProvider();
const store = new PostgresDerivedStore();
const runStats = new Map<string, { embedded: number; reused: number; failed: number }>();

function stats(libraryId: string) {
  let value = runStats.get(libraryId);
  if (!value) {
    value = { embedded: 0, reused: 0, failed: 0 };
    runStats.set(libraryId, value);
  }
  return value;
}

export async function embedChangedChunks(note: Note, embeddingProvider: EmbeddingProvider, reusable: Map<string, number[]>) {
  const chunks = chunkMarkdown(note);
  const missing = chunks.filter((chunk) => !reusable.has(chunk.contentHash));
  const vectors: number[][] = [];
  for (let index = 0; index < missing.length; index += 24) vectors.push(...await embeddingProvider.embedDocuments(missing.slice(index, index + 24).map((chunk) => chunk.text)));
  const generated = new Map(missing.map((chunk, index) => [chunk.contentHash, vectors[index]]));
  return { chunks: chunks.map((chunk) => ({ ...chunk, embedding: reusable.get(chunk.contentHash) ?? generated.get(chunk.contentHash)!, provider: embeddingProvider.name, model: embeddingProvider.model })), embedded: missing.length, reused: chunks.length - missing.length };
}

export async function indexNote(libraryId: string, note: Note) {
  if (!provider.available) return { indexed: 0, embedded: 0, reused: 0 };
  const chunkMetadata = chunkMarkdown(note);
  try {
    await store.initialize(provider);
    const reusable = await store.getReusableEmbeddings(libraryId, chunkMetadata.map((chunk) => chunk.contentHash), provider);
    const result = await embedChangedChunks(note, provider, reusable);
    await store.replaceNote(libraryId, note, result.chunks);
    const current = stats(libraryId);
    current.embedded += result.embedded;
    current.reused += result.reused;
    return { indexed: result.chunks.length, embedded: result.embedded, reused: result.reused };
  } catch (caught) {
    stats(libraryId).failed += chunkMetadata.length;
    throw caught;
  }
}

export async function enqueueIndexEvent(libraryId: string, event: IndexEvent) {
  if (!provider.available) return;
  await store.initialize(provider);
  await store.enqueue(libraryId, event);
}

export async function hybridSearch(
  libraryId: string,
  query: string,
  mode: SearchMode,
  lexical: SearchHit[],
  diagnostics = false,
  page = 0,
  pageSize = 20,
  lexicalTotal = lexical.length,
): Promise<HybridSearchResponse> {
  const start = page * pageSize;
  if (mode === "lexical") return { query, mode, effectiveMode: "lexical", page, pageSize, total: lexicalTotal, results: fuseResults(query, lexical, [], diagnostics).slice(start, start + pageSize) };
  try {
    await store.initialize(provider);
    const parsed = parseSemanticQuery(query);
    if (!parsed.text || parsed.requiresLexicalFilter) throw new Error("This filter is evaluated by canonical keyword search");
    const candidateLimit = Math.min(Math.max((page + 1) * pageSize * 3, 30), 80);
    const semantic = await store.semanticSearch(libraryId, await provider.embedQuery(parsed.text), parsed.text, candidateLimit, parsed.filters);
    const results = mode === "semantic" ? fuseResults(query, [], semantic, diagnostics) : fuseResults(query, lexical, semantic, diagnostics);
    return { query, mode, effectiveMode: mode, page, pageSize, total: mode === "hybrid" ? Math.max(lexicalTotal, results.length) : results.length, results: results.slice(start, start + pageSize) };
  } catch {
    const results = fuseResults(query, lexical, [], diagnostics);
    return { query, mode, effectiveMode: "lexical", page, pageSize, total: lexicalTotal, results: results.slice(start, start + pageSize), degraded: "Meaning search is unavailable. Keyword results are shown." };
  }
}

export async function intelligenceStatus(libraryId: string) {
  const status = await store.status(libraryId, provider);
  const current = stats(libraryId);
  return { ...status, embeddedThisRun: current.embedded, reusedThisRun: current.reused, failedChunksThisRun: current.failed };
}
export function removeIndexedNote(libraryId: string, noteId?: string, path?: string) { return store.deleteNote(libraryId, noteId, path); }
export function finishIndexEvent(libraryId: string, event: IndexEvent) { return store.finish(libraryId, event); }
export function failIndexEvent(libraryId: string, event: IndexEvent) { return store.fail(libraryId, event); }
export async function claimIndexEvents(libraryId: string) {
  if (!provider.available) return [];
  await store.initialize(provider);
  return store.claim(libraryId);
}
export function pruneIndexedNotes(libraryId: string, noteIds: string[]) { return store.pruneNotes(libraryId, noteIds); }
export function reconciliationEnabled(libraryId: string) { return store.reconciliationEnabled(libraryId); }
export function enableReconciliation(libraryId: string) { return store.enableReconciliation(libraryId); }
export function indexedNoteIds(libraryId: string) { return store.indexedNoteIds(libraryId); }

export async function semanticAskCandidates(libraryId: string, query: string, path?: string): Promise<SourceCandidate[]> {
  if (!provider.available) return [];
  try {
    await store.initialize(provider);
    const parsed = parseSemanticQuery(path ? `${query} path:${JSON.stringify(path)}` : query);
    if (!parsed.text) return [];
    const hits = await store.semanticSearch(libraryId, await provider.embedQuery(parsed.text), parsed.text, 36, parsed.filters);
    return hits.map((hit) => ({
      noteId: hit.id,
      title: hit.title,
      path: hit.path,
      heading: hit.heading,
      ordinal: Number(hit.chunkId.split(":").at(-1)) || 0,
      revision: hit.revision,
      excerpt: hit.snippet,
      score: Math.max(0, hit.similarity),
    }));
  } catch {
    return [];
  }
}

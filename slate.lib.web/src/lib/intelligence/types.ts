import type { Note, SearchHit } from "@/lib/api/contracts";

export type SearchMode = "hybrid" | "lexical" | "semantic";
export type IndexState = "ready" | "pending" | "indexing" | "unavailable" | "failed";

export interface ChunkMetadata {
  id: string;
  noteId: string;
  path: string;
  title: string;
  heading: string | null;
  ordinal: number;
  text: string;
  contentHash: string;
  revision: string;
  tags: string[];
  type: string | null;
  status: string | null;
}

export interface EmbeddedChunk extends ChunkMetadata {
  embedding: number[];
  provider: string;
  model: string;
}

export interface EmbeddingProvider {
  readonly name: string;
  readonly model: string;
  readonly dimensions: number;
  readonly available: boolean;
  embedQuery(text: string, signal?: AbortSignal): Promise<number[]>;
  embedDocuments(texts: string[], signal?: AbortSignal): Promise<number[][]>;
}

export interface SemanticHit extends SearchHit {
  heading: string | null;
  semanticRank: number;
  similarity: number;
  chunkId: string;
}

export interface SearchExplanation {
  lexicalRank?: number;
  semanticRank?: number;
  fusedScore: number;
  matchedFields: string[];
  chunkId?: string;
  matchedHeading?: string | null;
}

export interface HybridHit extends SearchHit {
  heading?: string | null;
  match: "keyword" | "meaning" | "both";
  explanation?: SearchExplanation;
}

export interface HybridSearchResponse {
  query: string;
  mode: SearchMode;
  effectiveMode: SearchMode;
  page: number;
  pageSize: number;
  total: number;
  results: HybridHit[];
  degraded?: string;
}

export interface IntelligenceStatus {
  enabled: boolean;
  state: IndexState;
  provider: string;
  model: string;
  dimensions: number;
  noteCount: number;
  chunkCount: number;
  pendingJobs: number;
  failedJobs: number;
  embeddedThisRun: number;
  reusedThisRun: number;
  failedChunksThisRun: number;
  lastIndexedAt: string | null;
  lastError: string | null;
}

export type IndexEvent =
  | { kind: "upsert"; noteId: string }
  | { kind: "delete"; noteId?: string; path?: string }
  | { kind: "rename"; path: string; destinationPath?: string }
  | { kind: "reconcile"; reason: "sync" | "refresh" | "bulk" | "restore" | "rebuild" };

export interface DerivedStore {
  initialize(provider: EmbeddingProvider): Promise<void>;
  getReusableEmbeddings(libraryId: string, hashes: string[], provider: EmbeddingProvider): Promise<Map<string, number[]>>;
  replaceNote(libraryId: string, note: Note, chunks: EmbeddedChunk[]): Promise<void>;
  deleteNote(libraryId: string, noteId?: string, path?: string): Promise<void>;
  semanticSearch(libraryId: string, vector: number[], query: string, limit: number, filters?: { path?: string; tag?: string; type?: string; status?: string }): Promise<SemanticHit[]>;
  status(libraryId: string, provider: EmbeddingProvider): Promise<IntelligenceStatus>;
  enqueue(libraryId: string, event: IndexEvent): Promise<void>;
}

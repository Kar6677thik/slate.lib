import { z } from "zod";
import type { AskRequest, AskStreamEvent } from "@/lib/intelligence/ask-types";
export type { AskPolicy, AskRequest, AskScope, AskScopeKind, AskSource, AskStreamEvent, AskTurn } from "@/lib/intelligence/ask-types";
export type { ProjectBrainSnapshot, ProjectEvidence, ProjectSynthesis, ProjectTimelineEvent } from "@/lib/intelligence/project-brain";
export type { EvolutionConfidence, EvolutionCurrentView, EvolutionEvent, EvolutionEventType, EvolutionScope, EvolutionSnapshot, EvolutionSource, EvolutionSynthesis } from "@/lib/intelligence/evolution";
export type { KnowledgeClaim, KnowledgeDiagnostics, KnowledgeIssue, KnowledgeReview, KnowledgeSnapshot, ReviewState } from "@/lib/intelligence/knowledge-issues";
export type { KnowledgeOverlap, OverlapDiagnostics, OverlapKind, OverlapNote, OverlapReviewState, OverlapSection, OverlapSnapshot } from "@/lib/intelligence/overlap";
export type { LinkDiagnostics, LinkOpportunitySnapshot, LinkSuggestion, LinkSuggestionStatus, LinkSuggestionType, LinkTarget } from "@/lib/intelligence/smart-links";
export type { ConceptDiagnostics, ConceptIdentityReview, ConceptKind, ConceptMember, ConceptPageSnapshot, ConceptRelationship, ConceptRelationshipType, ConceptSnapshot, ConceptStrength, KnowledgeConcept } from "@/lib/intelligence/concepts";
export type { HealthCategory, HealthEvidence, HealthFilters, HealthPriority, HealthReviewInput, HealthReviewState, HealthTargetWorkspace, LibraryHealthItem, LibraryHealthResponse } from "@/lib/intelligence/health";
export type { GapAction, GapCoverageLevel, GapCoverageSource, GapDiagnostics, GapFilters, GapImportance, GapReviewState, GapReviews, GapWorkspace, KnowledgeGap, KnowledgeGapKind, KnowledgeGapResponse } from "@/lib/intelligence/knowledge-gaps";
export type { InboxAction, InboxContentType, InboxReviewState, InboxTriageAnalysis, InboxTriageDiagnostics, InboxTriageRelatedNote, InboxTriageSnapshot, InboxTriageSuggestion, SuggestionReviewState, TriageConfidence } from "@/lib/intelligence/inbox-triage";
export const noteSchema = z.object({
  id: z.string().uuid(),
  path: z.string(),
  title: z.string(),
  markdown: z.string(),
  revision: z.string(),
});
export type Note = z.infer<typeof noteSchema>;
export interface Entry {
  name: string;
  path: string;
  isDirectory: boolean;
  id: string | null;
  title: string | null;
}
export interface FolderPage {
  path: string;
  entries: Entry[];
  nextPage: number | null;
}
export interface Status {
  libraryId: string;
  noteCount: number;
  serverVersion: string;
  git?: { state: string; pending: boolean; detail?: string };
  indexState: string;
}
export interface SearchHit {
  id: string;
  title: string;
  path: string;
  snippet: string;
  revision: string;
  score?: number;
}
export interface SearchPage {
  query: string;
  page: number;
  pageSize: number;
  total: number;
  results: SearchHit[];
  searchVersion?: number;
}
export type SearchMode = "hybrid" | "lexical" | "semantic";
export interface HybridSearchHit extends SearchHit {
  heading?: string | null;
  match: "keyword" | "meaning" | "both";
  explanation?: {
    lexicalRank?: number;
    semanticRank?: number;
    fusedScore: number;
    matchedFields: string[];
    chunkId?: string;
    matchedHeading?: string | null;
  };
}
export interface HybridSearchPage extends Omit<SearchPage, "results"> {
  mode: SearchMode;
  effectiveMode: SearchMode;
  results: HybridSearchHit[];
  degraded?: string;
}
export interface IntelligenceStatus {
  enabled: boolean;
  state: "ready" | "pending" | "indexing" | "unavailable" | "failed";
  provider: string;
  model: string;
  dimensions: number;
  noteCount: number;
  totalNotes: number;
  chunkCount: number;
  pendingJobs: number;
  failedJobs: number;
  embeddedThisRun: number;
  reusedThisRun: number;
  failedChunksThisRun: number;
  lastIndexedAt: string | null;
  lastError: string | null;
  askEnabled: boolean;
  generationProvider: string;
  generationModel: string;
  askDefaultPolicy: "strict";
  askRequestsThisRun: number;
  askRetrievedChunksThisRun: number;
  askInputTokensThisRun?: number;
  askOutputTokensThisRun?: number;
  askActiveRequests: number;
  askMaxConcurrent: number;
}

export type AskStreamHandler = (event: AskStreamEvent) => void;
export type AskPayload = AskRequest;
export interface NoteLink {
  raw: string;
  target: string;
  label?: string;
  heading?: string;
  state: string;
  targetId: string | null;
  targetPath: string | null;
  targetTitle: string | null;
  kind: string;
}
export interface Links {
  noteId: string;
  outgoing: NoteLink[];
  backlinks: {
    sourceId: string;
    sourceTitle: string;
    sourcePath: string;
    heading?: string | null;
  }[];
}
export interface HistoryEntry {
  commit: string;
  timestamp: string;
  message: string;
  author: string;
}
export interface HistoricalNote {
  id: string;
  path: string;
  title: string;
  markdown: string;
  timestamp: string;
  message: string;
  commit: string;
}
export interface Asset {
  id: string;
  originalFilename: string;
  contentType: string;
  byteSize: number;
  extension: string;
  inlineImage: boolean;
}
export interface Mutation {
  path: string;
  isDirectory: boolean;
  id: string | null;
  affectedItems: number;
}
export interface Connection {
  server: string;
  token: string;
}

export interface LinkIssue {
  sourceId: string;
  sourceTitle: string;
  sourcePath: string;
  sourceRevision: string;
  link: NoteLink & { start: number; length: number; candidates?: string[] };
}
export interface LinkIssuePage {
  page: number;
  total: number;
  results: LinkIssue[];
}
export interface LinkReplacement {
  start: number;
  length: number;
  original: string;
  proposed: string;
  targetId: string;
  targetPath: string;
}
export interface NoteTextPreview {
  id: string;
  path: string;
  revision: string;
  originalMarkdown: string;
  proposedMarkdown: string;
  changes: LinkReplacement[];
}
export interface BulkItem {
  sourcePath: string;
  destinationPath: string | null;
  isDirectory: boolean;
}
export interface BulkPreview {
  operationId: string;
  operation: string;
  items: BulkItem[];
  noteCount: number;
  fingerprint: string;
  repairs?: NoteTextPreview[] | null;
}
export interface BulkResult {
  operationId: string;
  state: string;
  items: BulkItem[];
  noteCount: number;
}
export interface RecoverableNote {
  id: string;
  path: string;
  title: string;
  sourceCommit: string;
  deletionCommit: string;
  deletedAt: string;
}
export interface RecoveryPage {
  notes: RecoverableNote[];
  bounded: boolean;
}
export interface GraphNode {
  id: string;
  title: string;
  path: string;
  type: string | null;
  status: string | null;
  depth: number;
}
export interface GraphEdge {
  source: string;
  target: string;
}
export interface KnowledgeGraph {
  focus: string;
  nodes: GraphNode[];
  edges: GraphEdge[];
  limited: boolean;
}
export interface RelatedNote {
  id: string;
  title: string;
  path: string;
  score: number;
  reasons: string[];
}
export interface RediscoveryHit {
  id: string;
  path: string;
  title: string;
  reason: string;
  date: string | null;
}
export interface RediscoveryPage {
  view: string;
  page: number;
  total: number;
  results: RediscoveryHit[];
}
export interface AssetListing {
  metadata: Asset & { createdAt: string; sha256: string };
  referenceCount: number;
}
export interface AssetPage {
  page: number;
  total: number;
  totalBytes: number;
  results: AssetListing[];
}
export interface AssetReference {
  noteId: string;
  title: string;
  path: string;
}
export interface AssetReferencePage {
  page: number;
  total: number;
  results: AssetReference[];
}
export interface AssetDerivedText {
  assetId: string;
  sha256: string;
  kind: string;
  state: string;
  text: string;
  updatedAt: string;
  error?: string | null;
  jobId?: string | null;
}

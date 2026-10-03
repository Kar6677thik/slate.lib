export type AskScopeKind = "library" | "note" | "folder" | "selected" | "project";
export type AskPolicy = "strict" | "general";

export interface AskScope {
  kind: AskScopeKind;
  noteId?: string;
  path?: string;
  noteIds?: string[];
  selectedText?: string;
}

export interface AskTurn {
  role: "user" | "assistant";
  text: string;
}

export interface AskSource {
  citationId: string;
  noteId: string;
  title: string;
  path: string;
  heading: string | null;
  ordinal: number;
  revision: string;
  excerpt: string;
  score: number;
}

export interface AskRequest {
  question: string;
  scope: AskScope;
  policy: AskPolicy;
  conversation: AskTurn[];
}

export type AskStreamEvent =
  | { type: "retrieval"; message: string }
  | { type: "sources"; sources: AskSource[] }
  | { type: "delta"; text: string }
  | { type: "replace"; text: string }
  | { type: "done"; citations: string[]; usage?: { inputTokens?: number; outputTokens?: number } }
  | { type: "error"; message: string; partial: boolean };

export interface GenerationInput {
  system: string;
  prompt: string;
  maxOutputTokens: number;
}

export interface GenerationUsage {
  inputTokens?: number;
  outputTokens?: number;
}

export interface GenerationProvider {
  readonly name: string;
  readonly model: string;
  readonly available: boolean;
  readonly maxInputTokens: number;
  generate(input: GenerationInput, signal?: AbortSignal): Promise<{ text: string; usage?: GenerationUsage }>;
  streamGenerate(input: GenerationInput, signal?: AbortSignal): AsyncIterable<{ text?: string; usage?: GenerationUsage }>;
}

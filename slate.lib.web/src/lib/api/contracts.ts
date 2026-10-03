import { z } from "zod";
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
}
export interface SearchPage {
  query: string;
  page: number;
  pageSize: number;
  total: number;
  results: SearchHit[];
}
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
  backlinks: { sourceId: string; sourceTitle: string; sourcePath: string }[];
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

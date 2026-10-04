import { createHash } from "node:crypto";
import type { Entry, FolderPage, HistoricalNote, HistoryEntry, Note, SearchPage } from "@/lib/api/contracts";
import type { GenerationProvider } from "./ask-types";
import {
  EVOLUTION_LIMITS,
  EVOLUTION_SCHEMA_VERSION,
  buildEvolutionPrompt,
  currentVersion,
  currentView,
  deriveEvolutionEvent,
  evolutionCacheKey,
  historicalVersion,
  type EvolutionEvent,
  type EvolutionScope,
  type EvolutionSnapshot,
  type EvolutionSource,
  type EvolutionSynthesis,
} from "./evolution";

export interface EvolutionCanonical {
  list(path: string, page: number, signal: AbortSignal): Promise<FolderPage>;
  search(query: string, signal: AbortSignal): Promise<SearchPage>;
  note(id: string, signal: AbortSignal): Promise<Note>;
  history(id: string, signal: AbortSignal): Promise<HistoryEntry[]>;
  historical(id: string, commit: string, signal: AbortSignal): Promise<HistoricalNote>;
  semantic?(query: string, path?: string): Promise<string[]>;
}

type CacheValue = EvolutionSynthesis & { libraryId: string; noteIds: string[]; paths: string[] };
const generatedCache = new Map<string, CacheValue>();
const MAX_CACHE_ENTRIES = 60;

export function clearEvolutionCache(libraryId?: string, noteId?: string, path?: string) {
  if (!libraryId) { generatedCache.clear(); return; }
  for (const [key, value] of generatedCache) {
    if (value.libraryId !== libraryId) continue;
    const pathMatch = path && value.paths.some((candidate) => candidate === path || candidate.startsWith(`${path}/`) || path.startsWith(`${candidate}/`));
    if (!noteId && !path || noteId && value.noteIds.includes(noteId) || pathMatch) generatedCache.delete(key);
  }
}

function safePath(path: string) {
  const value = path.replace(/\\/g, "/").replace(/^\/+|\/+$/g, "");
  if (!value || value.length > 1024 || value.split("/").some((part) => !part || part === "." || part === "..")) throw new Error("Invalid evolution path");
  return value;
}

async function listNotes(canonical: EvolutionCanonical, root: string, signal: AbortSignal) {
  const folders = [root];
  const files: Entry[] = [];
  let bounded = false;
  while (folders.length && folders.length + files.length < 1_000) {
    const folder = folders.shift()!;
    let page = 0;
    do {
      const listing = await canonical.list(folder, page, signal);
      for (const entry of listing.entries) {
        if (entry.isDirectory) folders.push(entry.path);
        else if (entry.id) files.push(entry);
        if (folders.length + files.length >= 1_000) { bounded = true; break; }
      }
      if (bounded || listing.nextPage === null) break;
      page = listing.nextPage;
    } while (true);
  }
  if (folders.length) bounded = true;
  return { files, bounded };
}

function candidateScore(entry: Entry, topic: string) {
  const target = `${entry.title ?? ""} ${entry.path}`.toLowerCase();
  const terms = topic.toLowerCase().match(/[\p{L}\p{N}_-]+/gu) ?? [];
  return terms.reduce((score, term) => score + (target.includes(term) ? 8 : 0), 0)
    + (/decision|adr|architecture|question|status|readme|overview/i.test(entry.path) ? 5 : 0)
    - entry.path.split("/").length / 10;
}

async function resolveNotes(canonical: EvolutionCanonical, scope: EvolutionScope, signal: AbortSignal) {
  let ids: string[] = [];
  let bounded = false;
  if (scope.kind === "note") ids = [scope.noteId];
  else if (scope.kind === "selected") ids = scope.noteIds.slice(0, EVOLUTION_LIMITS.maxCandidateNotes);
  else if (scope.kind === "project" || scope.kind === "folder") {
    const path = safePath(scope.path);
    const listed = await listNotes(canonical, path, signal);
    bounded = listed.bounded || listed.files.length > EVOLUTION_LIMITS.maxCandidateNotes;
    const topic = scope.topic?.trim() ?? "";
    const lexical = topic ? await canonical.search(`${topic} path:${JSON.stringify(path)}`, signal).catch(() => ({ results: [] } as unknown as SearchPage)) : { results: [] };
    const semantic = topic ? await canonical.semantic?.(topic, path).catch(() => []) ?? [] : [];
    const ranked = listed.files.slice().sort((a, b) => candidateScore(b, topic) - candidateScore(a, topic) || a.path.localeCompare(b.path));
    ids = [...new Set([...lexical.results.map((item) => item.id), ...semantic, ...ranked.map((item) => item.id!)])].slice(0, EVOLUTION_LIMITS.maxCandidateNotes);
  } else {
    const topic = scope.kind === "topic" ? scope.topic.trim() : "";
    if (!topic) return { notes: [], bounded: false };
    const filter = scope.path ? ` path:${JSON.stringify(safePath(scope.path))}` : "";
    const [lexical, semantic] = await Promise.all([
      canonical.search(`${topic}${filter}`, signal).catch(() => ({ results: [] } as unknown as SearchPage)),
      canonical.semantic?.(topic, scope.path ? safePath(scope.path) : undefined).catch(() => []) ?? [],
    ]);
    bounded = lexical.results.length > EVOLUTION_LIMITS.maxCandidateNotes;
    ids = [...new Set([...lexical.results.map((item) => item.id), ...semantic])].slice(0, EVOLUTION_LIMITS.maxCandidateNotes);
  }
  const notes = (await Promise.all(ids.map((id) => canonical.note(id, signal).catch(() => null)))).filter((note): note is Note => Boolean(note));
  return { notes, bounded };
}

function sourceKey(source: EvolutionSource) {
  return `${source.noteId}:${source.revision}:${source.heading ?? ""}:${source.state}`;
}

function assignCitations(events: EvolutionEvent[], current: ReturnType<typeof currentView>[]) {
  const all: EvolutionSource[] = [];
  const byKey = new Map<string, EvolutionSource>();
  const add = (value: EvolutionSource) => {
    const key = sourceKey(value);
    const existing = byKey.get(key);
    if (existing) return existing;
    const next = { ...value, citationId: `S${all.length + 1}` };
    all.push(next); byKey.set(key, next); return next;
  };
  for (const event of events) {
    if (event.before) event.before = add(event.before);
    event.after = add(event.after);
  }
  for (const item of current) add({ citationId: "", noteId: item.noteId, title: item.title, path: item.path, revision: item.revision, commit: null, timestamp: item.updatedAt, heading: null, excerpt: item.excerpt, state: "current" });
  return all;
}

function eventsWithinSourceBudget(events: EvolutionEvent[], current: ReturnType<typeof currentView>[]) {
  const keys = new Set(current.map((item) => `${item.noteId}:${item.revision}::current`));
  const selected: EvolutionEvent[] = [];
  for (const event of events) {
    const additions = [event.before, event.after].filter((item): item is EvolutionSource => Boolean(item)).map(sourceKey).filter((key) => !keys.has(key));
    if (keys.size + new Set(additions).size > EVOLUTION_LIMITS.maxSources) continue;
    for (const key of additions) keys.add(key);
    selected.push(event);
  }
  return selected;
}

function scopeTitle(scope: EvolutionScope, notes: Note[]) {
  if (scope.kind === "topic") return scope.topic || "Evolution of Thought";
  if (scope.kind === "note") return notes[0]?.title ?? "Note evolution";
  if (scope.kind === "selected") return `${notes.length} selected notes`;
  return scope.topic ? `${scope.topic} in ${scope.path}` : scope.path.split("/").at(-1) ?? scope.path;
}

export async function buildEvolution(canonical: EvolutionCanonical, libraryId: string, scope: EvolutionScope, provider: Pick<GenerationProvider, "available" | "name" | "model">, signal: AbortSignal): Promise<EvolutionSnapshot> {
  const resolved = await resolveNotes(canonical, scope, signal);
  const notes = resolved.notes;
  const events: EvolutionEvent[] = [];
  const fingerprintParts: string[] = [];
  let revisionCount = 0;
  let bounded = resolved.bounded || notes.length >= EVOLUTION_LIMITS.maxCandidateNotes;
  await Promise.all(notes.slice(0, EVOLUTION_LIMITS.maxHistoryNotes).map(async (note) => {
    const history = (await canonical.history(note.id, signal).catch(() => [])).slice(0, EVOLUTION_LIMITS.maxRevisionsPerNote);
    if (history.length >= EVOLUTION_LIMITS.maxRevisionsPerNote) bounded = true;
    fingerprintParts.push(`${note.id}:${note.revision}:${history.map((entry) => entry.commit).join(",")}`);
    const versions = (await Promise.all(history.map((entry) => canonical.historical(note.id, entry.commit, signal).catch(() => null))))
      .filter((item): item is HistoricalNote => Boolean(item))
      .map(historicalVersion)
      .sort((a, b) => (a.timestamp ?? "").localeCompare(b.timestamp ?? ""));
    revisionCount += versions.length + 1;
    const newestHistoryTimestamp = history.map((item) => item.timestamp).filter(Boolean).sort().at(-1) ?? null;
    const chain = [...versions, currentVersion(note, newestHistoryTimestamp)];
    for (let index = 0; index < chain.length; index += 1) {
      const event = deriveEvolutionEvent(index ? chain[index - 1] : null, chain[index]);
      if (event) events.push(event);
    }
  }));
  for (const note of notes.slice(EVOLUTION_LIMITS.maxHistoryNotes)) fingerprintParts.push(`${note.id}:${note.revision}`);
  events.sort((a, b) => (b.timestamp ?? "").localeCompare(a.timestamp ?? "") || a.path.localeCompare(b.path));
  if (events.length > EVOLUTION_LIMITS.maxEvents) bounded = true;
  const current = notes.map(currentView);
  const selectedEvents = eventsWithinSourceBudget(events.slice(0, EVOLUTION_LIMITS.maxEvents), current);
  if (selectedEvents.length < events.length) bounded = true;
  const sources = assignCitations(selectedEvents, current);
  const fingerprint = createHash("sha256").update(`${EVOLUTION_SCHEMA_VERSION}|${JSON.stringify(scope)}|${fingerprintParts.sort().join("|")}`).digest("hex");
  return {
    schemaVersion: EVOLUTION_SCHEMA_VERSION,
    scope,
    title: scopeTitle(scope, notes),
    events: selectedEvents,
    current,
    sources,
    noteCount: notes.length,
    revisionCount,
    bounded,
    sourceFingerprint: fingerprint,
    generatedAt: new Date().toISOString(),
    provider: { available: provider.available, name: provider.name, model: provider.model },
    degraded: provider.available ? undefined : "AI synthesis is not configured. The deterministic timeline, comparisons, and current view remain available.",
  };
}

function validateSynthesis(text: string, sources: EvolutionSource[]) {
  const valid = new Set(sources.map((source) => source.citationId));
  const citations = [...new Set([...text.matchAll(/\[(S\d+)\]/g)].map((match) => match[1]).filter((id) => valid.has(id)))];
  return { text: text.replace(/\[(S\d+)\]/g, (match, id: string) => valid.has(id) ? match : ""), citations };
}

export async function synthesizeEvolution(libraryId: string, snapshot: EvolutionSnapshot, provider: GenerationProvider, signal: AbortSignal, force = false): Promise<EvolutionSynthesis> {
  if (!provider.available) throw new Error("AI synthesis isn't configured");
  const cacheKey = evolutionCacheKey(libraryId, snapshot.scope, snapshot.sourceFingerprint, provider);
  if (!force) {
    const cached = generatedCache.get(cacheKey);
    if (cached) return { ...cached, cached: true };
  }
  const input = buildEvolutionPrompt(snapshot);
  const response = await provider.generate({ ...input, maxOutputTokens: 1_400 }, signal);
  const checked = validateSynthesis(response.text, snapshot.sources);
  const result: CacheValue = { markdown: checked.text, citations: checked.citations, sources: snapshot.sources, generatedAt: new Date().toISOString(), cached: false, cacheKey, libraryId, noteIds: snapshot.current.map((item) => item.noteId), paths: snapshot.current.map((item) => item.path) };
  generatedCache.set(cacheKey, result);
  while (generatedCache.size > MAX_CACHE_ENTRIES) generatedCache.delete(generatedCache.keys().next().value!);
  return result;
}


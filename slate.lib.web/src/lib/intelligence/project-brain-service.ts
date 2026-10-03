import type { Entry, FolderPage, HistoryEntry, Note, RelatedNote, SearchPage } from "@/lib/api/contracts";
import type { GenerationProvider } from "./ask-types";
import {
  PROJECT_BRAIN_SCHEMA_VERSION,
  PROJECT_LIMITS,
  buildProjectPrompt,
  deriveProjectSections,
  projectCacheKey,
  projectFingerprint,
  selectProjectSources,
  validateProjectSynthesis,
  type ProjectBrainSnapshot,
  type ProjectSynthesis,
} from "./project-brain";

export interface ProjectCanonical {
  list(path: string, page: number, signal: AbortSignal): Promise<FolderPage>;
  search(query: string, signal: AbortSignal): Promise<SearchPage>;
  note(id: string, signal: AbortSignal): Promise<Note>;
  history(id: string, signal: AbortSignal): Promise<HistoryEntry[]>;
  related(id: string, signal: AbortSignal): Promise<RelatedNote[]>;
  semantic?(query: string, path: string): Promise<string[]>;
}

const generatedCache = new Map<string, ProjectSynthesis>();
const MAX_CACHE_ENTRIES = 60;

export function clearProjectBrainCache(libraryId?: string) {
  if (!libraryId) generatedCache.clear();
  else for (const key of generatedCache.keys()) if (key.startsWith(`${libraryId}:`)) generatedCache.delete(key);
}

export function normalizeProjectPath(path: string) {
  const value = path.replace(/\\/g, "/").replace(/^\/+|\/+$/g, "");
  if (!value || value.length > 1024 || value.split("/").some((part) => !part || part === "." || part === "..")) throw new Error("Invalid project path");
  return value;
}

function candidateScore(entry: Entry) {
  const value = entry.path.toLowerCase();
  let score = 0;
  if (/(^|\/)(readme|overview|project|status|current)(\.md)?$/.test(value)) score += 20;
  if (/(^|\/)(architecture|design|decisions?|adr|questions?|ideas?|experiments?|failures?|risks?|blockers?)(\/|\.md|$)/.test(value)) score += 15;
  if (entry.path.split("/").length <= 3) score += 4;
  return score;
}

async function listProject(canonical: ProjectCanonical, root: string, signal: AbortSignal) {
  const folders = [root];
  const entries: Entry[] = [];
  let folderCount = 0;
  let bounded = false;
  while (folders.length && folderCount < PROJECT_LIMITS.maxFolders && entries.length < PROJECT_LIMITS.maxEntries) {
    const folder = folders.shift()!;
    folderCount += 1;
    let page = 0;
    do {
      const listing = await canonical.list(folder, page, signal);
      for (const entry of listing.entries) {
        if (entries.length >= PROJECT_LIMITS.maxEntries) { bounded = true; break; }
        entries.push(entry);
        if (entry.isDirectory && folders.length + folderCount < PROJECT_LIMITS.maxFolders) folders.push(entry.path);
        else if (entry.isDirectory) bounded = true;
      }
      if (bounded || listing.nextPage === null) break;
      page = listing.nextPage;
    } while (true);
  }
  if (folders.length) bounded = true;
  return { entries, folderCount, bounded };
}

async function searchCandidateIds(canonical: ProjectCanonical, root: string, signal: AbortSignal) {
  const filters = [
    `path:${JSON.stringify(root)} type:question status:open`,
    `path:${JSON.stringify(root)} type:decision`,
    `path:${JSON.stringify(root)} type:architecture`,
    `path:${JSON.stringify(root)} type:idea`,
    `path:${JSON.stringify(root)} type:experiment`,
    `path:${JSON.stringify(root)} type:failure`,
    `path:${JSON.stringify(root)} status:blocked`,
  ];
  const pages = await Promise.all(filters.map((query) => canonical.search(query, signal).catch(() => ({ results: [] } as unknown as SearchPage))));
  return pages.flatMap((page) => page.results).map((hit) => hit.id);
}

function projectGraph(notes: Note[]) {
  const byTitle = new Map(notes.map((note) => [note.title.toLowerCase(), note]));
  const edges: Array<{ source: string; target: string }> = [];
  const degree = new Map<string, number>();
  for (const note of notes) {
    for (const match of note.markdown.matchAll(/\[\[([^\]#|]+)(?:#[^\]|]+)?(?:\|[^\]]+)?\]\]/g)) {
      const target = byTitle.get(match[1].trim().toLowerCase());
      if (!target || target.id === note.id || edges.some((edge) => edge.source === note.id && edge.target === target.id)) continue;
      edges.push({ source: note.id, target: target.id });
      degree.set(note.id, (degree.get(note.id) ?? 0) + 1);
      degree.set(target.id, (degree.get(target.id) ?? 0) + 1);
    }
  }
  const nodes = [...notes].sort((a, b) => (degree.get(b.id) ?? 0) - (degree.get(a.id) ?? 0) || a.path.localeCompare(b.path)).slice(0, PROJECT_LIMITS.maxGraphNodes);
  const ids = new Set(nodes.map((note) => note.id));
  return { nodes: nodes.map((note) => ({ id: note.id, title: note.title, path: note.path, kind: /architecture|design/i.test(note.path) ? "architecture" : /decision|adr/i.test(note.path) ? "decision" : /question/i.test(note.path) ? "question" : "note" })), edges: edges.filter((edge) => ids.has(edge.source) && ids.has(edge.target)), limited: notes.length > nodes.length };
}

export async function buildProjectBrain(canonical: ProjectCanonical, libraryId: string, path: string, provider: Pick<GenerationProvider, "available" | "name" | "model">, signal: AbortSignal): Promise<ProjectBrainSnapshot> {
  const root = normalizeProjectPath(path);
  const listing = await listProject(canonical, root, signal);
  const noteEntries = listing.entries.filter((entry) => !entry.isDirectory && entry.id);
  const [searched, semantic] = await Promise.all([
    searchCandidateIds(canonical, root, signal),
    canonical.semantic?.("project overview architecture current state decisions open questions failures blockers recent work", root).catch(() => []) ?? Promise.resolve([]),
  ]);
  const ranked = noteEntries.slice().sort((a, b) => candidateScore(b) - candidateScore(a) || a.path.localeCompare(b.path));
  const candidateIds = [...new Set([...searched, ...semantic, ...ranked.map((entry) => entry.id!)])].slice(0, PROJECT_LIMITS.maxCandidateNotes);
  const notes = (await Promise.all(candidateIds.map((id) => canonical.note(id, signal).catch(() => null)))).filter((note): note is Note => !!note && (note.path === root || note.path.startsWith(`${root}/`)));
  const histories = new Map<string, HistoryEntry[]>();
  await Promise.all(notes.slice(0, PROJECT_LIMITS.maxHistoryNotes).map(async (note) => histories.set(note.id, (await canonical.history(note.id, signal).catch(() => [])).slice(0, 12))));
  const { sections, timeline } = deriveProjectSections(notes, histories);
  const sources = selectProjectSources(sections, "resume");
  const outside = new Map<string, RelatedNote>();
  await Promise.all(sections.important.slice(0, 3).map(async (item) => {
    for (const related of await canonical.related(item.noteId, signal).catch(() => [])) {
      if ((related.path === root || related.path.startsWith(`${root}/`)) || outside.has(related.id)) continue;
      outside.set(related.id, related);
    }
  }));
  const lastMeaningfulChange = timeline[0]?.timestamp ?? sections.current.map((item) => item.timestamp).filter((item): item is string => !!item).sort().at(-1) ?? null;
  const status = sections.risks.some((item) => item.status === "blocked") ? "Blocked" : sections.current.length ? "Active" : sections.questions.length ? "Open questions" : "No explicit status";
  return {
    schemaVersion: PROJECT_BRAIN_SCHEMA_VERSION,
    path: root,
    name: root.split("/").at(-1) ?? root,
    noteCount: noteEntries.length,
    folderCount: listing.folderCount,
    bounded: listing.bounded,
    status,
    lastMeaningfulChange,
    openQuestionCount: sections.questions.length,
    sections,
    timeline,
    graph: projectGraph(notes),
    relatedOutside: [...outside.values()].sort((a, b) => b.score - a.score).slice(0, 12),
    sources,
    sourceFingerprint: projectFingerprint(root, notes, histories),
    generatedAt: new Date().toISOString(),
    provider,
  };
}

export async function synthesizeProject(libraryId: string, snapshot: ProjectBrainSnapshot, kind: ProjectSynthesis["kind"], provider: GenerationProvider, signal: AbortSignal, force = false): Promise<ProjectSynthesis> {
  if (!provider.available) throw new Error("AI synthesis isn't configured.");
  const sources = selectProjectSources(snapshot.sections, kind);
  const key = projectCacheKey(libraryId, snapshot.path, snapshot.sourceFingerprint, provider, kind);
  const cached = generatedCache.get(key);
  if (cached && !force) return { ...cached, cached: true };
  const input = buildProjectPrompt(snapshot.path, kind, sources);
  const output = await provider.generate({ ...input, maxOutputTokens: kind === "resume" ? 1_600 : 900 }, signal);
  const valid = validateProjectSynthesis(output.text, sources);
  const value: ProjectSynthesis = { kind, markdown: valid.text, sources, citations: valid.citations, generatedAt: new Date().toISOString(), cached: false, cacheKey: key };
  generatedCache.set(key, value);
  while (generatedCache.size > MAX_CACHE_ENTRIES) generatedCache.delete(generatedCache.keys().next().value!);
  return value;
}


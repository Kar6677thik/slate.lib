import { after } from "next/server";
import { z } from "zod";
import type { Entry, FolderPage, HistoricalNote, HistoryEntry, Note, RelatedNote, SearchPage, Status } from "@/lib/api/contracts";
import { claimIndexEvents, enableReconciliation, enqueueIndexEvent, failIndexEvent, finishIndexEvent, hybridSearch, indexNote, indexedNoteIds, intelligenceStatus, pruneIndexedNotes, reconciliationEnabled, removeIndexedNote, semanticAskCandidates } from "@/lib/intelligence/service";
import type { IndexEvent, SearchMode } from "@/lib/intelligence/types";
import { fetchSlate, resolveSlateUpstream } from "@/lib/server/slate-upstream";
import { ASK_LIMITS, classifyQuestion, fitGroundedPrompt, hasSufficientEvidence, noteSourceCandidates, selectAskSources, validateCitations, type SourceCandidate } from "@/lib/intelligence/ask-context";
import { acquireGeneration, generationProvider, generationStatus, recordAsk } from "@/lib/intelligence/ask-service";
import type { AskRequest, AskStreamEvent } from "@/lib/intelligence/ask-types";
import { buildProjectBrain, clearProjectBrainCache, synthesizeProject, type ProjectCanonical } from "@/lib/intelligence/project-brain-service";
import { buildEvolution, clearEvolutionCache, synthesizeEvolution, type EvolutionCanonical } from "@/lib/intelligence/evolution-service";

export const dynamic = "force-dynamic";
const noStore = { "cache-control": "no-store", "x-content-type-options": "nosniff" };
const libraryCache = new Map<string, { id: string; expires: number }>();
const libraryTotals = new Map<string, number>();
const eventSchema = z.discriminatedUnion("kind", [
  z.object({ kind: z.literal("upsert"), noteId: z.string().uuid() }),
  z.object({ kind: z.literal("delete"), noteId: z.string().uuid().optional(), path: z.string().min(1).max(1024).optional() }).refine((value) => value.noteId || value.path),
  z.object({ kind: z.literal("rename"), path: z.string().min(1).max(1024), destinationPath: z.string().min(1).max(1024).optional() }),
  z.object({ kind: z.literal("reconcile"), reason: z.enum(["sync", "refresh", "bulk", "restore", "rebuild"]) }),
]);
const askScopeSchema = z.discriminatedUnion("kind", [
  z.object({ kind: z.literal("library"), selectedText: z.string().max(ASK_LIMITS.maxSelectedTextCharacters).optional() }),
  z.object({ kind: z.literal("note"), noteId: z.string().uuid(), selectedText: z.string().max(ASK_LIMITS.maxSelectedTextCharacters).optional() }),
  z.object({ kind: z.literal("folder"), path: z.string().min(1).max(1024), selectedText: z.string().max(ASK_LIMITS.maxSelectedTextCharacters).optional() }),
  z.object({ kind: z.literal("project"), path: z.string().min(1).max(1024), selectedText: z.string().max(ASK_LIMITS.maxSelectedTextCharacters).optional() }),
  z.object({ kind: z.literal("selected"), noteIds: z.array(z.string().uuid()).min(1).max(20), selectedText: z.string().max(ASK_LIMITS.maxSelectedTextCharacters).optional() }),
]);
const askSchema = z.object({
  question: z.string().trim().min(1).max(ASK_LIMITS.maxQuestionCharacters),
  scope: askScopeSchema,
  policy: z.enum(["strict", "general"]).default("strict"),
  conversation: z.array(z.object({ role: z.enum(["user", "assistant"]), text: z.string().max(4_000) })).max(ASK_LIMITS.maxHistoryTurns).default([]),
});
const projectBrainSchema = z.object({
  path: z.string().trim().min(1).max(1024),
  action: z.enum(["snapshot", "generate"]).default("snapshot"),
  kind: z.enum(["overview", "resume", "recent"]).default("overview"),
  refresh: z.boolean().default(false),
});
const evolutionScopeSchema = z.discriminatedUnion("kind", [
  z.object({ kind: z.literal("topic"), topic: z.string().trim().min(1).max(300), path: z.string().trim().min(1).max(1024).optional() }),
  z.object({ kind: z.enum(["project", "folder"]), path: z.string().trim().min(1).max(1024), topic: z.string().trim().max(300).optional() }),
  z.object({ kind: z.literal("note"), noteId: z.string().uuid(), topic: z.string().trim().max(300).optional() }),
  z.object({ kind: z.literal("selected"), noteIds: z.array(z.string().uuid()).min(1).max(20), topic: z.string().trim().max(300).optional() }),
]);
const evolutionSchema = z.object({
  action: z.enum(["snapshot", "generate"]).default("snapshot"),
  scope: evolutionScopeSchema,
  refresh: z.boolean().default(false),
});

function error(status: number, message: string) { return Response.json({ error: message }, { status, headers: noStore }); }

async function readJsonBody(request: Request) {
  if (!request.body) return {};
  const reader = request.body.getReader();
  const decoder = new TextDecoder();
  let text = "";
  let received = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      received += value.byteLength;
      if (received > 16 * 1024) {
        await reader.cancel();
        throw new Response("Request is too large", { status: 413 });
      }
      text += decoder.decode(value, { stream: true });
    }
    text += decoder.decode();
    return text ? JSON.parse(text) as unknown : {};
  } catch (caught) {
    if (caught instanceof Response) throw caught;
    throw new Response("Invalid JSON", { status: 400 });
  } finally {
    reader.releaseLock();
  }
}

async function canonicalLibraryId(connection: ReturnType<typeof resolveSlateUpstream>, signal: AbortSignal) {
  const cached = libraryCache.get(connection.server);
  if (cached && cached.expires > Date.now()) return cached.id;
  const response = await fetchSlate(connection, "v1/status", signal);
  if (!response.ok) throw new Response("Canonical library is unavailable", { status: response.status });
  const status = await response.json() as Status;
  const id = status.libraryId;
  if (!/^[a-f0-9-]{36}$/i.test(id)) throw new Response("Canonical library identity is invalid", { status: 502 });
  if (Number.isFinite(status.noteCount)) libraryTotals.set(id, Math.max(0, status.noteCount));
  libraryCache.set(connection.server, { id, expires: Date.now() + 60_000 });
  return id;
}

async function reconcile(connection: ReturnType<typeof resolveSlateUpstream>, libraryId: string, event: IndexEvent) {
  try {
    const folders = [""];
    const seen: string[] = [];
    while (folders.length) {
      const folder = folders.shift()!;
      let page = 0;
      do {
        const response = await fetchSlate(connection, `v1/library?path=${encodeURIComponent(folder)}&page=${page}`);
        if (!response.ok) throw new Error("Library listing failed");
        const listing = await response.json() as FolderPage;
        for (const entry of listing.entries as Entry[]) {
          if (entry.isDirectory) folders.push(entry.path);
          else if (entry.id) {
            seen.push(entry.id);
            const noteResponse = await fetchSlate(connection, `v1/notes/${encodeURIComponent(entry.id)}`);
            if (noteResponse.ok) await indexNote(libraryId, await noteResponse.json() as Note);
          }
        }
        if (listing.nextPage === null) break;
        page = listing.nextPage;
      } while (true);
    }
    await pruneIndexedNotes(libraryId, seen);
    await finishIndexEvent(libraryId, event);
  } catch { await failIndexEvent(libraryId, event).catch(() => undefined); }
}

async function resumePending(connection: ReturnType<typeof resolveSlateUpstream>, libraryId: string) {
  let events: IndexEvent[] = [];
  try { events = await claimIndexEvents(libraryId); } catch { return; }
  for (const event of events) {
    try {
      if (event.kind === "upsert") {
        const response = await fetchSlate(connection, `v1/notes/${event.noteId}`);
        if (response.status === 404) await removeIndexedNote(libraryId, event.noteId);
        else if (!response.ok) throw new Error("Note fetch failed");
        else await indexNote(libraryId, await response.json() as Note);
        await finishIndexEvent(libraryId, event);
      } else if (event.kind === "delete") {
        await removeIndexedNote(libraryId, event.noteId, event.path);
        await finishIndexEvent(libraryId, event);
      } else {
        const allowed = event.kind === "reconcile" && (event.reason === "rebuild" || await reconciliationEnabled(libraryId));
        if (!allowed) {
          for (const noteId of await indexedNoteIds(libraryId)) {
            const response = await fetchSlate(connection, `v1/notes/${noteId}`);
            if (response.status === 404) await removeIndexedNote(libraryId, noteId);
            else if (response.ok) await indexNote(libraryId, await response.json() as Note);
          }
          await finishIndexEvent(libraryId, event);
        }
        else {
          if (event.kind === "reconcile" && event.reason === "rebuild") await enableReconciliation(libraryId);
          await reconcile(connection, libraryId, event);
        }
      }
    } catch { await failIndexEvent(libraryId, event).catch(() => undefined); }
  }
}

function safeScopePath(path: string) {
  const normalized = path.replace(/\\/g, "/").replace(/^\/+|\/+$/g, "");
  if (!normalized || normalized.split("/").some((part) => !part || part === "." || part === "..")) throw new Response("Invalid scope path", { status: 422 });
  return normalized;
}

function comparisonQueries(question: string) {
  const queries = [question];
  const match = question.match(/\bcompare\s+(.{2,80}?)\s+(?:and|with|versus|vs\.?)\s+(.{2,80}?)(?:[?.]|$)/i);
  if (match) queries.push(match[1].trim(), match[2].trim());
  return [...new Set(queries)].slice(0, 3);
}

async function canonicalNote(connection: ReturnType<typeof resolveSlateUpstream>, id: string, signal: AbortSignal) {
  const response = await fetchSlate(connection, `v1/notes/${encodeURIComponent(id)}`, signal);
  if (!response.ok) throw new Response("A scoped note is unavailable", { status: response.status });
  return response.json() as Promise<Note>;
}

async function askCandidates(connection: ReturnType<typeof resolveSlateUpstream>, libraryId: string, request: AskRequest, signal: AbortSignal) {
  const candidates: SourceCandidate[] = [];
  const notes = new Map<string, Note>();
  const scope = request.scope;
  const folder = scope.kind === "folder" || scope.kind === "project" ? safeScopePath(scope.path!) : undefined;
  if (scope.kind === "note") notes.set(scope.noteId!, await canonicalNote(connection, scope.noteId!, signal));
  if (scope.kind === "selected") {
    const selected = await Promise.all(scope.noteIds!.slice(0, 20).map((id) => canonicalNote(connection, id, signal)));
    for (const note of selected) notes.set(note.id, note);
  }
  if (scope.selectedText?.trim() && scope.kind === "note") {
    const note = notes.get(scope.noteId!);
    if (note) candidates.push({ noteId: note.id, title: note.title, path: note.path, heading: "Selected text", ordinal: -1, revision: note.revision, excerpt: scope.selectedText.trim(), score: 2 });
  }
  if (scope.kind === "library" || folder) {
    const classified = classifyQuestion(request.question);
    for (const query of comparisonQueries(request.question)) {
      const filters = `${folder ? ` path:${JSON.stringify(folder)}` : ""}${classified.unanswered ? " type:question status:open" : ""}`;
      const response = await fetchSlate(connection, `v1/search?q=${encodeURIComponent(query + filters)}&page=0&pageSize=20`, signal);
      if (response.ok) {
        const page = await response.json() as SearchPage;
        for (const hit of page.results.slice(0, 10)) {
          if (!notes.has(hit.id) && notes.size < 12) {
            try { notes.set(hit.id, await canonicalNote(connection, hit.id, signal)); } catch { /* One stale lexical hit must not fail the answer. */ }
          }
        }
      }
    }
    candidates.push(...await semanticAskCandidates(libraryId, request.question, folder));
  }
  for (const note of notes.values()) candidates.push(...noteSourceCandidates(note, request.question, scope.kind === "note" || scope.kind === "selected" ? 0.7 : 0.4));

  if (classifyQuestion(request.question).temporal && scope.kind === "note") {
    const historyResponse = await fetchSlate(connection, `v1/notes/${scope.noteId}/history`, signal);
    if (historyResponse.ok) {
      const history = (await historyResponse.json() as HistoryEntry[]).slice(0, 2);
      for (const entry of history) {
        const response = await fetchSlate(connection, `v1/notes/${scope.noteId}/history/${encodeURIComponent(entry.commit)}`, signal);
        if (!response.ok) continue;
        const old = await response.json() as HistoricalNote;
        const note: Note = { id: old.id, title: `${old.title} (${old.timestamp})`, path: old.path, markdown: old.markdown, revision: old.commit };
        candidates.push(...noteSourceCandidates(note, request.question, 0.55));
      }
    }
  }
  return selectAskSources(request.question, candidates);
}

function streamEvent(controller: ReadableStreamDefaultController<Uint8Array>, event: AskStreamEvent) {
  controller.enqueue(new TextEncoder().encode(`data: ${JSON.stringify(event)}\n\n`));
}

async function askResponse(connection: ReturnType<typeof resolveSlateUpstream>, libraryId: string, request: AskRequest, clientSignal: AbortSignal) {
  const provider = generationProvider();
  if (!provider.available) return error(503, "Ask Slate isn't configured on this server.");
  const release = acquireGeneration(libraryId);
  if (!release) return error(429, "Ask Slate is already handling the maximum number of requests. Try again shortly.");
  const configuredOutputTokens = Number(process.env.SLATE_GENERATION_MAX_OUTPUT_TOKENS ?? 1_200);
  const maxOutputTokens = Number.isInteger(configuredOutputTokens) && configuredOutputTokens >= 128 && configuredOutputTokens <= 4_096 ? configuredOutputTokens : 1_200;
  const signal = AbortSignal.any([clientSignal, AbortSignal.timeout(65_000)]);
  const stream = new ReadableStream<Uint8Array>({
    async start(controller) {
      let partial = false;
      try {
        streamEvent(controller, { type: "retrieval", message: "Searching your Slate sources…" });
        const retrievedSources = await askCandidates(connection, libraryId, request, signal);
        const fitted = fitGroundedPrompt(request.question, request.policy, request.conversation, retrievedSources, provider.maxInputTokens, maxOutputTokens);
        const sources = fitted.sources;
        streamEvent(controller, { type: "sources", sources });
        if (request.policy === "strict" && !hasSufficientEvidence(request.question, sources)) {
          const text = "I couldn't find enough in your Slate library to answer that.";
          streamEvent(controller, { type: "delta", text });
          streamEvent(controller, { type: "done", citations: [] });
          recordAsk(libraryId, sources.length);
          return;
        }
        const input = { system: fitted.system, prompt: fitted.prompt, maxOutputTokens };
        let answer = "";
        let usage;
        for await (const part of provider.streamGenerate(input, signal)) {
          if (part.text) {
            answer += part.text;
            if (answer.length > 48_000) throw new Error("Generated answer exceeded the response limit");
            streamEvent(controller, { type: "delta", text: part.text });
            partial = true;
          }
          if (part.usage) usage = part.usage;
        }
        const validated = validateCitations(answer, sources);
        if (validated.text !== answer) streamEvent(controller, { type: "replace", text: validated.text });
        streamEvent(controller, { type: "done", citations: validated.citations, usage });
        recordAsk(libraryId, sources.length, usage);
      } catch {
        if (!signal.aborted) streamEvent(controller, { type: "error", message: "Ask Slate could not finish this answer. You can retry without losing the sources already found.", partial });
      } finally {
        release();
        controller.close();
      }
    },
    cancel() { release(); },
  });
  return new Response(stream, { headers: { ...noStore, "content-type": "text/event-stream; charset=utf-8", connection: "keep-alive" } });
}

function projectCanonical(connection: ReturnType<typeof resolveSlateUpstream>, libraryId: string): ProjectCanonical {
  async function value<T>(path: string, signal: AbortSignal) {
    const response = await fetchSlate(connection, path, signal);
    if (!response.ok) throw new Error(`Canonical project request failed (${response.status})`);
    return response.json() as Promise<T>;
  }
  return {
    list: (path, page, signal) => value<FolderPage>(`v1/library?path=${encodeURIComponent(path)}&page=${page}`, signal),
    search: (query, signal) => value<SearchPage>(`v1/search?q=${encodeURIComponent(query)}&page=0&pageSize=50`, signal),
    note: (id, signal) => value<Note>(`v1/notes/${encodeURIComponent(id)}`, signal),
    history: (id, signal) => value<HistoryEntry[]>(`v1/notes/${encodeURIComponent(id)}/history`, signal),
    related: (id, signal) => value<RelatedNote[]>(`v1/notes/${encodeURIComponent(id)}/related`, signal),
    semantic: async (query, path) => (await semanticAskCandidates(libraryId, query, path)).map((candidate) => candidate.noteId),
  };
}

function evolutionCanonical(connection: ReturnType<typeof resolveSlateUpstream>, libraryId: string): EvolutionCanonical {
  async function value<T>(path: string, signal: AbortSignal) {
    const response = await fetchSlate(connection, path, signal);
    if (!response.ok) throw new Error(`Canonical evolution request failed (${response.status})`);
    return response.json() as Promise<T>;
  }
  return {
    list: (path, page, signal) => value<FolderPage>(`v1/library?path=${encodeURIComponent(path)}&page=${page}`, signal),
    search: (query, signal) => value<SearchPage>(`v1/search?q=${encodeURIComponent(query)}&page=0&pageSize=50`, signal),
    note: (id, signal) => value<Note>(`v1/notes/${encodeURIComponent(id)}`, signal),
    history: (id, signal) => value<HistoryEntry[]>(`v1/notes/${encodeURIComponent(id)}/history`, signal),
    historical: (id, commit, signal) => value<HistoricalNote>(`v1/notes/${encodeURIComponent(id)}/history/${encodeURIComponent(commit)}`, signal),
    semantic: async (query, path) => (await semanticAskCandidates(libraryId, query, path)).map((candidate) => candidate.noteId),
  };
}

async function handler(request: Request, context: { params: Promise<{ path: string[] }> }) {
  const path = (await context.params).path.join("/");
  if (Number(request.headers.get("content-length") ?? 0) > 16 * 1024) return error(413, "Request is too large");
  let connection;
  try { connection = resolveSlateUpstream(request); }
  catch (caught) { return caught instanceof Response ? error(caught.status, "Request is not authorized") : error(400, "Invalid Slate connection"); }
  let json: unknown = {};
  if (request.method === "POST") {
    try { json = await readJsonBody(request); }
    catch (caught) { return caught instanceof Response ? error(caught.status, await caught.text()) : error(400, "Invalid JSON"); }
  }
  let libraryId: string;
  try { libraryId = await canonicalLibraryId(connection, request.signal); }
  catch (caught) { return caught instanceof Response ? error(caught.status, await caught.text()) : error(502, "Canonical library is unavailable"); }
  after(() => resumePending(connection, libraryId));
  if (path === "status" && request.method === "GET") return Response.json({ ...await intelligenceStatus(libraryId), totalNotes: libraryTotals.get(libraryId) ?? 0, ...generationStatus(libraryId) }, { headers: noStore });
  if (path === "ask" && request.method === "POST") {
    const parsed = askSchema.safeParse(json);
    if (!parsed.success) return error(422, "Invalid Ask Slate request");
    return askResponse(connection, libraryId, parsed.data, request.signal);
  }
  if (path === "project-brain" && request.method === "POST") {
    const parsed = projectBrainSchema.safeParse(json);
    if (!parsed.success) return error(422, "Invalid Project Brain request");
    try {
      const provider = generationProvider();
      const snapshot = await buildProjectBrain(projectCanonical(connection, libraryId), libraryId, parsed.data.path, { available: provider.available, name: provider.name, model: provider.model }, request.signal);
      if (parsed.data.action === "snapshot") return Response.json(snapshot, { headers: noStore });
      if (!provider.available) return error(503, "AI synthesis isn't configured.");
      const release = acquireGeneration(libraryId);
      if (!release) return error(429, "Project Brain is already handling the maximum number of generation requests.");
      try {
        const result = await synthesizeProject(libraryId, snapshot, parsed.data.kind, provider, AbortSignal.any([request.signal, AbortSignal.timeout(65_000)]), parsed.data.refresh);
        recordAsk(libraryId, result.sources.length);
        return Response.json(result, { headers: noStore });
      } finally { release(); }
    } catch (caught) {
      return error(502, caught instanceof Error ? caught.message : "Project Brain is unavailable");
    }
  }
  if (path === "evolution" && request.method === "POST") {
    const parsed = evolutionSchema.safeParse(json);
    if (!parsed.success) return error(422, "Invalid Evolution of Thought request");
    try {
      const provider = generationProvider();
      const snapshot = await buildEvolution(evolutionCanonical(connection, libraryId), libraryId, parsed.data.scope, { available: provider.available, name: provider.name, model: provider.model }, request.signal);
      if (parsed.data.action === "snapshot") return Response.json(snapshot, { headers: noStore });
      if (!provider.available) return error(503, "AI synthesis isn't configured.");
      const release = acquireGeneration(libraryId);
      if (!release) return error(429, "Evolution of Thought is already handling the maximum number of generation requests.");
      try {
        const result = await synthesizeEvolution(libraryId, snapshot, provider, AbortSignal.any([request.signal, AbortSignal.timeout(65_000)]), parsed.data.refresh);
        recordAsk(libraryId, result.sources.length);
        return Response.json(result, { headers: noStore });
      } finally { release(); }
    } catch (caught) {
      return error(502, caught instanceof Error ? caught.message : "Evolution of Thought is unavailable");
    }
  }
  if (path === "search" && request.method === "POST") {
    const body = json as { query?: string; mode?: SearchMode; page?: number; pageSize?: number; diagnostics?: boolean };
    const query = String(body.query ?? "").trim().slice(0, 500);
    const mode = (["hybrid", "lexical", "semantic"].includes(String(body.mode)) ? body.mode : "hybrid") as SearchMode;
    const page = Math.min(Math.max(Math.trunc(Number(body.page) || 0), 0), 100);
    const pageSize = Math.min(Math.max(Math.trunc(Number(body.pageSize) || 20), 1), 50);
    if (!query) return Response.json({ query, mode, effectiveMode: mode, page, pageSize, total: 0, results: [] }, { headers: noStore });
    const candidateSize = Math.min(Math.max((page + 1) * pageSize * 3, 30), 200);
    const response = await fetchSlate(connection, `v1/search?q=${encodeURIComponent(query)}&page=0&pageSize=${candidateSize}`, request.signal);
    if (!response.ok) return error(response.status, "Keyword search failed");
    const lexical = await response.json() as SearchPage;
    return Response.json(await hybridSearch(libraryId, query, mode, lexical.results, Boolean(body.diagnostics), page, pageSize, lexical.total), { headers: noStore });
  }
  if (path === "related" && request.method === "POST") {
    const body = json as { noteId?: string };
    if (!body.noteId || !/^[a-f0-9-]{36}$/i.test(body.noteId)) return error(422, "Invalid note identity");
    const [noteResponse, relatedResponse] = await Promise.all([
      fetchSlate(connection, `v1/notes/${body.noteId}`, request.signal),
      fetchSlate(connection, `v1/notes/${body.noteId}/related`, request.signal),
    ]);
    if (!noteResponse.ok || !relatedResponse.ok) return error(!noteResponse.ok ? noteResponse.status : relatedResponse.status, "Related notes are unavailable");
    const note = await noteResponse.json() as Note;
    const deterministic = await relatedResponse.json() as RelatedNote[];
    const lexical = deterministic.map((item) => ({ ...item, snippet: item.reasons.join(" · "), revision: "derived" }));
    const result = await hybridSearch(libraryId, note.title, "hybrid", lexical);
    const reasons = new Map(deterministic.map((item) => [item.id, item.reasons]));
    return Response.json(result.results.filter((item) => item.id !== note.id).slice(0, 12).map((item) => ({ id: item.id, title: item.title, path: item.path, score: item.score ?? 0, reasons: reasons.get(item.id) ?? [`Similar meaning${item.heading ? ` under ${item.heading}` : ""}`] })), { headers: noStore });
  }
  if (path === "events" && request.method === "POST") {
    const parsed = eventSchema.safeParse(json);
    if (!parsed.success) return error(422, "Invalid indexing event");
    const body = parsed.data as IndexEvent;
    clearProjectBrainCache(libraryId);
    clearEvolutionCache(libraryId, "noteId" in body ? body.noteId : undefined, "path" in body ? body.path : undefined);
    try {
      await enqueueIndexEvent(libraryId, body);
      if (body.kind === "upsert") {
        const response = await fetchSlate(connection, `v1/notes/${encodeURIComponent(body.noteId)}`, request.signal);
        if (response.ok) await indexNote(libraryId, await response.json() as Note);
      } else if (body.kind === "delete") { await removeIndexedNote(libraryId, body.noteId, body.path); await finishIndexEvent(libraryId, body); }
    } catch { await failIndexEvent(libraryId, body).catch(() => undefined); /* Derived indexing never changes the result of a canonical write. */ }
    return new Response(null, { status: 202, headers: noStore });
  }
  if (path === "rebuild" && request.method === "POST") {
    const body = json as { confirm?: boolean };
    if (!body.confirm) return error(422, "Rebuild confirmation is required");
    const event = { kind: "reconcile", reason: "rebuild" } as const;
    try { await enqueueIndexEvent(libraryId, event); }
    catch { return error(503, "Derived index is unavailable"); }
    return Response.json({ state: "pending" }, { status: 202, headers: noStore });
  }
  return error(404, "Not found");
}

export { handler as GET, handler as POST };

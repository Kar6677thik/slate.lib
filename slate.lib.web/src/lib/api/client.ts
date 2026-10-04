import { z } from "zod";
import {
  noteSchema,
  type Note,
  type Connection,
  type Status,
  type FolderPage,
  type SearchPage,
  type Links,
  type HistoryEntry,
  type HistoricalNote,
  type Asset,
  type Mutation,
  type LinkIssuePage,
  type NoteTextPreview,
  type BulkPreview,
  type BulkResult,
  type RecoveryPage,
  type KnowledgeGraph,
  type RelatedNote,
  type RediscoveryPage,
  type AssetPage,
  type AssetReferencePage,
  type AssetDerivedText,
  type HybridSearchPage,
  type IntelligenceStatus,
  type AskPayload,
  type AskStreamHandler,
  type SearchMode,
  type ProjectBrainSnapshot,
  type ProjectSynthesis,
  type EvolutionScope,
  type EvolutionSnapshot,
  type EvolutionSynthesis,
} from "./contracts";
export function normalizeServer(value: string) {
  const url = new URL(value.trim());
  if (
    url.username ||
    url.password ||
    url.search ||
    url.hash ||
    !["http:", "https:"].includes(url.protocol) ||
    (url.protocol !== "https:" &&
      !["localhost", "127.0.0.1", "[::1]"].includes(url.hostname))
  )
    throw new Error(
      "Use HTTPS, or HTTP on localhost for development. Do not include credentials or query parameters.",
    );
  return url.href.replace(/\/+$/, "");
}
export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}
export class SlateApi {
  constructor(readonly connection: Connection) {
    normalizeServer(connection.server);
  }
  async request<T>(
    path: string,
    init: RequestInit = {},
    signal?: AbortSignal,
  ): Promise<T> {
    let response: Response;
    try {
      response = await fetch(`/api/slate/${path}`, {
        ...init,
        cache: "no-store",
        redirect: "error",
        signal: signal
          ? AbortSignal.any([signal, AbortSignal.timeout(30000)])
          : AbortSignal.timeout(30000),
        headers: {
          ...(init.body && !(init.body instanceof Blob)
            ? { "Content-Type": "application/json" }
            : {}),
          ...init.headers,
          Authorization: `Bearer ${this.connection.token}`,
          "X-Slate-Server": this.connection.server,
        },
      });
    } catch (error) {
      if (signal?.aborted) throw error;
      throw new ApiError(
        0,
        "Cannot reach the server. Your work is kept. Check your connection and try again.",
      );
    }
    if (!response.ok)
      throw new ApiError(
        response.status,
        (
          {
            401: "Your device token has expired or been revoked. Reconnect in Settings.",
            403: "This server is not approved by the web host. Check the allowed server configuration.",
            404: "This item no longer exists. Refresh the library.",
            409: "This name or identity already exists. Nothing was overwritten.",
            412: "This note changed elsewhere. Your draft is preserved.",
            413: "The file exceeds the server upload limit.",
            422: "The content could not be accepted. Check its metadata.",
            429: "Too many requests. Wait a moment and try again.",
          } as Record<number, string>
        )[response.status] ??
          "The server could not complete this action. Your work is kept; try again.",
      );
    if (response.status === 204) return undefined as T;
    if (
      init.headers &&
      new Headers(init.headers).get("Accept") === "application/octet-stream"
    )
      return (await response.blob()) as T;
    return (await response.json()) as T;
  }
  post<T>(path: string, body: unknown = {}) {
    return this.request<T>(path, {
      method: "POST",
      body: JSON.stringify(body),
    });
  }
  private async intelligence<T>(path: string, init: RequestInit = {}, signal?: AbortSignal, timeoutMs = 30000) {
    const response = await fetch(`/api/intelligence/${path}`, {
      ...init,
      cache: "no-store",
      signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(timeoutMs)]) : AbortSignal.timeout(timeoutMs),
      headers: {
        ...(init.body ? { "Content-Type": "application/json" } : {}),
        Authorization: `Bearer ${this.connection.token}`,
        "X-Slate-Server": this.connection.server,
        ...init.headers,
      },
    });
    if (!response.ok) throw new ApiError(response.status, "Intelligence service is unavailable. Keyword search still works.");
    return response.status === 204 ? undefined as T : await response.json() as T;
  }
  private notifyIntelligence(event: unknown) {
    void this.intelligence<void>("events", { method: "POST", body: JSON.stringify(event) }).catch(() => undefined);
  }
  intelligenceStatus(signal?: AbortSignal) { return this.intelligence<IntelligenceStatus>("status", {}, signal); }
  intelligenceRebuild() { return this.intelligence<{ state: string }>("rebuild", { method: "POST", body: JSON.stringify({ confirm: true }) }); }
  hybridSearch(query: string, mode: SearchMode, page = 0, signal?: AbortSignal, diagnostics = false) {
    return this.intelligence<HybridSearchPage>("search", { method: "POST", body: JSON.stringify({ query, mode, page, pageSize: 20, diagnostics }) }, signal);
  }
  projectBrain(path: string, signal?: AbortSignal) {
    return this.intelligence<ProjectBrainSnapshot>("project-brain", { method: "POST", body: JSON.stringify({ action: "snapshot", path }) }, signal, 45000);
  }
  projectSynthesis(path: string, kind: ProjectSynthesis["kind"], refresh = false, signal?: AbortSignal) {
    return this.intelligence<ProjectSynthesis>("project-brain", { method: "POST", body: JSON.stringify({ action: "generate", path, kind, refresh }) }, signal, 70000);
  }
  evolution(scope: EvolutionScope, signal?: AbortSignal) {
    return this.intelligence<EvolutionSnapshot>("evolution", { method: "POST", body: JSON.stringify({ action: "snapshot", scope }) }, signal, 70000);
  }
  evolutionSynthesis(scope: EvolutionScope, refresh = false, signal?: AbortSignal) {
    return this.intelligence<EvolutionSynthesis>("evolution", { method: "POST", body: JSON.stringify({ action: "generate", scope, refresh }) }, signal, 70000);
  }
  async askSlate(request: AskPayload, onEvent: AskStreamHandler, signal?: AbortSignal) {
    const response = await fetch("/api/intelligence/ask", {
      method: "POST",
      cache: "no-store",
      signal: signal ? AbortSignal.any([signal, AbortSignal.timeout(70000)]) : AbortSignal.timeout(70000),
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${this.connection.token}`,
        "X-Slate-Server": this.connection.server,
      },
      body: JSON.stringify(request),
    });
    if (!response.ok || !response.body) {
      const message = response.status === 503 ? "Ask Slate isn't configured on this server." : "Ask Slate could not start this answer.";
      throw new ApiError(response.status, message);
    }
    if (!response.headers.get("content-type")?.toLowerCase().includes("text/event-stream")) throw new Error("Ask Slate returned an invalid stream.");
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = "";
    let received = 0;
    try {
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        received += value.byteLength;
        if (received > 2 * 1024 * 1024) throw new Error("Ask Slate response exceeded its safety limit.");
        buffer += decoder.decode(value, { stream: true });
        const frames = buffer.split(/\r?\n\r?\n/);
        buffer = frames.pop() ?? "";
        for (const frame of frames) {
          const line = frame.split(/\r?\n/).find((value) => value.startsWith("data:"));
          if (!line) continue;
          const event = JSON.parse(line.slice(5).trim()) as Parameters<AskStreamHandler>[0];
          if (!event || typeof event.type !== "string") throw new Error("Ask Slate returned an invalid stream.");
          onEvent(event);
        }
      }
    } finally {
      reader.releaseLock();
    }
  }
  async status(signal?: AbortSignal) {
    return z
      .object({
        libraryId: z.string().uuid(),
        noteCount: z.number(),
        serverVersion: z.string(),
        indexState: z.string(),
        git: z
          .object({
            state: z.string(),
            pending: z.boolean(),
            detail: z.string().nullable().optional(),
          })
          .nullable()
          .optional(),
      })
      .parse(await this.request<Status>("v1/status", {}, signal));
  }
  list(path = "", page = 0, signal?: AbortSignal) {
    return this.request<FolderPage>(
      `v1/library?path=${encodeURIComponent(path)}&page=${page}`,
      {},
      signal,
    );
  }
  async note(id: string, signal?: AbortSignal) {
    return noteSchema.parse(
      await this.request<Note>(`v1/notes/${id}`, {}, signal),
    );
  }
  async save(note: Note, markdown: string) {
    const saved = noteSchema.parse(
      await this.request<Note>(`v1/notes/${note.id}`, {
        method: "PUT",
        headers: { "If-Match": note.revision },
        body: JSON.stringify({ markdown, revision: note.revision }),
      }),
    );
    this.notifyIntelligence({ kind: "upsert", noteId: saved.id });
    return saved;
  }
  search(q: string, page = 0, signal?: AbortSignal) {
    return this.request<SearchPage>(
      `v1/search?q=${encodeURIComponent(q)}&page=${page}&pageSize=20`,
      {},
      signal,
    );
  }
  smartView(view: string, page = 0, signal?: AbortSignal) {
    return this.request<SearchPage>(
      `v1/views/${encodeURIComponent(view)}?page=${page}&pageSize=20`,
      {},
      signal,
    );
  }
  links(id: string, signal?: AbortSignal) {
    return this.request<Links>(`v1/notes/${id}/links`, {}, signal);
  }
  history(id: string, signal?: AbortSignal) {
    return this.request<HistoryEntry[]>(`v1/notes/${id}/history`, {}, signal);
  }
  historical(id: string, commit: string) {
    return this.request<HistoricalNote>(
      `v1/notes/${id}/history/${encodeURIComponent(commit)}`,
    );
  }
  restore(
    id: string,
    request: {
      commit: string;
      mode: "current" | "new" | "recover";
      revision?: string;
      folder?: string;
      name?: string;
    },
  ) {
    return this.post<Note>(`v1/notes/${id}/restore`, request).then((note) => {
      this.notifyIntelligence({ kind: "upsert", noteId: note.id });
      return note;
    });
  }
  recoverable(signal?: AbortSignal) {
    return this.request<RecoveryPage>("v1/history/deleted", {}, signal);
  }
  linkIssues(page = 0, signal?: AbortSignal) {
    return this.request<LinkIssuePage>(
      `v1/links/issues?page=${page}`,
      {},
      signal,
    );
  }
  previewLinkRepair(
    id: string,
    request: {
      sourceRevision: string;
      start: number;
      targetId: string;
      targetRevision: string;
    },
  ) {
    return this.post<NoteTextPreview>(
      `v1/notes/${id}/link-repair/preview`,
      request,
    );
  }
  applyLinkRepair(
    id: string,
    request: {
      sourceRevision: string;
      start: number;
      targetId: string;
      targetRevision: string;
    },
  ) {
    return this.post<Note>(`v1/notes/${id}/link-repair/apply`, request).then((note) => { this.notifyIntelligence({ kind: "upsert", noteId: note.id }); return note; });
  }
  previewWikiExport(id: string, signal?: AbortSignal) {
    return this.request<NoteTextPreview>(
      `v1/notes/${id}/wiki-export`,
      {},
      signal,
    );
  }
  applyWikiExport(id: string, sourceRevision: string, proposedMarkdown: string) {
    return this.post<Note>(`v1/notes/${id}/wiki-export`, {
      sourceRevision,
      proposedMarkdown,
    }).then((note) => { this.notifyIntelligence({ kind: "upsert", noteId: note.id }); return note; });
  }
  previewBulk(request: {
    operationId: string;
    operation: string;
    paths: string[];
    destinationFolderPath?: string;
    newName?: string;
  }) {
    return this.post<BulkPreview>("v1/library/bulk/preview", request);
  }
  applyBulk(request: {
    operationId: string;
    fingerprint: string;
    repairIncoming?: boolean;
  }) {
    return this.post<BulkResult>("v1/library/bulk/apply", request).then((result) => { this.notifyIntelligence({ kind: "reconcile", reason: "bulk" }); return result; });
  }
  bulkStatus(operationId: string, signal?: AbortSignal) {
    return this.request<BulkResult>(
      `v1/library/bulk/${operationId}`,
      {},
      signal,
    );
  }
  graph(
    id: string,
    options: { depth?: number; limit?: number; folder?: string; type?: string } = {},
    signal?: AbortSignal,
  ) {
    const params = new URLSearchParams({
      depth: String(options.depth ?? 1),
      limit: String(options.limit ?? 40),
    });
    if (options.folder) params.set("folder", options.folder);
    if (options.type) params.set("type", options.type);
    return this.request<KnowledgeGraph>(
      `v1/notes/${id}/graph?${params}`,
      {},
      signal,
    );
  }
  related(id: string, signal?: AbortSignal) {
    return this.request<RelatedNote[]>(
      `v1/notes/${id}/related`,
      {},
      signal,
    );
  }
  hybridRelated(id: string, signal?: AbortSignal) {
    return this.intelligence<RelatedNote[]>("related", { method: "POST", body: JSON.stringify({ noteId: id }) }, signal);
  }
  rediscover(view: string, today: string, page = 0, signal?: AbortSignal) {
    return this.request<RediscoveryPage>(
      `v1/rediscovery/${encodeURIComponent(view)}?today=${encodeURIComponent(today)}&page=${page}`,
      {},
      signal,
    );
  }
  daily(date: string) {
    return this.post<Note>("v1/workflows/daily", { date }).then((note) => { this.notifyIntelligence({ kind: "upsert", noteId: note.id }); return note; });
  }
  answer(id: string, answer: string, revision: string) {
    return this.post<Note>(`v1/notes/${id}/answer`, {
      answer,
      revision,
      answeredAt: new Date().toISOString(),
    }).then((note) => { this.notifyIntelligence({ kind: "upsert", noteId: note.id }); return note; });
  }
  assets(page = 0, unreferenced = false, signal?: AbortSignal) {
    return this.request<AssetPage>(
      `v1/assets?page=${page}&unreferenced=${unreferenced}`,
      {},
      signal,
    );
  }
  assetReferences(id: string, page = 0, signal?: AbortSignal) {
    return this.request<AssetReferencePage>(
      `v1/assets/${id}/references?page=${page}`,
      {},
      signal,
    );
  }
  assetText(id: string, signal?: AbortSignal) {
    return this.request<AssetDerivedText>(
      `v1/assets/${id}/text`,
      {},
      signal,
    );
  }
  extractAsset(id: string) {
    return this.post<AssetDerivedText>(`v1/assets/${id}/extract`);
  }
  create(
    folderPath: string,
    name: string,
    options: { title?: string; id?: string; initialMarkdown?: string } = {},
  ) {
    return this.post<Note>("v1/notes", { folderPath, name, ...options }).then((note) => {
      this.notifyIntelligence({ kind: "upsert", noteId: note.id });
      return note;
    });
  }
  question(folderPath: string, name: string, title: string, body: string) {
    const id = crypto.randomUUID();
    const instant = new Date().toISOString();
    const safeTitle = title.trim().replace(/[\r\n]+/g, " ");
    const initialMarkdown = [
      "---",
      `id: ${id}`,
      "type: question",
      "status: open",
      `created: ${JSON.stringify(instant)}`,
      `updated: ${JSON.stringify(instant)}`,
      "---",
      "",
      `# ${safeTitle}`,
      "",
      body.trim(),
      "",
    ].join("\n");
    return this.create(folderPath, name, {
      id,
      title: safeTitle,
      initialMarkdown,
    });
  }
  folder(parentPath: string, name: string) {
    return this.post<Mutation>("v1/folders", { parentPath, name });
  }
  capture(captureId: string, content: string, capturedAt: string) {
    return this.post<Note>("v1/captures", {
      captureId,
      content,
      capturedAt,
      kind: "quick-thought",
    }).then((note) => { this.notifyIntelligence({ kind: "upsert", noteId: note.id }); return note; });
  }
  rename(path: string, newName: string) {
    return this.post<Mutation>("v1/library/rename", { path, newName }).then((result) => { this.notifyIntelligence({ kind: "rename", path, destinationPath: result.path }); return result; });
  }
  transfer(
    kind: "move" | "copy",
    sourcePath: string,
    destinationFolderPath: string,
  ) {
    return this.post<Mutation>(`v1/library/${kind}`, {
      sourcePath,
      destinationFolderPath,
    }).then((result) => { this.notifyIntelligence({ kind: "reconcile", reason: "bulk" }); return result; });
  }
  details(path: string) {
    return this.request<{ descendantCount: number; isDirectory: boolean }>(
      `v1/library/item?path=${encodeURIComponent(path)}`,
    );
  }
  delete(path: string, recursive: boolean) {
    return this.post<Mutation>("v1/library/delete", { path, recursive }).then((result) => { this.notifyIntelligence({ kind: "delete", path }); return result; });
  }
  sync() {
    return this.post<{ state: string; pending: boolean; detail?: string }>(
      "v1/sync",
    ).then((result) => { this.notifyIntelligence({ kind: "reconcile", reason: "sync" }); return result; });
  }
  refresh() {
    return this.post<void>("v1/library/refresh").then((result) => { this.notifyIntelligence({ kind: "reconcile", reason: "refresh" }); return result; });
  }
  asset(id: string, signal?: AbortSignal) {
    return this.request<Blob>(
      `v1/assets/${id}`,
      { headers: { Accept: "application/octet-stream" } },
      signal,
    );
  }
  metadata(id: string) {
    return this.request<Asset>(`v1/assets/${id}/metadata`);
  }
  async upload(file: File, id = crypto.randomUUID()) {
    const hash = Array.from(
      new Uint8Array(
        await crypto.subtle.digest("SHA-256", await file.arrayBuffer()),
      ),
    )
      .map((n) => n.toString(16).padStart(2, "0"))
      .join("");
    return this.request<Asset>(
      `v1/assets?id=${id}&filename=${encodeURIComponent(file.name)}`,
      {
        method: "POST",
        body: file,
        headers: {
          "Content-Type": file.type || "application/octet-stream",
          "X-Slate-Sha256": hash,
        },
      },
    );
  }
}

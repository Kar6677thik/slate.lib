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
    return noteSchema.parse(
      await this.request<Note>(`v1/notes/${note.id}`, {
        method: "PUT",
        headers: { "If-Match": note.revision },
        body: JSON.stringify({ markdown, revision: note.revision }),
      }),
    );
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
    return this.post<Note>(`v1/notes/${id}/restore`, request);
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
    return this.post<Note>(`v1/notes/${id}/link-repair/apply`, request);
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
    });
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
    return this.post<BulkResult>("v1/library/bulk/apply", request);
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
  rediscover(view: string, today: string, page = 0, signal?: AbortSignal) {
    return this.request<RediscoveryPage>(
      `v1/rediscovery/${encodeURIComponent(view)}?today=${encodeURIComponent(today)}&page=${page}`,
      {},
      signal,
    );
  }
  daily(date: string) {
    return this.post<Note>("v1/workflows/daily", { date });
  }
  answer(id: string, answer: string, revision: string) {
    return this.post<Note>(`v1/notes/${id}/answer`, {
      answer,
      revision,
      answeredAt: new Date().toISOString(),
    });
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
    return this.post<Note>("v1/notes", { folderPath, name, ...options });
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
    });
  }
  rename(path: string, newName: string) {
    return this.post<Mutation>("v1/library/rename", { path, newName });
  }
  transfer(
    kind: "move" | "copy",
    sourcePath: string,
    destinationFolderPath: string,
  ) {
    return this.post<Mutation>(`v1/library/${kind}`, {
      sourcePath,
      destinationFolderPath,
    });
  }
  details(path: string) {
    return this.request<{ descendantCount: number; isDirectory: boolean }>(
      `v1/library/item?path=${encodeURIComponent(path)}`,
    );
  }
  delete(path: string, recursive: boolean) {
    return this.post<Mutation>("v1/library/delete", { path, recursive });
  }
  sync() {
    return this.post<{ state: string; pending: boolean; detail?: string }>(
      "v1/sync",
    );
  }
  refresh() {
    return this.post<void>("v1/library/refresh");
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

import postgres, { type Sql } from "postgres";
import type { Note } from "@/lib/api/contracts";
import type { DerivedStore, EmbeddedChunk, EmbeddingProvider, IndexEvent, IntelligenceStatus, SemanticHit } from "./types";

function vector(values: number[]) { return `[${values.map((value) => Number(value.toFixed(8))).join(",")}]`; }
function eventKey(libraryId: string, event: IndexEvent) { const suffix = event.kind === "upsert" ? `upsert:${event.noteId}` : event.kind === "delete" ? `delete:${event.noteId ?? event.path}` : `${event.kind}:${"path" in event ? event.path : event.reason}`; return `${libraryId}:${suffix}`; }

export class PostgresDerivedStore implements DerivedStore {
  private sql: Sql | null = null;
  private initialized = false;
  private client() {
    if (!process.env.SLATE_INTELLIGENCE_DATABASE_URL) throw new Error("SLATE_INTELLIGENCE_DATABASE_URL is not configured");
    return this.sql ??= postgres(process.env.SLATE_INTELLIGENCE_DATABASE_URL, { max: 5, idle_timeout: 20, connect_timeout: 5, prepare: false });
  }
  async initialize(provider: EmbeddingProvider): Promise<void> {
    if (this.initialized) return;
    if (!provider.available || !Number.isInteger(provider.dimensions) || provider.dimensions < 1 || provider.dimensions > 2000) throw new Error("Embedding provider is unavailable or its dimensions exceed pgvector HNSW limits");
    const sql = this.client();
    await sql`create extension if not exists vector`;
    await sql`create table if not exists intelligence_meta (key text primary key, value text not null, updated_at timestamptz not null default now())`;
    const schema = await sql`select value from intelligence_meta where key='schema_version'`;
    if (schema.length && schema[0].value !== "1") throw new Error("Unsupported intelligence schema; rebuild or migrate the derived database");
    await sql`insert into intelligence_meta(key,value) values('schema_version','1') on conflict(key) do nothing`;
    const dims = Math.trunc(provider.dimensions);
    await sql.unsafe(`create table if not exists intelligence_chunks (
      id text not null, library_id uuid not null, note_id uuid not null, path text not null, title text not null, heading text,
      ordinal integer not null, body text not null, content_hash text not null, revision text not null,
      tags text[] not null default '{}', note_type text, status text, provider text not null, model text not null,
      embedding vector(${dims}) not null, indexed_at timestamptz not null default now(), primary key(library_id,id)
    )`);
    await sql`create index if not exists intelligence_chunks_note on intelligence_chunks(library_id,note_id)`;
    await sql`create index if not exists intelligence_chunks_path on intelligence_chunks(library_id,path)`;
    await sql`create index if not exists intelligence_chunks_hash on intelligence_chunks(library_id,content_hash,provider,model)`;
    await sql.unsafe("create index if not exists intelligence_chunks_embedding on intelligence_chunks using hnsw (embedding vector_cosine_ops)");
    await sql`create table if not exists intelligence_jobs (
      id bigserial primary key, library_id uuid not null, event jsonb not null, event_key text not null unique, state text not null default 'pending',
      attempts integer not null default 0, available_at timestamptz not null default now(), lease_until timestamptz,
      last_error text, created_at timestamptz not null default now(), updated_at timestamptz not null default now()
    )`;
    const existing = await sql`select value from intelligence_meta where key='embedding_signature'`;
    const signature = `${provider.name}:${provider.model}:${provider.dimensions}`;
    if (existing.length && existing[0].value !== signature) {
      await sql`drop table if exists intelligence_chunks`;
      await sql`delete from intelligence_jobs`;
      await sql`delete from intelligence_meta where key like 'reconcile_enabled:%'`;
      await sql`update intelligence_meta set value=${signature},updated_at=now() where key='embedding_signature'`;
      this.initialized = false;
      return this.initialize(provider);
    }
    await sql`insert into intelligence_meta(key,value) values('embedding_signature',${signature}) on conflict(key) do update set value=excluded.value, updated_at=now()`;
    this.initialized = true;
  }
  async getReusableEmbeddings(libraryId: string, hashes: string[], provider: EmbeddingProvider) {
    if (!hashes.length) return new Map<string, number[]>();
    const rows = await this.client()`select distinct on (content_hash) content_hash, embedding::text as embedding from intelligence_chunks where library_id=${libraryId} and content_hash = any(${hashes}) and provider=${provider.name} and model=${provider.model}`;
    return new Map(rows.map((row) => [String(row.content_hash), JSON.parse(String(row.embedding).replace(/^\[/, "[").replace(/\]$/, "]")) as number[]]));
  }
  async replaceNote(libraryId: string, note: Note, chunks: EmbeddedChunk[]) {
    const sql = this.client();
    await sql.begin(async (tx) => {
      await tx`delete from intelligence_chunks where library_id=${libraryId} and note_id=${note.id}`;
      for (const chunk of chunks) await tx`
        insert into intelligence_chunks(id,library_id,note_id,path,title,heading,ordinal,body,content_hash,revision,tags,note_type,status,provider,model,embedding)
        values(${chunk.id},${libraryId},${chunk.noteId},${chunk.path},${chunk.title},${chunk.heading},${chunk.ordinal},${chunk.text},${chunk.contentHash},${chunk.revision},${chunk.tags},${chunk.type},${chunk.status},${chunk.provider},${chunk.model},${vector(chunk.embedding)}::vector)
      `;
      await tx`update intelligence_jobs set state='done', updated_at=now() where library_id=${libraryId} and event->>'noteId'=${note.id} and state in ('pending','indexing')`;
    });
  }
  async deleteNote(libraryId: string, noteId?: string, path?: string) {
    if (noteId) await this.client()`delete from intelligence_chunks where library_id=${libraryId} and note_id=${noteId}`;
    else if (path) await this.client()`delete from intelligence_chunks where library_id=${libraryId} and (path=${path} or path like ${`${path}/%`})`;
  }
  async semanticSearch(libraryId: string, embedding: number[], _query: string, limit: number, filters: { path?: string; tag?: string; type?: string; status?: string } = {}): Promise<SemanticHit[]> {
    const sql = this.client();
    const rows = await this.client()`
      with candidates as (
        select note_id, title, path, heading, body, revision, id, embedding <=> ${vector(embedding)}::vector as distance
        from intelligence_chunks
        where library_id=${libraryId}
          and ${filters.path ? sql`path like ${`${filters.path.replace(/[%_]/g, "\\$&")}%`}` : sql`true`}
          and ${filters.tag ? sql`${filters.tag} = any(tags)` : sql`true`}
          and ${filters.type ? sql`note_type = ${filters.type}` : sql`true`}
          and ${filters.status ? sql`status = ${filters.status}` : sql`true`}
        order by embedding <=> ${vector(embedding)}::vector
        limit ${Math.min(Math.max(limit * 8, 120), 320)}
      ), best as (
        select distinct on (note_id) * from candidates order by note_id, distance
      )
      select note_id, title, path, heading, body, revision, id, 1 - distance as similarity
      from best order by distance limit ${Math.min(Math.max(limit, 20), 80)}
    `;
    return rows.map((row, index) => ({
      id: String(row.note_id), title: String(row.title), path: String(row.path), heading: row.heading ? String(row.heading) : null,
      snippet: String(row.body).slice(0, 360), revision: String(row.revision), score: Number(row.similarity),
      semanticRank: index + 1, similarity: Number(row.similarity), chunkId: String(row.id),
    }));
  }
  async status(libraryId: string, provider: EmbeddingProvider): Promise<IntelligenceStatus> {
    if (!provider.available || !process.env.SLATE_INTELLIGENCE_DATABASE_URL) return { enabled: false, state: "unavailable", provider: provider.name, model: provider.model, dimensions: provider.dimensions, noteCount: 0, chunkCount: 0, pendingJobs: 0, failedJobs: 0, embeddedThisRun: 0, reusedThisRun: 0, failedChunksThisRun: 0, lastIndexedAt: null, lastError: null };
    try {
      await this.initialize(provider);
      const [counts] = await this.client()`select count(distinct note_id)::int notes, count(*)::int chunks, max(indexed_at)::text last from intelligence_chunks where library_id=${libraryId}`;
      const [jobs] = await this.client()`select count(*) filter(where state='pending')::int pending, count(*) filter(where state='indexing')::int indexing, count(*) filter(where state='failed')::int failed, max(last_error) filter(where state='failed') error from intelligence_jobs where library_id=${libraryId}`;
      return { enabled: true, state: Number(jobs.indexing) ? "indexing" : Number(jobs.pending) ? "pending" : Number(jobs.failed) ? "failed" : "ready", provider: provider.name, model: provider.model, dimensions: provider.dimensions, noteCount: Number(counts.notes), chunkCount: Number(counts.chunks), pendingJobs: Number(jobs.pending) + Number(jobs.indexing), failedJobs: Number(jobs.failed), embeddedThisRun: 0, reusedThisRun: 0, failedChunksThisRun: 0, lastIndexedAt: counts.last ? String(counts.last) : null, lastError: jobs.error ? String(jobs.error) : null };
    } catch { return { enabled: true, state: "unavailable", provider: provider.name, model: provider.model, dimensions: provider.dimensions, noteCount: 0, chunkCount: 0, pendingJobs: 0, failedJobs: 0, embeddedThisRun: 0, reusedThisRun: 0, failedChunksThisRun: 0, lastIndexedAt: null, lastError: "Derived index unavailable" }; }
  }
  async enqueue(libraryId: string, event: IndexEvent) {
    const key = eventKey(libraryId, event);
    await this.client()`insert into intelligence_jobs(library_id,event,event_key) values(${libraryId},${JSON.stringify(event)}::jsonb,${key}) on conflict(event_key) do update set event=excluded.event,state='pending',attempts=0,available_at=now(),lease_until=null,last_error=null,updated_at=now()`;
  }
  async finish(libraryId: string, event: IndexEvent) { await this.client()`update intelligence_jobs set state='done',lease_until=null,last_error=null,updated_at=now() where event_key=${eventKey(libraryId,event)}`; }
  async fail(libraryId: string, event: IndexEvent) { await this.client()`update intelligence_jobs set state=case when attempts >= 4 then 'failed' else 'pending' end, attempts=attempts+1, available_at=now() + make_interval(secs => least(300, power(2, attempts)::int)), lease_until=null,last_error='Indexing failed', updated_at=now() where event_key=${eventKey(libraryId,event)}`; }
  async claim(libraryId: string, limit = 4): Promise<IndexEvent[]> {
    const rows = await this.client()`
      with candidates as (
        select id from intelligence_jobs where library_id=${libraryId} and ((state='pending' and available_at <= now()) or (state='indexing' and lease_until <= now()))
        order by created_at for update skip locked limit ${Math.min(Math.max(limit, 1), 8)}
      )
      update intelligence_jobs jobs set state='indexing',lease_until=now() + interval '5 minutes',updated_at=now()
      from candidates where jobs.id=candidates.id returning jobs.event
    `;
    return rows.map((row) => typeof row.event === "string" ? JSON.parse(row.event) as IndexEvent : row.event as IndexEvent);
  }
  async pruneNotes(libraryId: string, noteIds: string[]) {
    if (!noteIds.length) await this.client()`delete from intelligence_chunks where library_id=${libraryId}`;
    else await this.client()`delete from intelligence_chunks where library_id=${libraryId} and not (note_id = any(${noteIds}::uuid[]))`;
  }
  async reconciliationEnabled(libraryId: string) {
    const rows = await this.client()`select value from intelligence_meta where key=${`reconcile_enabled:${libraryId}`}`;
    return rows[0]?.value === "true";
  }
  async enableReconciliation(libraryId: string) {
    await this.client()`insert into intelligence_meta(key,value) values(${`reconcile_enabled:${libraryId}`},'true') on conflict(key) do update set value='true',updated_at=now()`;
  }
  async indexedNoteIds(libraryId: string) {
    const rows = await this.client()`select distinct note_id::text id from intelligence_chunks where library_id=${libraryId}`;
    return rows.map((row) => String(row.id));
  }
}

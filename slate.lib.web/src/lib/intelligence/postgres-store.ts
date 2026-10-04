import postgres, { type Sql } from "postgres";
import type { Note } from "@/lib/api/contracts";
import type { DerivedStore, EmbeddedChunk, EmbeddingProvider, IndexEvent, IntelligenceStatus, SemanticHit } from "./types";
import type { KnowledgeClaim, KnowledgeIssue } from "./knowledge-issues";
import type { KnowledgeOverlap, OverlapNote } from "./overlap";
import type { LinkSuggestion } from "./smart-links";
import { conceptHash, type ConceptMember, type KnowledgeConcept } from "./concepts";
import type { KnowledgeGap } from "./knowledge-gaps";
import type { InboxTriageAnalysis } from "./inbox-triage";

function vector(values: number[]) { return `[${values.map((value) => Number(value.toFixed(8))).join(",")}]`; }
function eventKey(libraryId: string, event: IndexEvent) { const suffix = event.kind === "upsert" ? `upsert:${event.noteId}` : event.kind === "delete" ? `delete:${event.noteId ?? event.path}` : `${event.kind}:${"path" in event ? event.path : event.reason}`; return `${libraryId}:${suffix}`; }
function aggregateConcept(base: KnowledgeConcept, members: ConceptMember[]): KnowledgeConcept {
  const dates = members.flatMap((member) => member.timestamp ? [member.timestamp] : []).sort();
  const projectPaths = [...new Set(members.flatMap((member) => member.projectPath ? [member.projectPath] : []))];
  const openQuestionCount = members.reduce((sum, member) => sum + member.questions.length, 0);
  return {
    ...base,
    sourceCount: members.length,
    currentSourceCount: members.length,
    firstSeen: dates[0] ?? null,
    lastSeen: dates.at(-1) ?? null,
    projectPaths,
    tags: [...new Set(members.flatMap((member) => member.tags))].slice(0, 20),
    openQuestionCount,
    contentHash: conceptHash(members.map((member) => `${member.noteId}:${member.revision}`).sort().join("|")),
    members,
    relationships: [],
  };
}

export class PostgresDerivedStore implements DerivedStore {
  private sql: Sql | null = null;
  private initialized = false;
  private client() {
    if (!process.env.SLATE_INTELLIGENCE_DATABASE_URL) throw new Error("SLATE_INTELLIGENCE_DATABASE_URL is not configured");
    return this.sql ??= postgres(process.env.SLATE_INTELLIGENCE_DATABASE_URL, { max: 5, idle_timeout: 20, connect_timeout: 5, prepare: false });
  }
  async initializeKnowledge(): Promise<void> {
    const sql = this.client();
    await sql`create table if not exists intelligence_claims (
      library_id uuid not null, claim_id text not null, note_id uuid not null, revision text not null,
      content_hash text not null, subject text not null, predicate text not null, claim_type text not null,
      current boolean not null default true, project_path text, payload jsonb not null,
      indexed_at timestamptz not null default now(), primary key(library_id,claim_id)
    )`;
    await sql`create index if not exists intelligence_claims_note on intelligence_claims(library_id,note_id)`;
    await sql`create index if not exists intelligence_claims_subject on intelligence_claims(library_id,subject,predicate)`;
    await sql`create table if not exists intelligence_knowledge_issues (
      library_id uuid not null, fingerprint text not null, kind text not null, state text not null default 'open',
      claim_a_hash text not null, claim_b_hash text not null, project_path text, payload jsonb not null,
      analyzed_at timestamptz not null default now(), primary key(library_id,fingerprint)
    )`;
    await sql`create index if not exists intelligence_issues_project on intelligence_knowledge_issues(library_id,project_path,state)`;
    await sql`create table if not exists intelligence_overlap_notes (
      library_id uuid not null, note_id uuid not null, revision text not null, content_hash text not null,
      project_path text, payload jsonb not null, indexed_at timestamptz not null default now(), primary key(library_id,note_id)
    )`;
    await sql`create index if not exists intelligence_overlap_hash on intelligence_overlap_notes(library_id,content_hash)`;
    await sql`create index if not exists intelligence_overlap_project on intelligence_overlap_notes(library_id,project_path)`;
    await sql`create table if not exists intelligence_overlaps (
      library_id uuid not null, fingerprint text not null, kind text not null, note_a_hash text not null, note_b_hash text not null,
      project_path text, payload jsonb not null, analyzed_at timestamptz not null default now(), primary key(library_id,fingerprint)
    )`;
    await sql`create index if not exists intelligence_overlaps_project on intelligence_overlaps(library_id,project_path,kind)`;
    await sql`create table if not exists intelligence_link_suggestions (
      library_id uuid not null, fingerprint text not null, source_note_id uuid not null, target_note_id uuid not null,
      source_hash text not null, target_hash text not null, suggestion_type text not null, project_path text,
      payload jsonb not null, analyzed_at timestamptz not null default now(), primary key(library_id,fingerprint)
    )`;
    await sql`create index if not exists intelligence_link_source on intelligence_link_suggestions(library_id,source_note_id,suggestion_type)`;
    await sql`create index if not exists intelligence_link_target on intelligence_link_suggestions(library_id,target_note_id)`;
    await sql`create index if not exists intelligence_link_project on intelligence_link_suggestions(library_id,project_path,suggestion_type)`;
    await sql`create table if not exists intelligence_concepts (
      library_id uuid not null, concept_id text not null, normalized_name text not null, canonical_name text not null,
      kind text not null, content_hash text not null, schema_version integer not null, payload jsonb not null,
      indexed_at timestamptz not null default now(), primary key(library_id,concept_id)
    )`;
    await sql`create unique index if not exists intelligence_concepts_name on intelligence_concepts(library_id,normalized_name)`;
    await sql`create table if not exists intelligence_concept_memberships (
      library_id uuid not null, concept_id text not null, note_id uuid not null, revision text not null,
      strength text not null, project_path text, payload jsonb not null, indexed_at timestamptz not null default now(),
      primary key(library_id,concept_id,note_id)
    )`;
    await sql`create index if not exists intelligence_concept_member_note on intelligence_concept_memberships(library_id,note_id)`;
    await sql`create index if not exists intelligence_concept_member_project on intelligence_concept_memberships(library_id,project_path,concept_id)`;
    await sql`create table if not exists intelligence_concept_relationships (
      library_id uuid not null, source_concept_id text not null, target_concept_id text not null,
      relationship_type text not null, schema_version integer not null, payload jsonb not null,
      indexed_at timestamptz not null default now(), primary key(library_id,source_concept_id,target_concept_id,relationship_type)
    )`;
    await sql`create index if not exists intelligence_concept_rel_target on intelligence_concept_relationships(library_id,target_concept_id)`;
    await sql`create table if not exists intelligence_knowledge_gaps (
      library_id uuid not null, fingerprint text not null, kind text not null, concept_id text,
      project_path text, importance text not null, schema_version integer not null, payload jsonb not null,
      analyzed_at timestamptz not null default now(), primary key(library_id,fingerprint)
    )`;
    await sql`create index if not exists intelligence_gaps_project on intelligence_knowledge_gaps(library_id,project_path,kind)`;
    await sql`create index if not exists intelligence_gaps_concept on intelligence_knowledge_gaps(library_id,concept_id,kind)`;
    await sql`create table if not exists intelligence_inbox_triage (
      library_id uuid not null, capture_id uuid not null, capture_hash text not null,
      source_fingerprint text not null, suggested_type text not null, schema_version integer not null,
      payload jsonb not null, analyzed_at timestamptz not null default now(), primary key(library_id,capture_id)
    )`;
    await sql`create index if not exists intelligence_inbox_triage_type on intelligence_inbox_triage(library_id,suggested_type,analyzed_at)`;
  }
  async replaceKnowledgeClaims(libraryId: string, noteIds: string[], claims: KnowledgeClaim[]) {
    await this.initializeKnowledge();
    if (!noteIds.length) return;
    const sql = this.client();
    await sql.begin(async (tx) => {
      await tx`delete from intelligence_claims where library_id=${libraryId} and note_id = any(${noteIds}::uuid[])`;
      for (const claim of claims) await tx`
        insert into intelligence_claims(library_id,claim_id,note_id,revision,content_hash,subject,predicate,claim_type,current,project_path,payload)
        values(${libraryId},${claim.claimId},${claim.noteId},${claim.revision},${claim.contentHash},${claim.normalizedSubject},${claim.normalizedPredicate},${claim.claimType},${claim.current},${claim.projectPath},${JSON.stringify(claim)}::jsonb)
      `;
    });
  }
  async replaceKnowledgeIssues(libraryId: string, issues: KnowledgeIssue[]) {
    await this.initializeKnowledge();
    const sql = this.client();
    await sql.begin(async (tx) => {
      for (const issue of issues) await tx`
        insert into intelligence_knowledge_issues(library_id,fingerprint,kind,state,claim_a_hash,claim_b_hash,project_path,payload)
        values(${libraryId},${issue.fingerprint},${issue.kind},${issue.state},${issue.sourceA.contentHash},${issue.sourceB.contentHash},${issue.projectPath},${JSON.stringify(issue)}::jsonb)
        on conflict(library_id,fingerprint) do update set kind=excluded.kind,claim_a_hash=excluded.claim_a_hash,claim_b_hash=excluded.claim_b_hash,project_path=excluded.project_path,payload=excluded.payload,analyzed_at=now()
      `;
    });
  }
  async replaceOverlapNotes(libraryId: string, noteIds: string[], notes: OverlapNote[]) {
    await this.initializeKnowledge(); if (!noteIds.length) return; const sql = this.client();
    await sql.begin(async (tx) => {
      await tx`delete from intelligence_overlap_notes where library_id=${libraryId} and note_id = any(${noteIds}::uuid[])`;
      for (const note of notes) await tx`insert into intelligence_overlap_notes(library_id,note_id,revision,content_hash,project_path,payload) values(${libraryId},${note.noteId},${note.revision},${note.contentHash},${note.projectPath},${JSON.stringify(note)}::jsonb)`;
    });
  }
  async replaceOverlapFindings(libraryId: string, findings: KnowledgeOverlap[]) {
    await this.initializeKnowledge(); const sql = this.client();
    await sql.begin(async (tx) => { for (const item of findings) await tx`
      insert into intelligence_overlaps(library_id,fingerprint,kind,note_a_hash,note_b_hash,project_path,payload)
      values(${libraryId},${item.fingerprint},${item.kind},${item.noteA.contentHash},${item.noteB.contentHash},${item.projectPath},${JSON.stringify(item)}::jsonb)
      on conflict(library_id,fingerprint) do update set kind=excluded.kind,note_a_hash=excluded.note_a_hash,note_b_hash=excluded.note_b_hash,project_path=excluded.project_path,payload=excluded.payload,analyzed_at=now()
    `; });
  }
  async replaceLinkSuggestions(libraryId: string, sourceNoteIds: string[], suggestions: LinkSuggestion[]) {
    await this.initializeKnowledge(); const sql = this.client();
    await sql.begin(async (tx) => {
      if (sourceNoteIds.length) await tx`delete from intelligence_link_suggestions where library_id=${libraryId} and source_note_id = any(${sourceNoteIds}::uuid[])`;
      for (const item of suggestions) await tx`
        insert into intelligence_link_suggestions(library_id,fingerprint,source_note_id,target_note_id,source_hash,target_hash,suggestion_type,project_path,payload)
        values(${libraryId},${item.fingerprint},${item.sourceNoteId},${item.target.noteId},${item.sourceHash},${item.target.contentHash},${item.suggestionType},${item.projectPath},${JSON.stringify(item)}::jsonb)
        on conflict(library_id,fingerprint) do update set source_hash=excluded.source_hash,target_hash=excluded.target_hash,suggestion_type=excluded.suggestion_type,project_path=excluded.project_path,payload=excluded.payload,analyzed_at=now()
      `;
    });
  }
  async invalidateLinkSuggestions(libraryId: string, noteId: string) {
    await this.initializeKnowledge(); await this.client()`delete from intelligence_link_suggestions where library_id=${libraryId} and (source_note_id=${noteId} or target_note_id=${noteId})`;
  }
  async replaceConceptIndex(libraryId: string, concepts: KnowledgeConcept[]) {
    await this.initializeKnowledge(); const sql = this.client();
    await sql.begin(async (tx) => {
      await tx`delete from intelligence_concept_relationships where library_id=${libraryId}`;
      await tx`delete from intelligence_concept_memberships where library_id=${libraryId}`;
      await tx`delete from intelligence_concepts where library_id=${libraryId}`;
      for (const concept of concepts) {
        await tx`insert into intelligence_concepts(library_id,concept_id,normalized_name,canonical_name,kind,content_hash,schema_version,payload) values(${libraryId},${concept.id},${concept.normalizedName},${concept.canonicalName},${concept.kind},${concept.contentHash},${concept.schemaVersion},${JSON.stringify(concept)}::jsonb)`;
        for (const member of concept.members) await tx`insert into intelligence_concept_memberships(library_id,concept_id,note_id,revision,strength,project_path,payload) values(${libraryId},${concept.id},${member.noteId},${member.revision},${member.strength},${member.projectPath},${JSON.stringify(member)}::jsonb)`;
        for (const relationship of concept.relationships) await tx`insert into intelligence_concept_relationships(library_id,source_concept_id,target_concept_id,relationship_type,schema_version,payload) values(${libraryId},${concept.id},${relationship.targetConceptId},${relationship.relationshipType},${relationship.schemaVersion},${JSON.stringify(relationship)}::jsonb)`;
      }
    });
  }
  async replaceConceptNote(libraryId: string, noteId: string, concepts: KnowledgeConcept[]) {
    await this.initializeKnowledge(); const sql = this.client(); const incoming = new Map(concepts.map((concept) => [concept.id, concept]));
    await sql.begin(async (tx) => {
      const incomingIds = [...incoming.keys()];
      const affected = await tx`
        select distinct c.concept_id,c.payload from intelligence_concepts c
        left join intelligence_concept_memberships m on m.library_id=c.library_id and m.concept_id=c.concept_id
        where c.library_id=${libraryId} and (m.note_id=${noteId} or c.concept_id = any(${incomingIds}::text[]))
      `;
      const affectedIds = [...new Set([...affected.map((row) => String(row.concept_id)), ...incomingIds])];
      if (affectedIds.length) await tx`delete from intelligence_concept_relationships where library_id=${libraryId} and (source_concept_id = any(${affectedIds}::text[]) or target_concept_id = any(${affectedIds}::text[]))`;
      await tx`delete from intelligence_concept_memberships where library_id=${libraryId} and note_id=${noteId}`;
      const previous = new Map(affected.map((row) => [String(row.concept_id), (typeof row.payload === "string" ? JSON.parse(row.payload) : row.payload) as KnowledgeConcept]));
      for (const conceptId of affectedIds) {
        const next = incoming.get(conceptId), base = next ?? previous.get(conceptId); if (!base) continue;
        const stored = await tx`select payload from intelligence_concept_memberships where library_id=${libraryId} and concept_id=${conceptId}`;
        const members = stored.map((row) => (typeof row.payload === "string" ? JSON.parse(row.payload) : row.payload) as ConceptMember);
        if (next?.members[0]) members.push(next.members[0]);
        if (!members.length) { await tx`delete from intelligence_concepts where library_id=${libraryId} and concept_id=${conceptId}`; continue; }
        const updated = aggregateConcept({ ...base, aliases: [...new Set([...(previous.get(conceptId)?.aliases ?? []), ...(next?.aliases ?? [])])].slice(0, 12) }, members);
        await tx`
          insert into intelligence_concepts(library_id,concept_id,normalized_name,canonical_name,kind,content_hash,schema_version,payload)
          values(${libraryId},${updated.id},${updated.normalizedName},${updated.canonicalName},${updated.kind},${updated.contentHash},${updated.schemaVersion},${JSON.stringify(updated)}::jsonb)
          on conflict(library_id,concept_id) do update set canonical_name=excluded.canonical_name,kind=excluded.kind,content_hash=excluded.content_hash,schema_version=excluded.schema_version,payload=excluded.payload,indexed_at=now()
        `;
        if (next?.members[0]) { const member = next.members[0]; await tx`insert into intelligence_concept_memberships(library_id,concept_id,note_id,revision,strength,project_path,payload) values(${libraryId},${updated.id},${member.noteId},${member.revision},${member.strength},${member.projectPath},${JSON.stringify(member)}::jsonb)`; }
      }
    });
  }
  async replaceKnowledgeGaps(libraryId: string, gaps: KnowledgeGap[]) {
    await this.initializeKnowledge(); const sql = this.client();
    await sql.begin(async (tx) => {
      await tx`delete from intelligence_knowledge_gaps where library_id=${libraryId}`;
      for (const gap of gaps) await tx`
        insert into intelligence_knowledge_gaps(library_id,fingerprint,kind,concept_id,project_path,importance,schema_version,payload)
        values(${libraryId},${gap.fingerprint},${gap.kind},${gap.conceptId},${gap.projectPath},${gap.importanceLevel},${gap.schemaVersion},${JSON.stringify(gap)}::jsonb)
      `;
    });
  }
  async replaceInboxTriage(libraryId: string, items: InboxTriageAnalysis[]) {
    await this.initializeKnowledge(); const sql=this.client();
    await sql.begin(async (tx) => {
      for (const item of items) await tx`
        insert into intelligence_inbox_triage(library_id,capture_id,capture_hash,source_fingerprint,suggested_type,schema_version,payload)
        values(${libraryId},${item.captureId},${item.captureHash},${item.sourceFingerprint},${item.suggestedType},${item.schemaVersion},${JSON.stringify(item)}::jsonb)
        on conflict(library_id,capture_id) do update set capture_hash=excluded.capture_hash,source_fingerprint=excluded.source_fingerprint,suggested_type=excluded.suggested_type,schema_version=excluded.schema_version,payload=excluded.payload,analyzed_at=now()
      `;
      if (items.length) await tx`delete from intelligence_inbox_triage where library_id=${libraryId} and capture_id != all(${items.map((item)=>item.captureId)}::uuid[])`;
    });
  }
  async readKnowledgeGaps(libraryId: string, limit = 240) {
    await this.initializeKnowledge(); const rows = await this.client()`select payload from intelligence_knowledge_gaps where library_id=${libraryId} order by analyzed_at desc limit ${Math.max(1, Math.min(limit, 240))}`;
    return rows.map((row) => (typeof row.payload === "string" ? JSON.parse(row.payload) : row.payload) as KnowledgeGap);
  }
  async invalidateKnowledgeGapNote(libraryId: string, noteId: string) {
    await this.initializeKnowledge(); await this.client()`delete from intelligence_knowledge_gaps where library_id=${libraryId} and payload @> ${JSON.stringify({ noteIds: [noteId] })}::jsonb`;
  }
  async readHealthDerived(libraryId: string) {
    await this.initializeKnowledge(); const sql = this.client();
    const settled = await Promise.allSettled([
      sql`select payload from intelligence_knowledge_issues where library_id=${libraryId} order by analyzed_at desc limit 360`,
      sql`select payload from intelligence_overlaps where library_id=${libraryId} order by analyzed_at desc limit 360`,
      sql`select payload from intelligence_link_suggestions where library_id=${libraryId} order by analyzed_at desc limit 360`,
      sql`select payload from intelligence_concepts where library_id=${libraryId} order by indexed_at desc limit 360`,
      sql`select payload from intelligence_knowledge_gaps where library_id=${libraryId} order by analyzed_at desc limit 40`,
    ]);
    const names = ["knowledge issues", "knowledge overlap", "link opportunities", "concepts", "knowledge gaps"];
    const failedSources = settled.flatMap((result, index) => result.status === "rejected" ? [names[index]] : []);
    const payloads = <T>(index: number) => settled[index].status === "fulfilled" ? settled[index].value.map((row) => (typeof row.payload === "string" ? JSON.parse(row.payload) : row.payload) as T) : [];
    return { knowledgeIssues: payloads<KnowledgeIssue>(0), overlaps: payloads<KnowledgeOverlap>(1), linkSuggestions: payloads<LinkSuggestion>(2), concepts: payloads<KnowledgeConcept>(3), knowledgeGaps: payloads<KnowledgeGap>(4), failedSources };
  }
  async invalidateConceptNote(libraryId: string, noteId: string) {
    await this.initializeKnowledge(); const sql = this.client();
    const rows = await sql`delete from intelligence_concept_memberships where library_id=${libraryId} and note_id=${noteId} returning concept_id`;
    if (rows.length) {
      const ids = rows.map((row) => String(row.concept_id));
      await sql`delete from intelligence_concept_relationships where library_id=${libraryId} and (source_concept_id = any(${ids}) or target_concept_id = any(${ids}))`;
      await sql`delete from intelligence_concepts where library_id=${libraryId} and concept_id = any(${ids})`;
    }
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
    try {
      await this.initializeKnowledge();
      if (noteId) await this.client()`delete from intelligence_claims where library_id=${libraryId} and note_id=${noteId}`;
      else if (path) await this.client()`delete from intelligence_claims where library_id=${libraryId} and (payload->>'path'=${path} or payload->>'path' like ${`${path}/%`})`;
      await this.client()`delete from intelligence_knowledge_issues where library_id=${libraryId} and (payload->'sourceA'->>'noteId'=${noteId ?? ""} or payload->'sourceB'->>'noteId'=${noteId ?? ""} or payload->'sourceA'->>'path' like ${path ? `${path}%` : "\u0000"} or payload->'sourceB'->>'path' like ${path ? `${path}%` : "\u0000"})`;
      if (noteId) await this.client()`delete from intelligence_overlap_notes where library_id=${libraryId} and note_id=${noteId}`;
      else if (path) await this.client()`delete from intelligence_overlap_notes where library_id=${libraryId} and (payload->>'path'=${path} or payload->>'path' like ${`${path}/%`})`;
      await this.client()`delete from intelligence_overlaps where library_id=${libraryId} and (payload->'noteA'->>'noteId'=${noteId ?? ""} or payload->'noteB'->>'noteId'=${noteId ?? ""} or payload->'noteA'->>'path' like ${path ? `${path}%` : "\u0000"} or payload->'noteB'->>'path' like ${path ? `${path}%` : "\u0000"})`;
      if (noteId) await this.client()`delete from intelligence_link_suggestions where library_id=${libraryId} and (source_note_id=${noteId} or target_note_id=${noteId})`;
      else if (path) await this.client()`delete from intelligence_link_suggestions where library_id=${libraryId} and (payload->>'sourcePath' like ${`${path}%`} or payload->'target'->>'path' like ${`${path}%`})`;
      if (noteId) await this.invalidateConceptNote(libraryId, noteId);
      else if (path) {
        const rows = await this.client()`delete from intelligence_concept_memberships where library_id=${libraryId} and (payload->>'path'=${path} or payload->>'path' like ${`${path}/%`}) returning concept_id`;
        const ids = rows.map((row) => String(row.concept_id));
        if (ids.length) {
          await this.client()`delete from intelligence_concept_relationships where library_id=${libraryId} and (source_concept_id = any(${ids}) or target_concept_id = any(${ids}))`;
          await this.client()`delete from intelligence_concepts where library_id=${libraryId} and concept_id = any(${ids})`;
        }
      }
    } catch { /* Derived knowledge storage may be disabled independently. */ }
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

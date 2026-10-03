# Slate web intelligence architecture

## Implemented retrieval boundary

`EmbeddingProvider` exposes query and document embedding, provider name, model, dimensions, and availability. Implementations are disabled by default, an OpenAI-compatible server adapter restricted to an explicit origin allowlist, and a normalized deterministic provider for tests.

Markdown chunking is deterministic and understands frontmatter, headings, paragraphs, lists, and fenced code. Chunks and large code sections are bounded. Metadata includes note ID, path, title, heading, ordinal, content hash, revision, tags, type, and status. Paths are metadata rather than embedding input, so moving unchanged content reuses vectors.

For document embeddings the provider receives the note title, current heading, and one bounded chunk. For queries it receives only free text after all canonical filter clauses are removed. `path:`, `tag:`, `type:`, and `status:` can filter derived rows directly; queries using canonical-only date or content-presence filters fall back to Lucene so those filters keep their existing meaning. The provider never receives tokens, server URLs, Git data, assets, unrelated notes, or browser storage. Enabling a provider does not automatically embed the library; an operator must confirm a rebuild.

The API surface is `POST /api/intelligence/search`, `POST /api/intelligence/ask`, `GET /api/intelligence/status`, `POST /api/intelligence/events`, and confirmation-gated `POST /api/intelligence/rebuild`. Search accepts bounded page/page-size values and never sends a vector or full index to the browser. Diagnostic search may expose lexical rank, semantic rank, fused score, matched source, heading, and chunk ID. Raw vectors are never returned. Ask returns bounded Server-Sent Events for retrieval state, source metadata, text deltas, final validated text, citations, and optional provider usage.

## Boundary

The intelligence layer is optional and web-owned. The existing Slate backend remains the only canonical source of Markdown, paths, UUIDs, assets, Git history, lexical results, links, and mutations. Derived records can always be deleted and rebuilt. No generated output changes canonical content until the user reviews and explicitly applies a normal Slate mutation.

Core Slate must work when intelligence is disabled or unavailable.

## Selected derived store

Milestone 3 uses PostgreSQL 16 with the `pgvector` extension for optional derived search data.

This choice is intentionally conservative: PostgreSQL has mature migrations, transactions, filtering, operational tooling, and bounded vector similarity in one service. `pgvector` avoids a second vector database and supports metadata filters needed for library, path, project, type, status, and date scopes. The deployment will use one private service and one derived-data volume. Loss of that volume must affect only intelligence features; a rebuild repopulates it from Slate.

SQLite remains appropriate for browser-local preferences and selected offline content, but a browser database is not suitable for server-side provider secrets or shared background indexing. A dedicated graph database is unnecessary because explicit Slate graph traversal already exists and is bounded by the canonical backend.

## Provider interfaces

Provider-specific embedding code implements the bounded `EmbeddingProvider` contract described above. `GenerationProvider` independently exposes full and streamed generation, provider and model identity, availability, and input capability. Implementations are disabled, deterministic fake outside production, and an OpenAI-compatible endpoint restricted to loopback or an explicit HTTPS origin allowlist. Real providers are selected only by server configuration.

Provider keys are server-side environment/secret values. They are never returned by API responses, rendered into client bundles, stored in IndexedDB, or accepted in URLs. Notes are untrusted document data and cannot issue tool instructions or authorize mutations.

## Ask retrieval and context construction

Each question resolves one explicit scope: library, note, folder/subtree, selected note IDs, or the deterministic top-level folder used as current project. Selected text is accepted only as a bounded explicit source for the current note. Library and folder scopes use canonical lexical candidates plus the existing semantic candidates. Comparisons issue bounded queries for both named concepts; rationale, temporal and unanswered-question language add deterministic evidence boosts. Temporal current-note questions may add at most two bounded historical versions.

The selected pack is capped at eight notes, twelve chunks, three chunks per note, 1,200 characters per chunk, and 12,000 total source characters. Duplicate source text is removed by hash and selection favors higher-scoring evidence across different notes. The final prompt also respects the provider's maximum input allowance after reserving configured output tokens and a safety margin; lowest-ranked sources and then oldest conversation turns are removed until it fits.

Prior conversation is capped at six turns and 8,000 characters. Questions are capped at 2,000 characters and explicit selected text at 4,000. The complete HTTP request is capped at 16 KiB. Every follow-up repeats retrieval with the bounded conversation and active scope.

## Citations and output validation

Every provided source receives a request-local citation ID in ranking order: `S1`, `S2`, and so on. Source metadata includes the canonical note ID, title, path, heading, chunk ordinal, revision, excerpt, and score. Generated `[S<n>]` references are accepted only when the ID exists in the supplied pack; unknown IDs are removed and the final citation list is deduplicated. Generated text is capped at 48,000 characters and provider responses at 2 MiB. The browser renders Markdown through Slate's sanitized reader with raw HTML disabled and never evaluates returned code.

## Prompt-injection and mutation boundary

System rules, bounded conversation, user question, and retrieved sources occupy separate prompt sections. Every excerpt is enclosed in explicit `BEGIN_UNTRUSTED_DOCUMENT_CONTENT` and `END_UNTRUSTED_DOCUMENT_CONTENT` markers. The system message says document instructions, role changes, links, command requests, and secret requests are evidence only and must never be followed. Ask Slate has no mutation or command tools and cannot edit, move, delete, sync, upload, or alter metadata.

## Streaming, conversations, and degraded mode

Generation streams as validated SSE frames. The UI preserves safe partial text and discovered sources when a request is stopped or fails, then offers Retry. Client cancellation is combined with a 65-second server deadline; provider calls have a 60-second deadline. A per-library in-process limit defaults to two active generations and is configurable from one to four.

Conversation messages are stored in session storage under the canonical browser workspace scope, capped at twelve display messages, and never written to canonical Markdown. New conversation and Clear remove local history. When generation is disabled, status reports the provider as unavailable, Ask controls are disabled, and search and every canonical feature continue to work.

Per-process diagnostics track request count, retrieved chunk count, currently active generations, and provider-reported input/output tokens. Missing provider usage is left unknown rather than estimated. These are operational counters, not billing analytics.

## Derived schema

All implemented rows are scoped by canonical `library_id` and stable `note_id` and carry the canonical revision used to produce them.

Implemented tables are:

- `intelligence_chunks`: bounded text chunks, heading metadata, content hashes, revision, filters, and embeddings.
- `intelligence_jobs`: pending/indexing/failed/done state, attempts, availability time, and bounded diagnostic text.
- `intelligence_meta`: schema version and the active provider/model/dimension signature.

The following names reserve the direction of later, separately reviewed milestones and are not created by Milestone 3:

- `intelligence_documents`: optional aggregate note processing metadata.
- `intelligence_entities` and `intelligence_note_entities`: extracted concepts with evidence spans.
- `intelligence_relations`: explainable derived edges with evidence and model/schema version.
- `intelligence_artifacts`: summaries and other cached structured results, never canonical content.

Migrations are forward-only and versioned. Model, prompt, chunker, and embedding changes invalidate only affected derived rows.

## Incremental indexing

Indexing observes canonical revisions and processes one note at a time. Create/update schedules that note; rename/move reuses vectors by content hash; delete removes derived rows. Sync, refresh, bulk changes, restore, and confirmed rebuild queue a paged reconciliation. Reconciliation replaces current notes, prunes missing notes, and reuses unchanged vectors.

Normal reading and editing never waits for a job. UI status is Ready, Pending, Indexing, Unavailable, or Failed; retries use bounded exponential backoff and an operator can rebuild explicitly.

## Feature flags

Semantic indexing and search are disabled until an embedding provider and derived database are configured. Ask Slate and Project Brain synthesis are independently disabled until a generation provider is configured. Their read-only deterministic views remain available. Later link suggestions, health analysis, OCR, daily brief, writing/rewrite features, autonomous actions, and note mutation remain absent. The client uses server-reported availability rather than assuming a provider exists.

## Project Brain retrieval and caching

The project boundary is the selected folder subtree. Discovery is metadata-first and capped at 120 folders and 2,000 entries; at most 36 candidate note bodies, 16 note histories, 80 timeline events, and 40 graph nodes are processed for one snapshot. Large projects are labeled bounded and remain searchable through the existing hybrid search with an explicit path filter.

Resume Project selects at most 16 excerpts and 16,000 source characters, with at most three excerpts per source class and two per note. It balances overview/readme, recent, architecture, decision, question, failure, risk, experiment, and important-source evidence. Recent and explicitly current evidence is preferred for Current State; older evidence remains visible in decisions and timeline. Source IDs, canonical revisions, and citation validation use the Ask Slate model.

Generated artifacts use an in-process bounded cache keyed by canonical library ID, normalized project path, selected source revisions plus latest history commits, provider, model, synthesis kind, and Project Brain schema version. Relevant canonical intelligence events clear that library's cache. Manual refresh bypasses the generated-artifact entry. The cache contains derived text and citations only and can be dropped without affecting Markdown or Git.

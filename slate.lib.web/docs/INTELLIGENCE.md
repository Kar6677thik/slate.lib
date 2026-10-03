# Slate web intelligence architecture

## Boundary

The intelligence layer is optional and web-owned. The existing Slate backend remains the only canonical source of Markdown, paths, UUIDs, assets, Git history, lexical results, links, and mutations. Derived records can always be deleted and rebuilt. No generated output changes canonical content until the user reviews and explicitly applies a normal Slate mutation.

Core Slate must work when intelligence is disabled or unavailable.

## Selected derived store

Use PostgreSQL 16 with the `pgvector` extension when intelligence milestones begin.

This choice is intentionally conservative: PostgreSQL has mature migrations, transactions, filtering, operational tooling, and bounded vector similarity in one service. `pgvector` avoids a second vector database and supports metadata filters needed for library, path, project, type, status, and date scopes. The deployment will use one private service and one derived-data volume. Loss of that volume must affect only intelligence features; a rebuild repopulates it from Slate.

SQLite remains appropriate for browser-local preferences and selected offline content, but a browser database is not suitable for server-side provider secrets or shared background indexing. A dedicated graph database is unnecessary because explicit Slate graph traversal already exists and is bounded by the canonical backend.

## Provider interface

Provider-specific code will implement an `IntelligenceProvider` contract with bounded operations for embeddings, generation, structured extraction, summarization, and grounded question answering. The first implementation is a disabled provider plus a deterministic fake used in CI. Real providers are selected by server configuration.

Provider keys are server-side environment/secret values. They are never returned by API responses, rendered into client bundles, stored in IndexedDB, or accepted in URLs. Notes are untrusted document data and cannot issue tool instructions or authorize mutations.

## Derived schema

All rows are scoped by canonical `library_id` and stable `note_id` and carry the canonical revision used to produce them.

- `intelligence_documents`: note revision, path/title metadata, processing state, schema/model versions, timestamps, and failure summary.
- `intelligence_chunks`: bounded text chunks, heading/source offsets, embedding, and searchable metadata.
- `intelligence_entities` and `intelligence_note_entities`: extracted concepts with evidence spans.
- `intelligence_relations`: explainable derived edges with evidence and model/schema version.
- `intelligence_artifacts`: summaries and other cached structured results, never canonical content.
- `intelligence_jobs`: pending/processing/failed/completed state, attempts, lease, and bounded diagnostic text.

Migrations are forward-only and versioned. Model, prompt, chunker, and embedding changes invalidate only affected derived rows.

## Incremental indexing

Indexing observes canonical revisions and processes one note at a time. Create/update schedules that note; rename/move updates metadata and only re-embeds when content or chunk context requires it; delete removes derived rows; external Git import is reconciled by comparing the backend library/search/link versions plus current note revisions. A periodic bounded reconciliation repairs missed events without full recomputation after each save.

Normal reading and editing never wait for a job. Status is one of `pending`, `processing`, `indexed`, or `failed`; retries use bounded exponential backoff and an operator can retry or rebuild explicitly.

## Feature flags

All intelligence features are independently disabled by default until configured: master AI switch, semantic indexing, semantic search, summaries, link suggestions, health analysis, OCR, daily brief, and provider-backed generation. The client uses server-reported availability rather than assuming a provider exists.

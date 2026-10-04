# Web operations

## Link-opportunity operations

`intelligence_link_suggestions` contains rebuildable source/target hashes, suggestion types, project scope, and evidence payloads. A changed or deleted note invalidates suggestions where it is either source or target. Rebuild Link Opportunities uses the existing confirmed derived-index reconciliation flow and never edits Markdown or Git history.

The service caps library reads and relationship candidates before graph or claim analysis. Database failure degrades to session-only deterministic results. Generation-provider failure has no effect because core discovery does not use generation.

## Enabling meaning search

Meaning search is off by default. Run PostgreSQL 16 with pgvector on a private network and configure these server-only values:

```text
SLATE_INTELLIGENCE_DATABASE_URL=postgres://USER:PASSWORD@HOST:5432/slate_intelligence
SLATE_EMBEDDING_PROVIDER=openai-compatible
SLATE_EMBEDDING_URL=https://approved-provider.example/v1/embeddings
SLATE_EMBEDDING_ALLOWED_ORIGINS=https://approved-provider.example
SLATE_EMBEDDING_API_KEY=server-secret
SLATE_EMBEDDING_MODEL=text-embedding-3-small
SLATE_EMBEDDING_DIMENSIONS=1536
```

The k3s template reads those values from the optional `slate-web-intelligence` Secret. Create and rotate that Secret through the cluster's normal secret-management path; do not add literal credentials to `deploy/k3s/web.yaml`, CI output, or Git.

The provider URL is never accepted from the browser. Loopback HTTP is permitted for a private local model. After configuration, Settings reports Ready, Pending, Indexing, Unavailable, or Failed; indexed versus total notes; chunk and job counts; and per-process embedded, reused, and failed chunk totals. Confirm **Rebuild derived index** to opt into full-library indexing and populate or repair it. Before that confirmation, normal saves may index only the notes being changed; sync and refresh cannot trigger a paid whole-library embedding run. Model or dimension changes require a rebuild. Calls are batched at 24 chunks, hashes reuse unchanged vectors, repeated jobs deduplicate, and failures use capped backoff.

Set `SLATE_EMBEDDING_PROVIDER=disabled` to disable semantic features. Keyword search and deterministic Link Opportunities remain available. To erase derived data, stop the web service and drop the dedicated intelligence database or its `intelligence_chunks`, `intelligence_jobs`, `intelligence_meta`, and `intelligence_link_suggestions` tables; canonical notes are unaffected.

## Enabling Ask Slate

Ask Slate is independently disabled by default. Configure the optional `slate-web-intelligence` Secret or equivalent server environment with:

```dotenv
SLATE_GENERATION_PROVIDER=openai-compatible
SLATE_GENERATION_URL=https://approved-provider.example/v1/chat/completions
SLATE_GENERATION_ALLOWED_ORIGINS=https://approved-provider.example
SLATE_GENERATION_API_KEY=replace-with-secret
SLATE_GENERATION_MODEL=operator-selected-model
SLATE_GENERATION_MAX_INPUT_TOKENS=16000
SLATE_GENERATION_MAX_OUTPUT_TOKENS=1200
SLATE_GENERATION_MAX_CONCURRENCY=2
```

Non-loopback endpoints require HTTPS and an exact origin entry. Redirects, URL credentials, query strings, fragments, browser-provided endpoints, and production use of the fake provider are rejected. Input capability accepts 1,000–200,000 tokens; output is clamped to 128–4,096 tokens; concurrency is clamped to one–four per library. The application also caps the HTTP request at 16 KiB, generated text at 48,000 characters, provider responses at 2 MiB, and a complete Ask request at 65 seconds.

Use `SLATE_GENERATION_PROVIDER=disabled` to turn Ask Slate off without affecting hybrid or keyword search. The deterministic `fake` provider is for development and CI only and makes no paid calls. After changing generation configuration, restart the web server and inspect Settings → Intelligence. It shows enabled state, provider/model identity, request/source counters, active concurrency, and provider-reported tokens without revealing the key.

## Core mode

`slate.lib.web` runs as a Next.js application with a same-origin proxy to one approved Slate backend. Core mode requires only the web application and the existing backend. The web proxy is configured with `SLATE_ALLOWED_SERVERS`, `SLATE_UPSTREAM_URL` when internal routing differs from the public address, and `SLATE_PUBLIC_ORIGIN` for origin validation.

Core health covers application startup and proxy configuration. Backend availability is reported separately in the UI; loss of the optional intelligence layer must not make core Slate unhealthy.

## Intelligence mode

Intelligence mode adds a private PostgreSQL/pgvector service. The self-hosted Next.js server leases and processes bounded durable jobs after web responses; a subsequent authenticated intelligence request resumes pending jobs after a restart. Provider credentials are mounted only into the server process.

Operator actions are provider availability checks, an explicit derived-index rebuild after initial setup or model changes, review of failed-job counts, and optional derived-data deletion. A rebuild reads canonical notes in pages and never writes to the Markdown library.

## Diagnostics

The Settings diagnostics surface reports provider/model identity, queued/processing/failed job counts, indexed note/chunk counts, and last successful indexing time without secrets. It is operational status, not product analytics.

## Feature rollout

Ship intelligence features behind independent server-side flags. Core tests run with all flags disabled. CI uses a deterministic fake provider; real-provider checks are opt-in and never required for a normal build. Rollback consists of disabling flags or rolling back web/worker code; derived schema changes must remain compatible or be rebuilt from canonical Slate data.

## Verification gate

Every milestone must pass `pnpm lint`, `pnpm typecheck`, `pnpm test`, its relevant Playwright projects at desktop and 390/412-pixel mobile widths, and `pnpm build`. Publishing and deployment are separate operator actions and are outside this implementation run.

## Project Brain operations

Project Brain requires no canonical backend migration. Its deterministic workspace operates when generation and semantic search are disabled. Generation uses the existing `SLATE_GENERATION_*` configuration and per-library concurrency limit. Derived synthesis is memory-bounded to 60 cache entries per web process; restarts simply discard it. Canonical note, move, delete, restore, refresh, sync, and bulk events invalidate library project caches through the existing intelligence event path.

For large project folders, expect bounded discovery rather than an eager full-content scan. Operators should monitor canonical request latency and generation counters already exposed in Intelligence settings. A real-provider quality and usage check must be run separately with approved private fixtures before production enablement; normal CI performs no external provider call.
## Evolution operations

Evolution history retrieval is intentionally bounded: 12 candidate notes, 8 history-bearing notes, 8 revisions per note, 80 events, and 20 synthesis sources. These are safety and latency limits, not a complete archive export. The interface marks a result as bounded when a limit is reached.

Generated evolution summaries use the same generation provider and concurrency controls as Ask Slate and Project Brain. Cache identity includes the canonical revision/history fingerprint plus provider and model. Note upsert/delete/rename events invalidate matching note or path entries; sync, refresh, bulk, restore, and rebuild reconciliation clear the relevant library cache. A provider outage should affect synthesis only.

## Knowledge-analysis operations

The intelligence settings include **Rebuild Knowledge Analysis**, with confirmation. Rebuild queues the existing bounded reconciliation process and reconstructs semantic chunks, claims, contradiction candidates, and stale signals from canonical notes; it never edits Markdown or Git history. Normal indexing replaces claims for changed notes and reuses unchanged content hashes.

The Knowledge Issues diagnostics disclose claims indexed, open/resolved/dismissed issues, pending/failed analysis, pairs checked, deterministic findings, model-classified pairs, cache hits, and whether a bound was reached. Database failures degrade to session analysis. Search, editing, sync, Ask Slate, Project Brain, and Evolution continue to use their existing fallback behavior.

Pair processing is capped at 18 neighbors per claim and 72 pairs per changed note. Do not raise these limits without load testing and false-positive review. A rebuild may inspect up to 240 notes in one bounded view pass; refine to a project for larger libraries.

## Overlap-analysis operations

**Rebuild Overlap Analysis** confirms the existing bounded reconciliation path and reconstructs normalized signatures and pair findings only as disposable intelligence state. Normal note indexing replaces the changed note signature; delete and path operations remove relevant overlap rows. A database outage leaves the current session's deterministic comparison usable and never blocks canonical editing, capture, sync, or search.

Diagnostics report exact, near, partial, absorbed, consolidation, open/reviewed, analyzed-pair, deterministic/model, cache, pending/failure, and bound counts. Hard limits are 240 notes, 32 sections per note, 20 neighbors, 72 pairs per note, 320 findings, 8 model classifications, and concurrency 2. Increase them only after load and false-positive testing. Normal CI keeps the external classifier disabled.
## Concept index operations

The concept index is derived and disposable. Opening Concepts can rebuild a bounded session view when PostgreSQL is unavailable. `intelligence_concepts`, `intelligence_concept_memberships`, and `intelligence_concept_relationships` hold rebuildable records when the database is configured. Canonical note writes finish independently; indexing invalidates only memberships and relationships affected by the changed note.

Settings exposes **Rebuild Concept Index** behind confirmation. The existing reconcile job remains the durable library traversal mechanism. A rebuild changes no Markdown, Git history, titles, aliases, or authored links. Diagnostics report indexed concepts, relationships, aliases, merged identities, processed notes, pending and failed work, reuse, work rebuilt in the process, and whether a bound was reached.

## Library Health operations

Library Health is read-only and needs no migration or scheduled scanner. Its endpoint reads bounded canonical pages and existing persisted intelligence rows, caches raw source collection for 30 seconds, and invalidates that cache after canonical note indexing or deletion. Manual Refresh bypasses the short cache; it still does not rerun specialist analyzers.

Diagnostics report source count, aggregated/open counts, priority counts, health-only findings, aggregation duration, failed source queries, refresh time, and whether the item bound was reached. If PostgreSQL or one canonical auxiliary endpoint is unavailable, the UI labels partial results and continues with every successful source. Operators should investigate persistent indexing failures through Intelligence settings rather than dismissing the underlying health item.

## Knowledge Gap Finder operations

Knowledge Gaps uses a 60-second library-scoped session cache and persists rebuildable findings when PostgreSQL is configured. Note indexing and deletion invalidate findings tied to that canonical note through the existing intelligence event path. **Rebuild Knowledge Gaps** requires confirmation, rebuilds derived data only, and leaves editing, search, sync, and canonical history available.

The deterministic workspace remains functional when generation or semantic retrieval is disabled. Without PostgreSQL it labels the bounded session fallback and does not block canonical operations. Monitor pending/failed job counts, bounds reached, and persisted availability in diagnostics. Do not raise the documented note, concept, project, relationship, question, finding, page, or concurrency caps without load and false-positive testing.
## Inbox triage operations

Inbox diagnostics expose item, analyzed, pending, and failed counts; strong project suggestions; overlap matches; question suggestions; append opportunities; deterministic/model classification counts; cache hits; and bound status. The normal state is deterministic analysis with optional semantic-only matches absent. If PostgreSQL is unavailable, triage continues from bounded session analysis and reports that persistence is unavailable.

Review state (`Unprocessed`, `Processed`, `Deferred`) and suggestion decisions (`Accepted`, `Dismissed`, `Not Relevant`) are browser-local and fingerprinted by capture content/evidence. A material capture edit invalidates the prior state. Semantic enrichment is capped at eight captures, eight neighbors, and concurrency two; failures fall back to deterministic evidence. Derived analyses may be rebuilt safely because canonical Markdown contains no triage metadata.

Append is a two-stage operation: review/edit the proposed insertion, create a local target draft with the known revision, then use the normal editor Save. A newer target revision surfaces the editor recovery comparison. The capture remains until the user explicitly marks it processed or invokes the existing deletion flow.

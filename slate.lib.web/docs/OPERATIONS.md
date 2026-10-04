# Web operations

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

Set `SLATE_EMBEDDING_PROVIDER=disabled` to disable semantic features. Keyword search remains available. To erase derived data, stop the web service and drop the dedicated intelligence database or its `intelligence_chunks`, `intelligence_jobs`, and `intelligence_meta` tables; canonical notes are unaffected.

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

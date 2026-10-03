# Web operations

## Core mode

`slate.lib.web` runs as a Next.js application with a same-origin proxy to one approved Slate backend. Core mode requires only the web application and the existing backend. The web proxy is configured with `SLATE_ALLOWED_SERVERS`, `SLATE_UPSTREAM_URL` when internal routing differs from the public address, and `SLATE_PUBLIC_ORIGIN` for origin validation.

Core health covers application startup and proxy configuration. Backend availability is reported separately in the UI; loss of the optional intelligence layer must not make core Slate unhealthy.

## Intelligence mode

Later intelligence milestones add a private PostgreSQL/pgvector service and a separately runnable worker using the same versioned application code. Web requests enqueue bounded jobs; workers lease and process them. Run at least one worker only when intelligence is enabled. Provider credentials are mounted only into server/worker processes.

Required operator actions will include database migration, provider availability check, derived index rebuild, retry failed jobs, and delete derived data. A rebuild reads canonical notes in pages and is resumable. It never writes to the Markdown library.

## Diagnostics

The private diagnostics surface reports canonical backend availability, derived schema/model version, queued/processing/failed job counts, provider availability without secrets, request counts, derived storage size, and last successful indexing/reconciliation time. It is operational status, not product analytics.

## Feature rollout

Ship intelligence features behind independent server-side flags. Core tests run with all flags disabled. CI uses a deterministic fake provider; real-provider checks are opt-in and never required for a normal build. Rollback consists of disabling flags or rolling back web/worker code; derived schema changes must remain compatible or be rebuilt from canonical Slate data.

## Verification gate

Every milestone must pass `pnpm lint`, `pnpm typecheck`, `pnpm test`, its relevant Playwright projects at desktop and 390/412-pixel mobile widths, and `pnpm build`. Publishing and deployment are separate operator actions and are outside this implementation run.

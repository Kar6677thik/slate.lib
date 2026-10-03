# Slate web architecture plan

## Product boundary

The browser is a client of the existing ASP.NET Core Slate service. It does not own a second library and it does not translate Markdown into another canonical document model. Every canonical read and mutation travels through the authenticated same-origin proxy to the existing API. Stable note UUIDs and optimistic revisions remain the concurrency boundary.

The application is organized into four layers:

1. **Product surfaces** in `src/components` and `src/features`: library, editor/reader, search, knowledge views, history, graph, links, capture, and settings.
2. **Typed Slate adapter** in `src/lib/api`: browser contracts and one request client for existing backend endpoints.
3. **Private browser state** in `src/lib/storage`: drafts, recent activity, layout/editor preferences, favorites, pins, saved searches, and later deliberately downloaded offline material.
4. **Server boundary** in `src/app/api/slate`: an origin-checked, server-allowlisted, path-allowlisted proxy that forwards only required headers and bounded bodies.

TanStack Query owns remote cache/revalidation. IndexedDB owns durable drafts and later acknowledged offline operations. React state owns transient modal, pane, selection, and command state. URL parameters preserve current navigation and note identity without including credentials.

## Canonical mutation rule

Create, update, rename, move, copy, delete, bulk apply, answer, append, restore, repair, and sync operations use the backend implementation. Reviewed server previews and source fingerprints are preserved where the API supplies them. Client retries never repeat a structural mutation after an unknown acknowledgement; status/receipt endpoints resolve that ambiguity.

## Optional intelligence extension

When Milestone 3 starts, Next.js server routes expose a separate authenticated intelligence API backed by PostgreSQL/pgvector and a worker. The worker reads canonical pages/revisions through a server-side Slate client, writes only rebuildable derived rows, and never mounts or edits the library filesystem. Provider keys are available only to server and worker processes. The browser receives feature availability, job status, bounded results, citations, and proposals.

The web application must remain fully functional if the intelligence database, worker, or provider is absent. See `INTELLIGENCE.md` for schema and indexing decisions.

## Missing-capability decision

No backend addition is required for Milestone 1. Existing endpoints cover all requested parity behavior. New server work begins only for derived intelligence APIs, background job control, hybrid semantic retrieval, and grounded provider execution. Those APIs belong to the web deployment and cannot redefine canonical Slate content.

## Performance constraints

- All library/search/health/recovery views remain paged.
- Graph requests are bounded to 40 client nodes by default and never fetch the full library.
- Note bodies load only when opened; quick navigation searches the server rather than preloading titles.
- Expensive editor/reader modules remain dynamically imported.
- Queries use cancellation signals and input debouncing.
- Background intelligence work is incremental, leased, retryable, and off the UI request path.

## Responsive interaction model

Desktop uses resizable library, document, and details panes. Mobile uses the same capabilities through full-width dialogs/sheets and bottom navigation rather than shrinking the desktop chrome. All actions require a keyboard/touch equivalent, and primary acceptance widths are 390×844 and 412×915 with no horizontal overflow.

## Verification and release boundary

Each milestone is complete only after lint, strict type checking, unit tests, relevant Playwright desktop/mobile tests, and a production build. CI may package a result later, but this implementation run does not push, tag, publish, or deploy.

# Slate web architecture plan

## Milestone 3 search flow

Canonical data remains owned by `slate.lib`: Markdown files, Git history, note identity, and Lucene are unchanged. The browser calls the same-origin intelligence route, which obtains keyword candidates from the approved Slate upstream. When enabled, the server embeds only cleaned free-text queries and asks PostgreSQL/pgvector for filtered chunk candidates. Reciprocal-rank fusion combines both lists with deterministic exact-title priority. Provider, database, and indexing failures return keyword results with a visible degraded state.

PostgreSQL 16 with pgvector is the selected derived store. SQLite has a smaller footprint but no dependable cross-platform indexed vector path for the 100,000-chunk target. Qdrant is capable but adds a specialized service while PostgreSQL also provides metadata filtering and durable jobs. Browser vector storage would expose derived vectors and cannot safely hold provider credentials.

The `intelligence_chunks` and `intelligence_jobs` tables are disposable and rebuildable. The HNSW index supports bounded candidate retrieval; provider, model, and dimensions form an index signature so incompatible vectors cannot mix. Canonical mutations complete before a best-effort indexing event. Events are idempotent by key, embeddings are reused by content hash and sent in bounded batches, and note replacement is transactional. Reconcile work runs after the response with capped retry backoff.

## Product boundary

The browser is a client of the existing ASP.NET Core Slate service. It does not own a second library and it does not translate Markdown into another canonical document model. Every canonical read and mutation travels through the authenticated same-origin proxy to the existing API. Stable note UUIDs and optimistic revisions remain the concurrency boundary.

The application is organized into four layers:

1. **Product surfaces** in `src/components` and `src/features`: library, editor/reader, search, knowledge views, history, graph, links, capture, and settings.
2. **Typed Slate adapter** in `src/lib/api`: browser contracts and one request client for existing backend endpoints.
3. **Private browser state** in `src/lib/storage`: drafts, recent activity, layout/editor preferences, favorites, pins, saved searches, and later deliberately downloaded offline material.
4. **Server boundary** in `src/app/api/slate`: an origin-checked, server-allowlisted, path-allowlisted proxy that forwards only required headers and bounded bodies.

TanStack Query owns remote cache/revalidation. IndexedDB owns durable drafts and later acknowledged offline operations. React state owns transient modal, pane, selection, and command state. URL parameters preserve current navigation and note identity without including credentials.

## Universal Command Center

Milestone 2 adds a declarative registry in `src/features/commands`. A command describes its identity, copy, icon, keywords, group, optional shortcut and danger/context metadata, an availability predicate, and an execution handler. Registry filtering and ranking are pure and independently tested. Components that own local behavior, such as editor modes, the details rail, the library explorer, and responsive panes, register small handlers with `CommandRuntimeProvider`; commands invoke those existing handlers instead of duplicating UI state or mutations.

Static commands and dynamic result sources share one result model. Local providers return open tabs, favorites, recent notes, saved searches, and pinned folders synchronously. Remote note enrichment reuses the canonical paged search API with debounce and cancellation and starts only after the user enters normal search text. Command-only mode never starts note search. Command history is a library-scoped local list capped at 25 items and only adds a bounded ranking bonus; it is not analytics or server data.

The Command Center keeps focus in an ARIA combobox/listbox interaction, supports Arrow keys, Home, End, Enter, and Escape, and restores the prior focus target. The same component is a centered desktop palette and a large bottom sheet on mobile. Destructive commands only open the existing guarded operation flow.

## Canonical mutation rule

Create, update, rename, move, copy, delete, bulk apply, answer, append, restore, repair, and sync operations use the backend implementation. Reviewed server previews and source fingerprints are preserved where the API supplies them. Client retries never repeat a structural mutation after an unknown acknowledgement; status/receipt endpoints resolve that ambiguity.

## Optional intelligence extension

Milestone 3 exposes a separate authenticated intelligence API through Next.js server routes backed by PostgreSQL/pgvector and a bounded in-process job drain. It reads canonical pages/revisions through a server-side Slate client, writes only rebuildable derived rows, and never mounts or edits the library filesystem. Provider keys are available only to the server process. The browser receives feature availability, job status, bounded results, citations, and diagnostics.

The web application must remain fully functional if the intelligence database, worker, or provider is absent. See `INTELLIGENCE.md` for schema and indexing decisions.

## Ask Slate generation flow

Milestone 4 adds a vendor-neutral `GenerationProvider` beside the embedding provider. Disabled, deterministic test, and allowlisted OpenAI-compatible implementations expose availability, model identity, input capability, full generation, and streaming generation. Credentials and provider URLs remain server-only. The browser can choose scope and strict/general policy but cannot choose a provider URL or key.

An authenticated Ask request resolves its canonical library, validates scope, and retrieves from the existing lexical and semantic systems. Deterministic question classification adds rationale, comparison, temporal, and unanswered-question signals without an LLM planner. Candidates are deduplicated, ranked, diversified across notes, and capped before the source pack is assembled. Each source receives a stable request-local ID such as `S1`; generated citation IDs are parsed and checked against that exact pack before reaching the UI.

Prompt construction separates system rules, bounded prior conversation, the user question, and strongly delimited untrusted documents. The generation layer is read-only and has no tools. Per-library in-process concurrency, client cancellation, a 65-second request deadline, provider response limits, and configured input/output token ceilings bound provider work. Conversation display state lives only in browser session storage; every follow-up repeats retrieval.

The desktop and mobile clients share one dedicated research dialog. Desktop uses a bounded workspace-sized modal and mobile uses the full viewport. Both expose scope and policy controls, streamed answer state, validated inline citations, evidence cards, retry/stop/copy actions, and source navigation without permanently occupying a workspace pane.

## Project Brain pipeline

Project Brain is a first-class workspace destination keyed by a normalized folder path. The browser retains that project path while source notes open in normal tabs, so closing a note or using browser history returns to the same brain instead of creating duplicate project surfaces. Optional project roots live in versioned, library-scoped browser preferences.

The authenticated intelligence route enumerates folder metadata in pages with hard folder/entry bounds, searches canonical metadata for explicit types/statuses, and fetches bodies and history for a bounded candidate set. Pure extractors classify explicit conventions before heading-based possible decisions/risks, build a bounded chronological timeline, and derive a project-only explicit-link graph with an accessible list. Outside related notes are fetched separately and never enter the synthesis source pack.

Generated overview, recent-work summary, and Resume Project reuse the Milestone 4 generation provider, concurrency guard, source shape, citation validation, and untrusted-document delimiters. They load independently after the deterministic snapshot. Project Brain does not expose mutation tools or automatically create summary/status notes.

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
## Evolution pipeline

`src/lib/intelligence/evolution-service.ts` collects a bounded candidate set from canonical note APIs, lexical search, and the existing semantic candidate service. It reads at most 12 candidate notes, history for at most 8 notes, and 8 committed versions per note. Historical snapshots are requested on demand; Slate does not index every Git revision.

`src/lib/intelligence/evolution.ts` normalizes Markdown and frontmatter, removes cosmetic-only revisions, compares changed sections, assigns evidence strength, and emits typed `EvolutionEvent` records with before/after sources. The current view is built separately from the latest canonical `Note` objects. The intelligence route may pass those deterministic events to the configured generation provider, but generated text cannot add events or sources.

Generated results use an in-process bounded cache keyed by library, normalized scope, revision/history fingerprint, provider, model, schema, and feature. Canonical mutation events invalidate entries that contain the affected note or path; reconcile events clear the library’s evolution cache.

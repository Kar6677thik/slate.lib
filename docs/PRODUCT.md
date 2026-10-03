# slate.lib — Product specification

## Evolution implementation — 2026-10-02

Milestone A adds multi-selection, reviewed bulk move/copy/duplicate/delete, Windows internal drag/drop, a searchable command registry, and shared Markdown editing commands. Android supports long-press selection, selection actions, and a compact editor toolbar. Existing note-ID tabs and dirty-draft protection remain. Favorites/pins and capture workflows are implemented in B/C below.


Status: evolution milestones A–M implemented; validation and remaining device qualification are recorded in ROADMAP, 2026-10-03.

This document owns product scope. [ARCHITECTURE](ARCHITECTURE.md) owns components and deployment; [CONTENT_AND_SYNC](CONTENT_AND_SYNC.md) owns data and consistency rules; [UX](UX.md) owns interactions; [ROADMAP](ROADMAP.md) owns implementation order. Change these together when a decision changes. “MVP” means the end of roadmap phase 3D; phases 1–2 establish the Windows read/write explorer, Phase 3A adds search/Git/history, and Phase 3B adds Android/capture/durable client state.

## Vision and user

slate.lib is a private knowledge library for one person, built and operated by that person. It should become a comfortable place to accumulate years of learning, questions, ideas, references, project context, decisions, experiments, images, and failures. Its essential loop is **open → find or create → read or write → save → find again**.

The library may grow to tens of thousands of Markdown notes and many assets. Clients browse and request this content from a home server; installing the application never installs the library. Projects are ordinary folders and notes, not a project-management system.

## Principles

- Capture before organization. Inbox accepts incomplete thoughts without requiring tags or a destination.
- Markdown files and real directories are the source of truth. Files remain understandable in a text editor without slate.lib.
- Preserve work. Never silently overwrite another device's or external editor's changes; retain drafts through errors.
- Search and reading come first. Graphs, AI, and visual novelty must not displace the basic loop.
- Keep Git in the background. Saving, indexing, and off-site synchronization have different meanings.
- Make Windows and Android feel appropriate to their platforms while sharing content rules.
- Keep one deployable backend and a small set of dependencies. Every component must earn its maintenance cost.
- Be honest about limits: offline cache coverage, stale results, failed backups, and unsynchronized content are visible.

## Core workflows

1. **Capture a question:** Android → Question or Quick Thought → type “Why does PostgreSQL need VACUUM?” → Save to Inbox. Later add an answer, related links, and move the same note to `computer-science/databases/postgresql/`.
2. **Learn and retrieve:** Windows → quick open or search → read a note → edit Markdown → save → search a phrase from the new content. Search must locate saved content promptly without waiting for GitHub.
3. **Organize:** browse actual folders → create, rename, move, or delete a note/folder. Confirm destructive scope. Existing note identity survives movement.
4. **Keep project context:** create `projects/anythingtcg/` containing `overview.md`, `architecture.md`, and `decisions/`, `experiments/`, `failures/`, `ideas/`, `questions/`. Use ordinary links and optional metadata.
5. **Capture a reference:** Android Share → slate.lib → review shared text, URL, or image → save to Inbox. Upload image bytes before saving a link to them.
6. **Edit elsewhere:** clone the private knowledge repository in VS Code, edit and push normally. The home server imports validated changes; open clients refresh without losing unsaved edits.
7. **Recover work:** a stale save preserves the draft and opens base/local/current review. Restore writes new commits. Divergent Git histories can be combined after review only when changed paths are disjoint; overlapping changes require operator resolution.

## Implemented feature catalogue

| Area | Current implementation |
| --- | --- |
| Explorer | Reviewed multi-select move/copy/duplicate/delete; internal Windows drag/drop; Android long-press selection; stable IDs and recoverable operation journals |
| Writing | Source/read/split modes, drafts, Markdown insertion commands, tabs, searchable command palette |
| Reading | Safe local highlighting/Mermaid/math, nested outline, heading navigation, collapsible callouts |
| Capture | Questions and answers, daily notes, Add Link, templates, reviewed append, bounded Android multi-file Share |
| Search and views | Type/status/date/has filters, exact-title ranking, paged smart views and saved searches |
| Navigation | UUID Favorites, path-based pins with missing-folder recovery; device-local preferences |
| Links | Wiki and Markdown backlinks, reviewed incoming repairs and wiki export, broken/ambiguous link management |
| Assets | Paged catalog, reference lookup, thumbnails, isolated PDF extraction and optional local OCR; explicit unused-asset cleanup |
| Offline | Selected note/folder/pin downloads, quota, plain-text downloaded-content search, durable note/upload queue and receipts |
| Conflicts | Base/local/current text workbench; reviewed Git merge only for disjoint changed paths |
| History | Bounded version comparison, restore as a new revision or new note, deleted-note recovery |
| Rediscovery | Explained age/status/date views and random notes; optional private local reading history |
| Graph | Bounded current-note neighborhood with depth/type/folder controls; eight deterministic explained related notes |

Structural operations remain online-only. Offline search covers deliberately downloaded content and uses plain terms, not the server's filter grammar. Asset cleanup considers the current library, not historical Git revisions or other devices' pending work; the review warns about this explicitly. No automatic cleanup runs. Cross-device preference sync, AI/embeddings, a web client, full-library replication and overlapping-file Git merges remain outside this implementation.
## MVP acceptance

The first usable release must let the owner perform the essential loop on both an installed Windows app and an installed Android app against the same Linux home server.

- Create a thought with no metadata decisions; save it, restart both client and server, and find/read it again.
- Browse and perform the basic file/folder operations in the catalogue; moving a note retains its ID and app-generated internal links.
- Save from either device while GitHub is unreachable. “Saved” remains true; “Sync pending” remains visible until push succeeds.
- Edit externally and see a valid fast-forward import after polling/indexing. Divergence preserves both histories and gives an actionable ordinary-Git recovery path.
- Attach an image and retrieve notes using words, tags and paths. Question metadata can be edited directly in Markdown and queried without a separate entity or workflow.
- Read previously cached content without the server; preserve any unsent draft without claiming a server save.
- Rebuild all search/link metadata from the library and recover notes/assets from a tested backup.

A beautiful MVP means legible typography, predictable navigation, responsive lists, useful errors, and reliable saves. It does not require every future editing or discovery feature.

## Platform experience

**Windows:** a proper desktop window with native navigation and editor controls. A collapsible tree, Inbox and Recent sit beside a folder listing or document; smart views are available. Search is always reachable; keyboard focus, selection, context menus, breadcrumbs, quick open, and back/forward behave consistently. Large folders are virtualized and loaded in pages. Dark/light themes, scaling, and resizable panes must remain readable.

**Android:** an installed mobile app with Library, Search, Inbox, and a prominent Capture action. Reading occupies the screen; edit/preview switch rather than compete for space. Folder browsing uses drill-down and breadcrumbs. Share integration, quick capture, keyboard-safe controls, large touch targets, and draft recovery are first-class. Lifecycle interruptions must not erase text.

The native shell can host a restricted document-rendering surface. The application is not a website packaged as a desktop/mobile app.

## UX expectations

- Opening a note must not download the entire library or unrelated assets. Load visible assets lazily.
- Provide progress and cancellation for long operations; keep typing and navigation responsive.
- An ambiguous wiki link asks for a target; a missing target never silently points to an unrelated note.
- Search results distinguish title, path, and excerpt, including duplicate titles in different folders.
- Save failures preserve the draft and offer a concrete next step. Do not expose Git commands during normal writing.
- Empty states give one useful action, such as “Capture a thought” or “Create a note here”.
- Meet practical keyboard, screen-reader, contrast, font-scaling, and reduced-motion expectations. Color alone never indicates save/conflict state.

## Explicitly outside scope

No organizations, teams, tenants, invitations, RBAC matrix, collaboration, presence, realtime co-editing, CRDTs, billing, business workflows, advertising, analytics platform, plugin marketplace, or project-management subsystem. No microservices, message broker, distributed cache, or mandatory external database.

No AI, embeddings, semantic search, graph visualization, recommendations, advanced learning analytics, whole-library downloads, full conflict merge UI, automatic divergent Git merging, or automatic offline replay in the MVP. No public sharing or public Internet API exposure in the initial deployment. No Electron, Blazor application shell, or Angular wrapper for the native clients.

## Future direction

Rediscovery uses understandable queries before recommendations: random eligible note; an old idea/question; notes created on this month/day; recently modified learning notes; notes marked `learning` or `needs-review`. It must show why a note appeared. Reading-history-based resurfacing requires an explicitly introduced, private activity store; it is not silently collected in the MVP.

AI may later consume the same retrieval and document APIs to summarize learning, recover project context, identify unanswered questions, compare historical thinking, and suggest gaps. Model providers, vector storage, prompts, and automation are deliberately undecided until there is a concrete feature and a privacy decision.

Milestone B adds UUID Favorites and path-based folder pins on both clients, with explicit missing-pin recovery. These are local preferences; canonical Markdown and Git are unaffected.

Milestone C: both clients expose Question, Answer and Add Link; new daily folders use daily/YYYY-MM-DD.md from the device-local date. Templates are ordinary notes in templates/. Captures can be reviewed and appended to an existing note without creating another note. Android accepts bounded multi-file Share batches and retains an interrupted handoff.

Milestone D implemented (2026-10-02): Favorites, Unanswered Questions, Recently Modified, Orphan Notes, Notes With Diagrams, Notes With Code, Currently Learning, Needs Review, and locally saved named searches are available from both clients. Views use paged derived queries; saved queries can be renamed/deleted and run through normal search.

Current scope: milestones A–M are implemented. Dated phase/milestone records below are historical checkpoints, not statements that later completed features are still deferred. ROADMAP contains current verification and device limitations.


Milestone E implemented (2026-10-02): lexical search combines type/status, created/modified/date ranges, content/link predicates, tags, paths and text. Exact-title and leading-title-prefix signals supplement existing title/alias/heading/body ranking. Both clients expose compact filters and paged results. Offline-content search is implemented with milestone I, not claimed here.


Milestone F implemented (2026-10-02): reviewed incoming-link repair accompanies native move/rename flows. Both clients expose missing/ambiguous link checks and reviewed wiki-to-relative-Markdown conversion. UUID/title links that survive a move are left alone; uncertain links are never auto-repaired.


Milestone G implemented (2026-10-02): nested reader outline, current-section indication, previous/next heading navigation, copy heading link, and explicit expandable callouts are available in both clients. Reader position/disclosure state is retained in the WebView session where browser storage is available.


Milestone H — attachment management (2026-10-02)

The Windows command palette and Android Commands expose Attachments: paged metadata, current reference counts, referencing-note navigation, lazy image thumbnails, explicit PDF text extraction and local image OCR. Extracted text is visibly derived and does not modify binaries or Markdown. Unreferenced attachment removal requires a preview, a 24-hour age grace period, a fresh full canonical-reference audit, and confirmation. History and other devices’ pending drafts may still refer to unused binaries; the warning is explicit.

Milestone I — deliberate offline availability (2026-10-02)

Offline work is available from the Windows palette and Android Commands/note actions. Mark a note, folder, or pinned folder, then read/search downloaded copies and their attachments without a server. The manager shows download progress, timestamps, failures, storage/quota and pending saves. Create/edit/capture saves use durable operations with attachment dependencies, receipts, attempts and errors. Structural mutations remain online-only.

Milestone J — conflict handling (2026-10-02)

Windows and Android now show Base, Local, Current and Proposed versions for stale note saves. Non-overlapping line changes combine deterministically; overlapping blocks require explicit side selection or reviewed manual editing. Saving revalidates the fetched current revision. Save local as separate note preserves both notes. Independent divergent histories can be explicitly previewed/combined only when they changed different files; same-file changes and rewritten histories require the recovery runbook.

Milestone K — history and recovery (2026-10-02)

History supports current-versus-historical and historical-versus-historical comparisons, version metadata, restore as a new current revision, and restore as a new note. Deleted-note recovery offers a bounded list from existing Git history, previews content and asks for a destination. Restoration preserves history and uses normal new commits; existing destinations never silently overwrite.

Milestone L — rediscovery (2026-10-02)
Both clients expose Rediscover with random notes, older ideas/open questions, Something Forgotten, On This Day, explicit learning states, and a dated timeline. Results explain their eligibility. Unknown dates are omitted from date-driven views; templates are excluded. Date-based ordering is deterministic; Random Note is deliberately random. Results are paged in groups of 20 from cached parsed metadata.
Reading history is opt-in and device-local: UUID plus last-opened timestamp, at most 1,000 entries retained for 180 days, never uploaded. Privacy controls enable/disable and clear it. Forgotten results exclude recently opened notes when tracking is enabled; when disabled, only explicit note age is used. No reading activity before opt-in is implied. No canonical metadata migration or production changes.

Milestone M — graph and related notes (2026-10-02)
Windows commands and Android note actions expose a current-note graph and up to eight related notes. Graph traversal is bidirectional over resolved wiki/Markdown links, with directed edges displayed, depths 1–3, folder/type filters, 40 client nodes (60 API maximum) and 240 edges. Limits are visible. Selecting a plotted node or its accessible numbered row opens it. Filters restrict traversal; the current note remains visible.
Related scores are deterministic: direct link +12; shared incoming source +4 each (maximum 20); shared tag +3 each (maximum 15); title/heading term +1 each (maximum 4); same non-root folder +1. Every score has an explanation. Ties sort by path. Templates are excluded from suggestions. All data is rebuilt from the existing link index and cached parsed metadata; there are no embeddings, AI calls, graph database or canonical-data migrations.
Full solution Windows/Android build: zero warnings/errors. All 181 tests pass, including graph edge direction, incoming traversal, filters, limits, invalidation, score explanations and deterministic ordering. L was verified with 179 passing tests before M. Native and physical-device acceptance remains separately reported.

# Slate web capability matrix

## Hybrid search and semantic retrieval

Search offers three explicit modes. **All** fuses the existing Lucene results with semantic chunk matches. **Keyword** uses the canonical Slate Lucene endpoint unchanged. **Meaning** ranks semantically related chunks and falls back to Keyword when intelligence is unavailable.

The existing `path:`, `tag:`, `type:`, and `status:` syntax applies directly to semantic candidates. Canonical-only `created:`, `modified:`, `date:`, and `has:` filters are still evaluated by Lucene, so All and Meaning deliberately fall back to filtered keyword results for those queries. Meaning results identify their source heading and show a quiet “Meaning match” label; they never imitate keyword highlighting. The Command Center uses All mode for free text while its `>` command mode remains local and immediate.

Meaning search is optional and disabled until the server operator configures it. Notes, Git history, saves, links, assets, and Lucene search continue to work without it.

## Ask Slate

Ask Slate is a read-only research workspace over the existing hybrid retrieval layer. It supports explicit scopes for the entire library, current note, current folder, deterministically identified top-level project folder, selected open notes, and selected editor/reader text. The active scope is always visible. It is available from the top bar, activity rail, editor selection action, and intentional Command Center commands; ordinary Command Center text never invokes generation.

Strict Library is the default policy. It answers only from retrieved Slate excerpts and returns an insufficient-evidence response when the source pack is weak or empty. General + Library renders separate **From your library** and **General context** sections. Answers stream into a source-oriented research view with validated citation links, excerpts, note paths and headings, Open note, Open in tab, Copy link, Stop, Retry, Copy answer, New conversation, and Clear controls.

Short conversations stay in this browser's session storage and are capped at twelve displayed messages. Every follow-up performs retrieval again with the current scope and at most six bounded prior turns. Ask Slate never edits notes, executes document instructions, triggers sync, or saves transcripts into Markdown. If generation is disabled, the rest of Slate and hybrid search continue normally and the Ask surface explains that it is unavailable.

This document records the web product boundary as inspected on 2026-10-03. The ASP.NET Core service remains the canonical owner of Markdown, note and folder identities, assets, Git history, lexical search, links, and every content mutation.

## Milestone 1 parity matrix

| Feature | Backend support | Web support before this milestone | Work needed |
| --- | --- | --- | --- |
| Library browsing and note editing | Complete: paged folders, reads, optimistic updates, create, rename, move, copy, delete | Complete | Preserve and regression test |
| Quick Thought | Complete: `POST /v1/captures` | Complete | Preserve |
| Advanced lexical search | Complete: Lucene query syntax with `type:`, `status:`, `tag:`, `path:`, `created:`, `modified:`, and `has:` filters | Direct query entry and paging exist; filter affordances and saved searches do not | Add a compact filter builder, query explanation, and saved searches without hiding direct syntax |
| Smart views | Complete: seven paged views at `GET /v1/views/{view}` | Missing | Add views for unanswered, modified, orphans, diagrams, code, learning, and review |
| Multi-select and bulk operations | Complete: fingerprinted preview/apply/status at `/v1/library/bulk/*` | Missing | Add selection state, preview, collision/repair review, apply receipt, and keyboard/mobile affordances |
| Favorites and pinned folders | Canonical backend intentionally does not own these; native clients store library-scoped local preferences | Missing | Store versioned, library-scoped browser preferences; never write these into Markdown |
| Saved searches | Canonical backend intentionally does not own these; native clients store library-scoped local preferences | Missing | Store versioned browser-local named queries and surface them in navigation/search |
| Question and answer | Complete: typed question notes use normal create; answers use revision-checked `POST /v1/notes/{id}/answer` | Missing | Add Question creation and explicit Answer action with previewable user input |
| Daily Note | Complete: serialized `POST /v1/workflows/daily` | Missing | Add open/create-today action with idempotent server behavior |
| Link diagnostics and repair | Complete: paged issues plus revision-checked preview/apply | Links/backlinks only | Add health list, candidate choice, textual preview, and explicit apply |
| History comparison | Complete: bounded history and historical source | Read-only single snapshot | Add current-versus-history and history-versus-history text comparison |
| Restore and restore as new | Complete: revision-aware `POST /v1/notes/{id}/restore` | Missing | Add explicit current/as-new workflows and collision-safe destination input |
| Deleted-note recovery | Complete: bounded Git recovery list plus restore endpoint | Missing | Add recovery list, historical preview, and restore-as-new/original actions |
| Rediscovery | Complete: paged deterministic views with explanations | Missing | Add all existing views and keep the backend-provided reason visible |
| Current-note graph | Complete: bounded traversal, depth 1–3, folder/type filters | Missing | Add a bounded accessible graph/list and filters; never request the whole library |
| Related notes | Complete: deterministic ranked results with reasons | Missing | Add current-note panel with reasons and direct navigation |
| Assets | Upload/download/metadata plus paged inventory, references, thumbnails, extracted text, explicit extraction, and guarded cleanup | Upload/download/basic metadata | Add asset workspace and explicit extraction/cleanup flows in a later Milestone 1 slice |
| Wiki export | Complete: preview/apply | Missing | Add reviewed conversion action in the link tools slice |
| Safe sync merge | Complete: preview/apply | Basic sync only | Add conflict-specific reviewed flow after bulk/history parity |
| Offline replay | Complete for safe note operations | IndexedDB drafts only | Defer full offline product work to Milestone 26; do not imply acknowledgement before server receipt |

## Existing API inventory used by the web

- Core: status, library pages/item details, note reads and optimistic writes, create/capture/folders, rename/move/copy/delete, search, sync, refresh, Git flush, assets, links, and history.
- Parity additions: smart views, bulk preview/apply/status, link issues/repair/wiki export, daily/question/answer/append, graph/related/rediscovery, history restore/deleted recovery, asset inventory/references/derivatives/cleanup, safe sync merge, and offline replay.
- The same-origin Next.js proxy remains the only browser-to-Slate path. Its route allowlist must enumerate every exposed API shape and must continue forwarding bearer tokens in headers only.

## Server capabilities that are actually missing

Milestone 1 requires no new backend endpoint. Later milestones need web-only derived capabilities that the canonical server deliberately does not provide: embeddings, semantic retrieval, generated summaries, entity/concept extraction, duplicate and possible-conflict candidates, intelligence job state, and grounded generation. Those belong to the optional intelligence layer described in `INTELLIGENCE.md`, not to canonical Markdown storage.

## Delivery order

1. Milestone 1A: typed API/proxy parity and deterministic local preferences. **Implemented.**
2. Milestone 1B: smart views, advanced search, favorites, pins, saved searches, Daily Note, Question, and Answer. **Implemented.**
3. Milestone 1C: selection and reviewed bulk operations. **Implemented with server-generated preview, optional incoming-link repair review, and explicit apply.**
4. Milestone 1D: link health/repair and wiki export. **Implemented with source comparison and revision-checked apply.**
5. Milestone 1E: history comparison, restore, and deleted recovery. **Implemented.**
6. Milestone 1F: rediscovery, related notes, graph, and advanced asset tools. **The requested Milestone 1 rediscovery, related-note, and bounded current-note graph parity is implemented. Advanced asset tooling remains a later product slice.**
7. Milestone 2: command registry and shared desktop/mobile command surfaces. **Implemented.** The Universal Command Center blends commands, open tabs, favorites, recent notes, saved searches, pinned folders, and cancellable server note search. `>` and `Ctrl/Cmd+Shift+P` enter command-only mode; availability follows the current note, dirty state, registered editor tools, and details panels.
8. Milestone 3: derived store, background indexing, and hybrid search. **Implemented with optional PostgreSQL/pgvector storage, server-only providers, deterministic chunking, durable incremental jobs, RRF, diagnostics, and lexical fallback.**
9. Milestones 4–10: grounded assistance, project/context views, evolution, conflicts, duplicates, linking, and virtual concept pages.
10. Milestones 11–18: graph expansion, health, triage, rediscovery expansion, briefs, learning, gaps, and failure memory.
11. Milestones 19–27: advanced reader, capture/clipper, attachment intelligence, voice/writing proposals, workspaces/collections, offline expansion, and final mobile polish.

Each numbered milestone is gated by lint, strict type checking, unit tests, relevant Playwright coverage, and a production build.

## Project Brain and Resume Project

Milestone 5 turns any selected folder subtree into a read-only Project Brain. A folder action, project-root shortcut, or contextual Command Center action opens the dedicated workspace. Project membership is path-based; an explicitly marked project root is a browser-local navigation preference and does not alter Markdown. Related notes outside the subtree appear only under **Related from Elsewhere** and are excluded from generated project claims.

The workspace renders deterministic evidence before generation: overview/current documents, recent canonical history, decisions and ADRs, open question notes, architecture sources, ideas, experiments, failures, blockers, a bounded timeline, explicit-link graph list, and important sources. Empty evidence sections are omitted. Generated Overview, Recent Changes summary, and Resume Project are optional, separately loaded, citation-validated views. Resume Project covers the current state, recent work, decisions, unresolved questions, known problems, high-value reading, and possible context to review next without creating tasks or changing notes.

## Milestone 2 command surface

The web client now exposes a declarative command registry grouped by Create, Navigation, Document, Workspace, Library, Appearance, and Settings. Dynamic providers add open tabs, local favorites, recent notes, pinned folders, saved searches, and remote note results without scanning or preloading the library.

Current command families include note/folder/question/capture/daily creation; primary and smart-view navigation; save/read/write/split/favorite/file operations; backlinks, outgoing links, related notes, graph, history, document info, and wiki export; tab and pane controls; refresh/sync/recovery/rediscovery/link diagnostics; theme selection; and settings. Commands that do not have a real implementation or valid current context are omitted.

Desktop shortcuts are `Ctrl/Cmd+K` for blended search, `Ctrl/Cmd+Shift+P` for command-only mode, `Ctrl/Cmd+S` to save, `Ctrl/Cmd+W` to close the active tab, `Ctrl/Cmd+Tab` and `Ctrl/Cmd+Shift+Tab` to change tabs, `Ctrl/Cmd+N` for a new note, and `Ctrl/Cmd+Shift+C` for Quick Thought. Inside CodeMirror, `Ctrl/Cmd+K` remains the Markdown-link command; the command-only shortcut always opens the Command Center.
## Evolution of Thought

Evolution of Thought traces a topic, project, folder, active note, or set of open notes across committed versions. Open it from the Command Center, the note toolbar, Project Brain, or a search result. The workspace separates a dated timeline, before/after evidence, the newest canonical view, and source navigation.

Event labels state the strength of the evidence: **Explicit change**, **Documented decision**, **Changed implementation**, **Possible shift**, **Superseded idea**, and **Open question**. Possible shifts are interpretive and are never presented as facts. Ask Slate can continue from an evolution scope, while optional synthesis uses the fixed sections Early View, What Changed, Current View, Key Turning Points, and Unresolved Questions.

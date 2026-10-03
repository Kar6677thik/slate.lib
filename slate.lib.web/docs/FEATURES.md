# Slate web capability matrix

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
7. Milestone 2: command registry and shared desktop/mobile command surfaces.
8. Milestone 3: derived store, background indexing, and hybrid search.
9. Milestones 4–10: grounded assistance, project/context views, evolution, conflicts, duplicates, linking, and virtual concept pages.
10. Milestones 11–18: graph expansion, health, triage, rediscovery expansion, briefs, learning, gaps, and failure memory.
11. Milestones 19–27: advanced reader, capture/clipper, attachment intelligence, voice/writing proposals, workspaces/collections, offline expansion, and final mobile polish.

Each numbered milestone is gated by lint, strict type checking, unit tests, relevant Playwright coverage, and a production build.

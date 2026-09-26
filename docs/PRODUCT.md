# slate.lib — Product specification

Status: reviewed specification with implementation complete through Phase 3B, 2026-09-24.

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

1. **Capture a question:** Android → Quick Thought (or the later Question shortcut) → type “Why does PostgreSQL need VACUUM?” → Save to Inbox. Later add an answer, related links, and move the same note to `computer-science/databases/postgresql/`.
2. **Learn and retrieve:** Windows → quick open or search → read a note → edit Markdown → save → search a phrase from the new content. Search must locate saved content promptly without waiting for GitHub.
3. **Organize:** browse actual folders → create, rename, move, or delete a note/folder. Confirm destructive scope. Existing note identity survives movement.
4. **Keep project context:** create `projects/anythingtcg/` containing `overview.md`, `architecture.md`, and `decisions/`, `experiments/`, `failures/`, `ideas/`, `questions/`. Use ordinary links and optional metadata.
5. **Capture a reference:** Android Share → slate.lib → review shared text, URL, or image → save to Inbox. Upload image bytes before saving a link to them.
6. **Edit elsewhere:** clone the private knowledge repository in VS Code, edit and push normally. The home server imports validated changes; open clients refresh without losing unsaved edits.
7. **Recover work:** a stale save preserves the draft and offers Open latest or Save draft as a new note. Combine text manually when needed. Divergent Git histories pause synchronization and are resolved using ordinary Git/VS Code; a merge workbench is a later feature.

## Feature catalogue and release boundary

The later column records intended direction, not a requirement to build every feature immediately. All write operations eventually exist on both platforms, using different interactions.

| Area | MVP: phases 1–3 | After MVP |
| --- | --- | --- |
| Library | Real folder hierarchy, breadcrumbs, paged listings, create/read/edit notes, create/rename/move/delete files and folders, basic sorting | Multi-select, copy/cut/paste, duplicate, drag/drop, richer context menus |
| Writing | Native plain Markdown editor, Read/Write/Preview modes, explicit save and debounced autosave, recoverable local drafts | Windows split mode, editing commands, stronger syntax editing, tab/workspace polish |
| Reading | Headings, lists, tasks, tables, quotes, fenced code, links, images, heading anchors; selectable text and code copy | Highlighting, footnotes, TOC, Mermaid, math, callouts, safe expandable sections |
| Capture | Inbox, New Note/Quick Thought, Add Image; Android share text/URL/image; Windows image paste | Dedicated Question/Daily Note/Add Link shortcuts, multiple shared files, templates, reviewed merge-into-existing-note |
| Search | Server lexical search, snippets, quick open, title/tag/path/unanswered filters | Type/status/date/has filters, ranking refinement, richer query UI, cached-content search |
| Connections | Stable IDs, wiki links, relative Markdown links, aliases, backlinks, broken-link indicators | Reviewed repair of incoming path links after moves; related-note suggestions, optional graph |
| Questions | Markdown metadata for open/answered status and dates; unanswered search; ordinary related-note links | Dedicated capture/answer controls, aggregation and resurfacing |
| Smart views | Inbox and Recent; unanswered notes available through search | Favorites, Unanswered Questions, Recently Modified, Orphan Notes, Notes With Diagrams/Code, Currently Learning, Needs Review, saved queries |
| Personal navigation | Recent notes, back/forward on Windows | Favorites, pinned folders; optional cross-device preferences |
| Assets | Immutable server assets; PNG/JPEG/WebP/GIF image display, PDF/file attachments opened through platform actions | Thumbnails, PDF text extraction/OCR, storage cleanup tools |
| Sync | Atomic server saves, ETag conflicts, batched Git commits, fetch-before-push, validated fast-forward imports, incremental indexing | In-app history/merge tools only if needed |
| Offline | Bounded read cache and local draft recovery; clear “Saved on this device” wording | Explicit offline downloads, queued edits/creates/uploads and limited local search |
| History | Git history plus in-app read-only note versions; deleted work preserved by the deletion protocol | Diff and restore |
| Rediscovery | Recent notes only | Random Note, Something Forgotten, Old Idea, Old Question, On This Day, Recently Learned, Continue Learning, timeline |
| Delivery | Windows signed MSIX, Android signed APK, private backend; manual release installation | In-app version checks and user-directed installation |
| Other clients / AI | None | Optional Angular web client; much later semantic retrieval and grounded AI tools |

Copy/cut/paste of editor text is standard platform behavior in the MVP; the deferred explorer operations act on files/folders. Local draft recovery is not an offline synchronization engine. Both clients use bounded cache/draft files in the MVP; neither needs a database, full clone or local search index. ID links survive moves; incoming path links can require manual repair until the later reviewed repair feature. Syntax unsupported by the current renderer remains intact in source and has a readable fallback.

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

**Windows:** a proper desktop window with native navigation and editor controls. A collapsible tree, Inbox and Recent sit beside a folder listing or document; richer smart views follow. Search is always reachable; keyboard focus, selection, context menus, breadcrumbs, quick open, and back/forward behave consistently. Large folders are virtualized and loaded in pages. Dark/light themes, scaling, and resizable panes must remain readable.

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

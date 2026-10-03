# slate.lib — Interaction specification

## Milestone A implementation — 2026-10-02

Windows explorer: Ctrl+click toggles, Shift+click selects a visible range, Ctrl+A selects visible rows, Escape clears, arrows navigate, Enter opens, Ctrl+C/X/V transfers selections, and Ctrl+D duplicates. Menus adapt to selection scope; bulk actions preview destinations/counts before applying. Drag notes/folders/selected items onto real folders; Ctrl on drop copies. Invalid self/descendant drops are rejected.

Ctrl+Shift+P opens the context-sensitive fuzzy command palette; arrows navigate and Enter runs. Editor Ctrl+B/I/K, Ctrl+Shift+K, Ctrl+backtick and Ctrl+Shift+7/8/9 insert emphasis, links, code and lists. Heading, quote, fence and attachment commands are also available. Ctrl+K outside the editor searches. Android long-press enters selection; tap toggles, Back clears, and the action bar reviews bulk changes. The editor toolbar transforms selected text. These additions supersede the earlier future-tense descriptions of those specific interactions.


Status: implementation through evolution milestones A–M, 2026-10-03. Release boundaries are defined in [PRODUCT](PRODUCT.md) and [ROADMAP](ROADMAP.md). Behaviors below are MVP unless explicitly marked later. Persisted semantics come from [CONTENT_AND_SYNC](CONTENT_AND_SYNC.md).

## Common behavior

Readable content and reliable writing take priority over dense controls. Use native platform navigation, selection, accessibility, and text editing conventions. Offer light/dark themes, adjustable reading font size, comfortable line length, and a visible keyboard focus indicator. Do not require animation, hover, color recognition, or a precise pointer to complete an action.

Notes display their title and path separately. Folder operations act on actual files; smart-view membership is computed. When metadata controls arrive in phase 4, they write the same Markdown front matter the source editor shows. Preserve unknown fields and source formatting. Never create a second hidden copy of the note body for rich-text state.

Saving works the same on both clients: keep a local draft, autosave after 2 seconds without typing while connected, and provide an explicit Save action. Save immediately on explicit request; navigation can proceed once a local draft is durable without waiting for GitHub. Serialize requests per note; disable only actions whose preconditions are uncertain, not the editor. Typing after a dispatched save remains dirty until acknowledged in a subsequent request.

Autosave applies to already-created notes during a healthy online editing session. A new capture, including Share, requires its first explicit Save before it becomes server content. An offline/recovered draft or uncertain failed save requires explicit Save/Retry after reconnection in the MVP; resuming the app must not silently replay it. Read back the note/asset's current state before retrying an uncertain write; there is no MVP operation-receipt service. Successful acknowledgement clears only the matching saved draft version, never newer typing.

The compact status displays **Saving…**, **Saved**, **Saved on this device**, or **Needs attention**. Expand it to see this note's indexing status and the library's history/GitHub/backup status. “Saved on this device” means a durable local draft only. Pending indexing/sync never makes the editor appear to have lost a successful server save.

## Windows

### Shell and navigation

```text
+---------------------------------------------------------------------+
| slate.lib     [ Search library...                  ]  [+ Note]  (...) |
+---------------------+-----------------------------------------------+
| Library             | < >   Library / databases / postgresql         |
| > inbox             +-----------------------------------------------+
| v computer-science  | MVCC                   Read / Write / Preview |
|   v databases       |                                               |
|     > postgresql    | # Multi-version concurrency control           |
| > projects          |                                               |
|                     | Document or native Markdown editor            |
| Smart views         |                                               |
| Inbox               |                                               |
| Recent              +-----------------------------------------------+
|                     | Backlinks (collapsed)                         |
|                     | Saved                  Sync pending           |
+---------------------+-----------------------------------------------+
```

The right pane displays a folder list, note, or search results; it is not mandatory to show all three at once. The sidebar and later split panes are resizable/collapsible. Restore window layout without requiring all prior documents to download on launch.

Expand the tree lazily. Clicking a folder opens a virtualized list with name, type, and modified date. Breadcrumb segments are actionable. Back/forward retains locations and scroll positions without creating extra note copies. Sort by name, effective updated time, or type; folders first by default, stable path tie-breaks. Open documents retain their ID when moved and update their breadcrumb after a conditional refresh.

Recent means locally opened notes, most recently opened first; it is not Recently Modified. Phase 4 adds favorites by note ID and pinned folders by path. A move performed on this device updates its pin; after an external/other-device move, a missing pin offers repinning. These local preferences do not require cross-device synchronization or a durable change feed.

### Editor and reader

For documents too large for comfortable native editing on a device, offer read-only view and explain the limit; never truncate source. Read is rendered content; Write is the native Markdown source editor; Preview renders the current local draft and is visibly labeled if unsaved. Preserve caret and scroll when switching. Standard selection, undo/redo, clipboard, find-in-document, and IME composition must work. Do not submit partial IME composition as a forced save. Syntax that cannot render remains intact; code fences remain readable if highlighting fails.

Image paste uploads the image, then inserts a relative reference at the saved insertion point. Show progress and a cancellable pending placeholder outside the canonical source. If upload/save fails, retain bytes with the draft and let the user retry/remove them. Never announce an image as saved before both the asset and referencing note are durable.

Phase 3C provides side-by-side editor/preview on Windows and one shared reader for code highlighting, math, Mermaid, callouts, footnotes, anchors and tables. Split scrolling is independent. Diagram/math failures leave source or an error instead of a blank reader. Phase 4 adds syntax editing commands, TOC and carefully bounded expandable sections.

### Search and quick open

Search opens a result view without destroying the current draft. Debounce network requests by 200 ms, cancel/disregard superseded requests, and preserve query text when opening/backing out of a result. Each hit shows title, path, a small highlighted excerpt, and relevant type/status. Empty query shows recent searches/notes stored on this device. Invalid filters show the specific error and retain the query.

Quick open is a keyboard-first overlay for title/alias/path. Enter opens the selected result; Escape returns focus to the origin. Full search searches body and filters. The result count/page indicates incomplete indexing or a stale snapshot where applicable. No result preview may execute HTML from a note.

The command palette arrives in phase 4 and lists actual available commands, their shortcuts, and enabled state. It invokes the same actions as menus, including New Note, Move, Capture Question, Toggle Split, and Sync now; it is not a separate command language.

### Keyboard and context menus

| Shortcut | Action | Release |
| --- | --- | --- |
| `Ctrl+N` | New note in current folder, otherwise Inbox | MVP |
| `Ctrl+S` | Save current draft now | MVP |
| `Ctrl+P` | Quick open | MVP |
| `Ctrl+Shift+F` | Library search | MVP |
| `Ctrl+F` | Find in open document | MVP |
| `Alt+Left` / `Alt+Right` | Back / forward | MVP |
| `F2` | Rename selected explorer item | MVP |
| `Delete` | Delete selected explorer item with confirmation; never intercept editor text deletion | MVP |
| `Ctrl+Shift+P` | Command palette | Phase 4 |
| `Ctrl+C/X/V` | Copy/cut/paste explorer selection when explorer focused; native text actions in editor | Explorer phase 4 |
| `Ctrl+D` | Duplicate selected explorer item when explorer focused | Phase 4 |
| `Ctrl+B` / `Ctrl+I` | Wrap/toggle Markdown emphasis in editor | Phase 4 |

MVP file context menu: Open, Rename, Move, Delete. Folder menu: New Note, New Folder, Rename, Move, Delete. Background menu: New Note, New Folder, Sort. Favorite/Unfavorite, Pin/Unpin, Copy, Cut, Paste, Duplicate and multi-selection are implemented. Only valid actions appear enabled; destructive actions name the item/scope.

Phase 4 drag/drop: dragging notes/folders within the library moves them; Ctrl-drag copies. Show the intended destination and operation before drop; reject descendant/self/occupied destinations. Asset files dropped into the editor upload attachments; arbitrary OS folders are not recursively imported without a future explicit import flow. Dropping into smart views is disabled except Inbox, which maps to the real `inbox/` folder. Every drag action has a menu/touch equivalent.

## Android

### Navigation and reading

```text
+--------------------------------+
| Library                  (...) |
| Library > databases            |
| [ Search in library...       ] |
|                                |
|  postgresql/                   |
|  MVCC                          |
|  Isolation levels              |
|                                |
|                    [+ Capture] |
+--------------------------------+
| Library     Search      Inbox  |
+--------------------------------+
```

Use bottom navigation for Library, Search, and Inbox. Capture is a prominent action reachable from each. Folder traversal uses a single list and breadcrumb/back navigation, not a permanently expanded desktop tree. Note reading uses the full width with restrained margins, pinch/zoom only where appropriate for images, selectable prose, and Copy on code blocks. Back returns to the previous location and scroll position. Links show a chooser when ambiguous.

Before a move, show known incoming path links that may need manual repair; generated ID links remain valid. A missing referenced heading opens the correct note with a warning. Long-press opens an item's context actions; the overflow menu is an accessible equivalent. Create/rename/move/delete and folder creation exist in the MVP. Move uses a folder picker with breadcrumb navigation and New Folder. Multi-select/copy/cut/duplicate use a selection action bar. Desktop shortcuts and drag/drop are not required to use any mobile operation.

### Capture and Inbox

MVP Capture opens directly into **Quick Thought**, with the text box focused, keyboard visible, Inbox selected and a filename suggested from the first line. New Note allows a destination; Add Image uses a picker. Paste a URL as ordinary text or receive it through Share. No required title/tag dialog or template chooser.

Phase 4 adds dedicated Question, Daily Note and Add Link shortcuts. Question presets `type: question`, `status: open`; Daily Note opens/creates today's note using device-local date. Add Link asks for URL/comment and does not crawl the web page.

New Note may select a destination; Quick Thought, image and shared captures default to Inbox. The later Daily Note shortcut uses `daily/`. Save gives a small confirmation with Open/Move actions, then returns to the origin. If disconnected, the action is labeled **Save draft on device**, with no claim of a library save. Keep the draft visible in Inbox's local-drafts section until explicitly saved/discarded.

Inbox is a workbench for unfinished material. MVP actions are open, edit, rename, move and delete. Tags/type/question status are ordinary front matter; dedicated question/answer controls are implemented. An answered question has an answer date and may link to related notes, without a separate entity. Append to note previews the combined text and preserves the source capture.

### Share integration

Register as a share target for text/URLs and supported image streams. Copy shared stream bytes into app-private draft storage while the Android URI grant is valid; never store another app's temporary URI as the asset. Show a preview plus optional comment and default destination Inbox. The user taps Save or Save draft; simply receiving a share never silently publishes it. Unsupported/multiple items explain what can be accepted and retain no inaccessible placeholder.

If the activity is killed or switched away, a durable share draft remains discoverable on next launch. Share supports up to eight explicitly shared files within the documented byte limits. No broad device-storage permission is needed to capture an explicitly shared stream.

### Editing and lifecycle

Use a native Markdown editor with an Edit/Preview toggle and a visible Save action above the keyboard. Controls respect safe areas, keyboard insets, font scaling, and touch sizes. Preserve selection/draft across rotation, app suspension, process restart after a durable draft save, and interrupted requests. Do not depend on background execution to complete a save. A small Markdown insertion toolbar is available; the initial editor must remain comfortable for a few paragraphs and small corrections.

## Smart views

Smart views do not own notes and cannot be renamed/moved/deleted as folders. Reveal in Folder always navigates to the physical parent. MVP Inbox lists `inbox/` descendants and Recent is local open history. Unanswered notes are reachable with `is:unanswered` search without a separate view.

Phase 4 adds Favorites (local IDs), Unanswered Questions (`is:unanswered`), Recently Modified (known `updated` dates first), Orphan Notes (no inbound links except self), Notes With Diagrams (`has:diagram`), Notes With Code (`has:code`), Currently Learning (`status:learning`), and Needs Review (`status:needs-review`). Graph views, custom saved queries, and resurfacing are later. A query view must make active filters visible.

## Failures and conflicts

| Situation | User sees / can do |
| --- | --- |
| Server unavailable | Cached content labeled with last fetched time; local drafts remain editable; retry or export draft |
| GitHub unavailable | Saved content remains usable; unobtrusive Sync pending; details explain remote reachability |
| Index delayed/failed | Search freshness warning; if rebuilding, search is temporarily unavailable. Folder/ID reads still work |
| New server version during editing | “This note changed elsewhere”; keep draft, compare against latest, choose resolution |
| Concurrent save | Keep draft; offer Open latest and Save draft as a new note. Manual copy/edit can combine content; no MVP three-way merge workbench |
| Divergent Git histories | Global Sync needs attention; show reason/operator recovery instructions. Ordinary local saves continue; merge in Git/VS Code during maintenance |
| Folder delete | Name and descendant counts with explicit confirmation; explain recovery through history, not guaranteed immediate Undo |
| Missing asset/link | Alt text or readable link plus specific missing/ambiguous state; no broken blank screen |
| Storage full / permission error | Save not acknowledged; draft retained if local storage works; offer copy/export text if device storage also fails |
| Application update available | Version/release notes, Download/Install or Later; checksum is verified and the OS confirms installation |

Do not erase a draft as a side effect of Retry, Refresh, Back, authentication failure, app update, or accepting an unrelated refresh. “Discard draft” is explicit. A conflict resolution can conflict again if another device saved meanwhile; explain and repeat without losing the proposed text.

Milestone B: Favorites opens an ordinary note-ID result view. Folder menus/commands toggle pins. Navigation exposes pinned folders; missing pins offer Locate / Repin / Remove pin and never guess an external rename. Device-performed folder moves update nested pins. Favorites open by UUID after note moves.

Milestone C: Question asks for title/context; Answer appends an Answer section and changes status to answered while preserving source. Add Link accepts HTTP(S)/comment and never fetches the page. New Note offers available templates; Android applies a template only to a blank draft. {{title}} and {{date}} expand in the body. Capture append shows the destination and complete proposed source before saving. Existing saved captures can also be appended while their source stays intact. Share receiving never authorizes publication; only an explicit Save does. Additional pending shares appear in Recent drafts instead of stacking pages.

Milestone D implemented (2026-10-02): Windows navigation/command palette and Android Commands/Search open Smart views. Each view states its definition and shows 20 notes per page with previous/next controls. Select a result and Open note. Save the current query from Smart views; Manage saved searches offers Rename/Delete. Favorites remain device-local.

Current scope: milestones A–M are implemented. Dated phase/milestone records below are historical checkpoints, not statements that later completed features are still deferred. ROADMAP contains current verification and device limitations.


Milestone E implemented (2026-10-02): Add search filter beside the query offers one field at a time; direct query syntax stays editable. Previous/Next pages keep only one page of results in memory. Superseded responses cannot replace a newer query. Example: type:question status:open postgres.


Milestone F implemented (2026-10-02): move/rename previews offer Review and repair, Move without repairs, or Cancel. Review shows each affected source path, old/proposed links, resolved target, and original/proposed source. Check links offers explicit destination selection and a second preview; missing-heading repair explicitly links to the chosen note. Note actions expose Convert wiki links. The Windows palette exposes both actions; Android Commands includes Check links.


Milestone G implemented (2026-10-02): Outline shows all six heading levels with hierarchy indentation and a current-section highlight. Toolbar arrows navigate sections; Copy copies a stable note/heading wiki link. Android dismisses the drawer before jumping. Opening a heading inside a closed callout expands its ancestors. Reduced-motion preference disables smooth scrolling.


Milestone H — attachments

Use Manage attachments in the Windows palette or Attachments in Android Commands. Pages show at most 20 items, with all/unreferenced filters. Select an item for metadata/hash and an image thumbnail; Actions opens referencing notes, derived-text reading, extraction or reviewed permanent removal. Derived text is paged in 12,000-character chunks. Removal reports reclaimed bytes. Parsing errors refuse cleanup. Native interaction and physical-device OCR checks remain required.

Milestone I — offline work

Offline work contains Download current note (when applicable), Download folder, Download pinned folder, Manage downloads, Set quota, plain-text offline search and pending work. Downloads show progress and explicit cancellation. Every downloaded copy can be stale; refresh is explicit and timestamped. Remove downloaded copies affects only offline copies. Pending work can be retried; only never-attempted operations can be cancelled because attempted work may already have reached the server. Newer local drafts survive older queued saves. Native reconnection, Android suspension and airplane-mode interactions still require device checks.

Milestone J — merge workbench

A stale save opens version selection, a proposed-result editor, conflicting-block controls and a complete-result review checkbox. Block choice is disabled after manual editing to avoid replacing that manual work. Saving a stale proposal is rejected and local drafts remain. Separate-note recovery asks for a collision-safe filename. Review independent sync conflicts is an advanced command on Windows/Android; it previews incoming paths and asks to combine. It creates a local merge commit; ordinary Sync publishes later.

Milestone K — history browser

Windows History and Android Version history use the same paged browser. Select a version to read, compare, restore, or restore as new. Long source/diff text is paged in 16,000-character chunks. Restore confirmation includes a bounded preview; the full historical source is separately readable. Recover deleted notes is in both command menus. Recovery prompts for an existing folder and collision-safe filename. Current restoration rejects stale revisions and preserves the newer server content.

Milestone L — rediscovery (2026-10-02)
Both clients expose Rediscover with random notes, older ideas/open questions, Something Forgotten, On This Day, explicit learning states, and a dated timeline. Results explain their eligibility. Unknown dates are omitted from date-driven views; templates are excluded. Date-based ordering is deterministic; Random Note is deliberately random. Results are paged in groups of 20 from cached parsed metadata.
Reading history is opt-in and device-local: UUID plus last-opened timestamp, at most 1,000 entries retained for 180 days, never uploaded. Privacy controls enable/disable and clear it. Forgotten results exclude recently opened notes when tracking is enabled; when disabled, only explicit note age is used. No reading activity before opt-in is implied. No canonical metadata migration or production changes.

Milestone M — graph and related notes (2026-10-02)
Windows commands and Android note actions expose a current-note graph and up to eight related notes. Graph traversal is bidirectional over resolved wiki/Markdown links, with directed edges displayed, depths 1–3, folder/type filters, 40 client nodes (60 API maximum) and 240 edges. Limits are visible. Selecting a plotted node or its accessible numbered row opens it. Filters restrict traversal; the current note remains visible.
Related scores are deterministic: direct link +12; shared incoming source +4 each (maximum 20); shared tag +3 each (maximum 15); title/heading term +1 each (maximum 4); same non-root folder +1. Every score has an explanation. Ties sort by path. Templates are excluded from suggestions. All data is rebuilt from the existing link index and cached parsed metadata; there are no embeddings, AI calls, graph database or canonical-data migrations.
Full solution Windows/Android build: zero warnings/errors. All 181 tests pass, including graph edge direction, incoming traversal, filters, limits, invalidation, score explanations and deterministic ordering. L was verified with 179 passing tests before M. Native and physical-device acceptance remains separately reported.

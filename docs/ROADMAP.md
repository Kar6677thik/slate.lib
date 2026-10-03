# slate.lib — Implementation roadmap

## Evolution verification record — 2026-10-02

Milestone A implements shared selection/Markdown transforms, reviewed journaled bulk operations, immutable copy identities/internal-link rebasing, Windows selection/keyboard/drag integration, Android long-press actions/insertion toolbar, and the Windows searchable command registry. Real-file and authenticated HTTP tests cover preconditions, collisions, retries, rollback after interruption and retained deletion bytes. Both native targets compile. Pointer/long-press/keyboard qualification on physical devices remains required. Commands dependent on favorites/pins and knowledge workflows are completed with B/C; subsequent completion records appear below.


Status: evolution A–M implemented; final verification recorded 2026-10-03. Read [PRODUCT](PRODUCT.md), [ARCHITECTURE](ARCHITECTURE.md), [CONTENT_AND_SYNC](CONTENT_AND_SYNC.md), [UX](UX.md), and [OPERATIONS](OPERATIONS.md) as one contract. MVP ends at phase 3D. Every phase leaves a coherent runnable slice; no calendar estimates are implied.

## Phase 1 — Connect, browse, read

**Implementation scope clarification (2026-09-24):** the implementation request narrows this first slice to Windows browse/read. A later implementation request makes phase 2 a Windows editing/filesystem slice, so Android screens/device builds move to phase 3B and installed-package qualification to phase 3D. Phase 1 uses an unpackaged Windows development executable. The additional shared contracts, safe reader, explicit ID setup and simple device tokens below remain in scope. Phase 1 acceptance is the real filesystem → API → Windows browse/read flow, with automated backend tests and a native development-build smoke test.

**Goal:** start a server over real Markdown and open it in a native client before building writing/synchronization infrastructure.

**Dependencies:** specification exit criteria; supported development tooling; a small synthetic knowledge repository. Pin supported .NET/MAUI and Markdown dependencies when scaffolding actually begins. Bootstrap library/note IDs through a small explicit setup command; no automatic Git import needed yet.

**Exact deliverables:**

- Minimal ASP.NET Core process reading a configured Markdown directory; safe path handling, current ID/path map, paged folder listing and note reads.
- Native Windows shell with an expandable lazy tree/list, breadcrumbs, open note and safe Markdig preview. Android browse/read screens follow in phase 3B.
- Private connection and per-device token handling; loopback-only development is sufficient before remote-device testing. Secure storage in clients and a local token provisioning command.
- Minimal shared content/contracts and API-client code only where reused. Unpackaged Windows development smoke build; Android qualification follows in phase 3B and installed packaging in phase 3D.
- Reader selection, scaling and keyboard navigation checks. Full native editor selection/IME/caret/64-KiB qualification follows in phase 3B.

**Usable result:** start server → native client connects → real folders appear → Markdown renders on Windows.

**Deferred:** server writes, Git automation, Lucene, assets, client caches/databases, complete explorer actions, CI/deployment automation and rich renderer.

**Completion criteria:** open fixture notes through the native Windows development build, browse pages without fetching the whole library, reconnect after server restart, reject invalid/unauthorized paths, and prevent Markdown scripts/remote subresources from executing. Verify reader text selection, scaling and keyboard navigation; touch/install checks follow with device qualification. Record any concrete MAUI blocker; prefer a narrow platform handler over switching frameworks. Do not require exhaustive UI tests.

**Phase 1 verification record (2026-09-24):** SDK 10.0.401 on Windows 11 x64; solution restore/build passed with zero warnings, and 27 automated tests passed. The running API returned all five sample notes from disk; a real Windows junction outside the library returned 400. The native MAUI development app connected, expanded/collapsed three folder levels, opened notes using keyboard selection, rendered headings/lists/task lists/code/tables, followed a relative Markdown link through the API, allowed reader text selection/zoom, and reconnected after a stopped/restarted backend. A narrow Windows document-host customization loads sanitized HTML directly instead of MAUI's script-inserting HTML loader; scripts, native bridges, network subresources and downloads remain disabled. Renderer sanitization has automated coverage. Installed MSIX, Android, touch/IME/editor behavior, remote HTTPS and 20,000-note performance remain unqualified and deferred as above. No phase 2 feature has been implemented.

## Phase 2 — Windows editing and filesystem operations

**Implementation scope clarification (2026-09-24):** the implementation request deliberately limits phase 2 to the Windows client, the existing ASP.NET Core boundary and real-library mutations. Search, Git, Android, assets, deployment, durable offline work, multi-device conflict UX and all later product systems remain deferred. This clarification supersedes the earlier phase-2 grouping while preserving those requirements for phase 3 or later.

**Goal:** turn the phase 1 Windows browser/reader into a usable Markdown editor and file explorer without bypassing the API boundary.

**Dependencies:** phase 1 Windows client/API; safe atomic replacement support on the server filesystem. Use sample/disposable data until backup is configured.

**Exact deliverables:**

- Windows native Markdown source editor with Write, Preview and Split modes, explicit Save, visible saved/unsaved state and Save/Discard/Cancel protection on navigation, reconnect and close.
- Create Markdown notes and folders. New notes receive UUIDv4 front matter and are opened for editing.
- Rename, move, copy, cut/paste, duplicate and delete notes/folders. Context menus and the main Windows keyboard shortcuts expose these actions.
- Authenticated API mutations with portable path/name validation, containment/reparse-point checks, collision rejection and no physical-path disclosure.
- Optimistic note saves using the current revision, same-directory temporary files and atomic replacement. Copied notes/folder trees receive fresh note IDs.
- Explicit refresh after mutations or external filesystem changes; folder lists remain metadata-only and lazy.

**Usable result:** create folder → create note → edit/preview → save → close/reopen → organize through ordinary Windows explorer actions, with every change reflected in the configured library.

**Deferred:** Android, installed packages, local durable drafts, autosave, search, Git/history, assets, Inbox/Recent, deployment/CI, backup, links/backlinks and richer editing/rendering.

**Completion criteria:** create/edit/save/reopen real Markdown; prevent accidental loss of dirty text; complete file and folder rename/move/copy/delete with predictable collision behavior; preserve IDs on rename/move and assign fresh IDs on copy; reject traversal, unsafe names, symlinks/reparse points and stale writes; refresh the tree after mutations; pass focused real-filesystem/API tests and the existing phase 1 suite; produce a clean solution build.

**Phase 2 verification record (2026-09-24):** restore and solution build passed with zero warnings; all 43 automated tests passed against .NET 10.0. The Windows development executable launches successfully after replacing an unsupported MAUI XAML accelerator with native key handling. Tests exercise real temporary directories and the HTTP boundary. A disposable configured library completed create folder/note, save, rename, copy, duplicate, move, folder rename and recursive delete; disk contents and fresh copied UUIDs were inspected. The current automation surface could verify the running native process but could not drive its window, so interactive control behavior remains a focused manual smoke check rather than a claimed result.

## Phase 3A — Search, Git synchronization and history

**Goal:** make every accepted Windows/API edit immediately searchable, commit it to local Git history in sensible batches, synchronize safely with an optional private remote, and expose read-only note history.

**Dependencies:** phase 2; a configured derived-data directory; Git CLI; an explicitly initialized Library repository. A remote is optional and all source operations work locally without one.

**Exact deliverables:** embedded Lucene.NET lexical index outside the Library, incremental mutation/import updates, explicit rebuild, bounded query grammar and Windows search results; one serialized Git coordinator with debounced local commits, fetch-before-push, fast-forward-only imports, visible sync state and an ordinary-Git divergence runbook; history list/content APIs and a read-only Windows history view.

**Usable result:** save → search immediately → commit locally → synchronize when possible; an external fast-forward push updates the live tree, index and history without restarting Slate.

**Deferred:** Android, durable client drafts/caches, Inbox/Recent, assets, rich Markdown, packaging, deployment, application updates and production backup.

**Completion criteria:** create/edit/rename/move/copy/delete update search without a full rebuild; a deleted index rebuilds from source; nearby mutations batch into one commit; temporary bare-remote tests cover push, fast-forward add/modify/delete/rename, remote outage and divergence; history follows a renamed note and historical source is read-only; all phase 1/2 tests continue to pass.

**Phase 3A verification record (2026-09-24):** restore and solution build pass with zero warnings; all 54 automated tests pass. Lucene tests cover fields, ranking inputs, filters, immediate mutation updates, rebuild/reopen/corruption recovery and authenticated API behavior. Git tests use temporary repositories/bare remotes for batching, checkpoint-before-delete, initial push, external add/modify/delete/rename import, incremental search refresh, outage, divergence, interrupted-activation recovery, history and commit-parameter rejection. A separate disposable live backend completed create → search → edit → commit → history → push → second-clone edit/push → Sync → filesystem/tree/search/history verification with equal local/remote heads. The Windows development executable launched and remained responsive; native-control automation was unavailable, so interactive search/history/menu behavior remains a manual smoke check.

## Phase 3B — Android, fast capture and durable drafts

**Goal:** bring the established browse/read/write/search loop to Android and protect in-progress text across lifecycle and connectivity interruptions.

**Exact deliverables:** Android navigation/editor/search, Quick Thought and Inbox/Recent, Share text/URL, bounded recent-note caches, durable atomic drafts, Windows draft parity, and Android long-press/overflow/folder-picker equivalents for existing explorer operations.

**Usable result:** capture and edit from either native client while preserving drafts through process termination and brief disconnection.

**Phase 3B verification record (2026-09-24):** the MAUI project now targets Windows and Android. Android has mobile drill-down Library browsing, server search, Inbox/Recent tabs, full-width safe Markdown reading, Edit/Preview, revision-safe save, note/folder creation, overflow mutations with a folder picker, Settings for server/token, Quick Thought, and an exported `ACTION_SEND` plain-text/URL target. The Share activity atomically stages payloads before relying on an existing page. Both clients use the shared atomic JSON draft store; existing-note drafts retain base source/revision, pending captures retry with one client UUID, recents retain at most 100 IDs, and disposable note caches are capped at 100 MiB on Android/500 MiB on Windows. The backend capture route creates ordinary Markdown under `inbox/`, recreates that folder, immediately indexes it, and marks Git pending.

Solution restore and builds pass with zero warnings; all 65 automated tests pass. The explicit `net10.0-android` build produced a signed debug APK using Android workload `36.1.69`. No emulator or device was attached, so Android launch, touch/IME, rotation/process-death, real Share-sheet delivery, and device networking were not manually claimed. The Windows development target was regression-built; its interactive native controls remain subject to the same manual smoke limitation recorded in 3A.

## Phase 3C — Assets, links and rich Markdown

**Goal:** add image/file capture and dependable connected reading without compromising canonical Markdown.

**Exact deliverables:** immutable asset storage and upload/read APIs, Windows image paste, Android image/share capture, safe image/PDF handling, stable ID/title/alias/path links, headings, backlinks/broken-link indications and the phase-appropriate rich renderer.

**Usable result:** notes can include portable references and safely rendered personal assets on both clients.

**Phase 3C verification record (2026-09-25):** the server now stores immutable UUID-named assets and sidecar metadata under configured `Data:AssetsPath`, outside the Markdown/Git tree. Uploads stream with a 25 MiB default limit, hash/idempotency checks and magic-byte media detection. Both editors stage pending bytes durably; Windows provides image/file buttons and clipboard-image paste, while Android provides pickers and single image/PDF/generic Share capture. Canonical note references remain relative `.assets/<uuid>.<ext>` Markdown and are rewritten when their source note moves.

Wiki links resolve deterministically by ID, explicit path, title/filename and alias. Ambiguity and missing headings remain visible, rename preserves the old filename as an alias, and a rebuildable in-memory link index supplies backlinks after mutations and Git refresh. The shared local-file renderer uses Markdig plus pinned Mermaid 12.0.0, KaTeX 0.18.9 and highlight.js 11.12.0 bundles for headings, footnotes, callouts, tables, code, diagrams and math. Raw HTML is disabled, output is allowlisted, CSP blocks network content, and Mermaid uses strict mode. All 77 automated tests pass, including durable pending-asset retry and backlink reconciliation after a real Git import. The Windows and explicit Android development targets compile; no emulator/device was attached, so clipboard, Share providers and visual WebView output still require the listed manual smoke checks.

## Phase 3D — Packaging, deployment, updates and backup

**Goal:** qualify and operate the complete daily-use MVP on the private home server and installed devices.

**Exact deliverables:** signed Windows MSIX and Android APK, one-replica k3s deployment, private HTTPS/auth revocation, manual release/deploy flow, health/status, encrypted off-site backup with tested restore, and bounded user-directed update checks.

**Usable result:** the cross-device application is installable, privately hosted, recoverable and maintainable with personal content.

**Phase 3D verification record (2026-09-25):** the API has anonymous liveness/readiness, authenticated path-safe operational status, durable per-device create/list/revoke/rotate credentials, and authenticated release metadata/downloads. The clients compare strict versions, check once daily or on demand, verify size/SHA-256 and hand the package to the OS installer. Release targets produce a Windows MSIX and Android APK; production CI injects publisher keys only from secrets.

The backend and restic helper have multi-stage/container definitions. k3s uses one `Recreate` replica, a retained host-path volume, non-root/read-only security settings, probes, bounded resources, private NodePort and a daily encrypted backup CronJob. Main CI tests, publishes immutable GHCR images and deploys exact SHA tags over Tailscale/SSH; tag CI publishes signed clients and mirrors a release manifest. A disposable encrypted recovery drill deleted its source, restored canonical data, checked all restic packs and verified API search, backlinks and asset bytes. Live home-server deployment and physical-device publisher/network qualification require the owner's infrastructure and secrets.

## Phase 4 — Explorer, reading and capture polish

**Goal:** improve sustained work after the reliable MVP exists.

**Dependencies:** phase 3D in daily use and tested backup. Do not bundle every item into one indivisible release.

**Exact deliverables:** both-platform copy/cut/paste/duplicate/multi-select; Windows drag/drop, command palette and Markdown shortcuts; Android selection bar/insertion toolbar; favorites/pins; dedicated Question/Daily Note/Add Link/answer controls; remaining predefined smart views and type/status/date/has filters. Add reviewed incoming path-link repairs, wiki-to-relative-Markdown export conversion, TOC and carefully bounded expandable sections.

**Usable result:** polished explorer/editor/reader interactions and easier application updates, without a new backend platform.

**Deferred:** automatic deployment agents unless manual deployment proves burdensome; full offline sync, graph, AI, recommendations and web client.

**Completion criteria:** copied subtrees get new IDs with correct internal links; all bulk mutations are recoverable and never overwrite collisions. Link-repair preview matches changes and rechecks affected revisions. Renderer fixtures fail safely on malformed input. Every pointer action has a keyboard/touch alternative. Install signed updates over prior packages while retaining drafts/settings; content-only pushes trigger no build/release.

## Phase 5 — Deliberate offline work and history

**Goal:** extend beyond brief disconnections and expose useful Git history inside the app.

**Dependencies:** stable online revision contracts and evidence from daily use that offline work is needed. Write a focused update to these same specs before implementing the queue; do not introduce a competing architecture set.

**Exact deliverables:** selected-folder/favorite downloads, explicit quota/coverage, downloaded-content search, queued creates/edits/assets and in-app note history/diff/restore. Decide then whether SQLite simplifies transactional pending-operation storage, and define receipt/retry retention/migration before unattended replay. Add base/local/current compare tooling if it earns its complexity. History follows IDs; restore writes a new revision/commit without rewriting history.

**Usable result:** intentional offline work, visible coverage and reviewable synchronization; convenient historical recovery.

**Deferred:** CRDTs, automatic text merge, guaranteed mobile background execution, offline structural mutations and whole-library downloads by default.

**Completion criteria:** survive process/device restarts with queued text/assets, upload dependencies first, handle uncertain/expired retries, preserve drafts under quota pressure and stop at remote edits/deletes without resurrection. Restoring across rename/deletion respects current ETags and destination collisions. Migrate earlier local draft files without losing pending work.

## Phase 6 — Rediscovery and optional extensions

**Goal:** make old knowledge useful again when actual usage warrants it.

**Dependencies:** reliable accumulated knowledge and a concrete personal workflow for each feature.

**Exact deliverables:** independently selectable Random Note, Something Forgotten, Old Idea/Question, On This Day, Recently Learned, Continue Learning and a simple timeline using known dates/statuses. Unknown historical dates remain unknown unless a later history-derived view explicitly supplies them. Reading-history storage, related-note suggestions, a small graph or Angular client require a demonstrated need. AI/vector retrieval needs a separate scoped feature/privacy decision; this review adds no model/provider/vector store.

**Usable result:** understandable routes back to forgotten notes, with optional clients remaining API consumers.

**Deferred:** anything without a personal workflow, including enterprise features, analytics and unsolicited recommendation/notification systems.

**Completion criteria:** each selected feature explains why content appears, is dismissible, preserves canonical files and does not require all clients to download the library. AI does not start without explicit scope and acceptance cases.

## Test priorities

Prefer tests of behavior with real temporary directories/Git repositories/Lucene indexes over mocks of every internal class. Add tests when the associated slice ships, not an upfront testing framework.

| Area | Required evidence |
| --- | --- |
| Files/API | Atomic save crash points, preconditions, uncertain-response read-back, path collisions, recursive operation recovery, delete checkpoint; never lose an acknowledged draft |
| Identity/links | UUID survives moves, duplicate IDs rejected, duplicate names ambiguous, aliases and headings resolve, collision-free anchors, code examples ignored, missing targets/backlinks update |
| Markdown/assets | Known-field validation with unknown metadata preserved, malformed/bounded YAML, relative assets across moved depths, interrupted upload, immutable-ID mismatch, portable materialization |
| Search | Add/edit/rename/move/delete, stale-worker race, rebuild from source, ranking/query fixtures and 20,000-note benchmark |
| Git | Fetch-before-push, rejected push, outage, fast-forward import validation, local-head race, divergence runbook, interrupted activation and damaged-repository recovery |
| Security | `../`/encoded traversal, absolute paths, symlinks/submodules, case collisions, Git argument injection, Markdown HTML/script/scheme abuse, oversized/active uploads, missing/revoked tokens, private network and CI artifact boundaries |
| Native UI | Focused device smoke tests for editing/IME/clipboard/share/lifecycle/installation/accessibility; no exhaustive trivial-control or pixel tests |
| Preservation | Restore from independent backup, recover unpushed text/history and assets, and keep local drafts through update/reconnect |

## Specification Exit Criteria

Before implementation begins:

- [x] All five documents were read completely and reviewed as one system; no competing spec/configuration set exists.
- [x] Canonical Markdown/IDs/folders/assets, server/client storage and GitHub responsibilities have one clear definition.
- [x] The first phase is a small runnable Windows browse/read slice; phase 2 adds Windows write/explorer mutations, phase 3A search/history, and phase 3B Android.
- [x] MVP ends at phase 3D with both native clients, basic explorer/capture/assets/search, external Git editing and tested backup; 3A–3D are independently runnable increments.
- [x] Git uses one owner and fast-forward imports; divergent histories and repository failure have a preservation-first operator runbook.
- [x] Client databases, durable change feeds, automatic divergent merging and full merge UI are deferred; optimistic conflicts and local draft protection remain.
- [x] Native-client capability gaps, search dependency risk, link portability limits and manual recovery tradeoffs are explicit and have implementation qualification steps.
- [x] Important filesystem/identity/link/search/Git/API/security behavior has phase-appropriate test criteria.
- [x] Application releases remain separate from content updates; AI/enterprise/framework building is outside initial implementation.
- [x] This review changed documentation only; no project, production code, deployment or pipeline files were scaffolded.

Specification readiness permits phase 1 to begin; it does not claim that unbuilt software has passed the phase tests.

Milestone B verification: versioned library-scoped preferences, one-time legacy bookmark import, Favorites navigation, folder pins and missing-pin recovery are implemented. Five behavioral tests cover moves, external missing pins, restart, migration preservation, schema rejection and concurrent instances. All 118 tests pass.

Milestone C verification: dedicated question/answer/link capture, serialized daily creation, template expansion, reviewed draft/saved-capture append, durable multi-file Share and explicit submission state are implemented. Nine new behavioral/HTTP tests cover unknown metadata preservation, stale answer/append rejection, search membership, concurrent daily creation, templates, active-scheme rejection, interrupted Share recovery and no implicit submission. All 127 tests pass; both native targets compile. Legacy uppercase Daily folders are deliberately reused without an unreviewed rename; new folders use lowercase daily.

Milestone D verified (2026-10-02): seven indexed smart views plus Favorites and saved-search create/rename/delete are connected to Windows and Android. Full solution build: 0 warnings/errors. All 133 tests pass, including real Lucene paging, date order, changing orphan/question membership, index reopening, preference restart, and authenticated HTTP. Native interaction checks remain outstanding. Subsequent milestone completion is recorded below.

Current scope: milestones A–M are implemented. Dated phase/milestone records below are historical checkpoints, not statements that later completed features are still deferred. ROADMAP contains current verification and device limitations.


Milestone E verification (2026-10-02): advanced filters, title ranking, compact builders, and client pagination implemented. Both native targets built without warnings/errors; all 142 tests passed before the final tolerant-date compatibility adjustment, which subsequently passed. Offline search remains part of I. Subsequent milestone completion is recorded below.


Milestone F verified (2026-10-02): reviewed incoming repairs, missing/ambiguous link management, Markdown edges, wiki conversion, and case-only rename integration implemented. 150 tests passed; full solution builds Windows and Android with 0 warnings/errors. Tests cover stale source/target rejection, interrupted move+repair recovery, portable fragments, ambiguity, case-only rename, and Markdown-derived backlinks. E compatibility recheck also passed. Subsequent milestone completion is recorded below.


Milestone G verified (2026-10-02): reading outline/navigation/disclosures implemented; full solution builds both native targets with 0 warnings/errors and all 154 tests pass. New renderer tests cover six levels, duplicate IDs, fenced-code exclusion, callout body preservation/default state, raw-HTML safety, and local asset materialization. Subsequent milestone completion is recorded below.

Milestone H verified (2026-10-02): attachment catalog/references, isolated PDF extraction, bounded thumbnails, local OCR integration, and explicitly reviewed cleanup implemented. Full solution builds both clients with 0 warnings/errors; all 161 tests pass. OCR engine and native-device interaction require operational verification; no production deployment performed. Subsequent milestone completion is recorded below.
Milestone I verified (2026-10-02): explicit offline note/folder/pin downloads, persistent quota-managed copies/assets, plain-term offline search, durable save/upload dependencies, receipt-based replay, safe cancellation and pending-work controls implemented. Both native targets build with 0 warnings/errors; all 168 tests pass, including real HTTP lost-acknowledgement/restart tests. Legacy JSON is retained; no SQLite migration. Subsequent milestone completion is recorded below. Airplane-mode/Tailscale transitions and suspended-device behavior require manual verification.

Milestone J verified (2026-10-02): shared three-way workbench and controlled independent-file history merge implemented. Full solution builds both clients with 0 warnings/errors; all 173 tests pass. Tests cover random diff reconstruction, insertion/delete/edit ambiguity, bounded large comparisons, parent preservation and stale previews; existing same-file divergence test now verifies controlled merge refusal. Windows native image-file lifetime regression found during the full suite was corrected. Subsequent milestone completion is recorded below.

Milestone K verified (2026-10-02): comparison, bounded history metadata, current/as-new restore and deleted recovery implemented. Both clients build with 0 warnings/errors; all 177 tests pass. Real Git tests cover new commit ancestry, stale revisions, collision rejection, stable deleted identities, original history preservation and rebased attachment URLs. Subsequent milestone completion is recorded below.

Milestone L — rediscovery (2026-10-02)
Both clients expose Rediscover with random notes, older ideas/open questions, Something Forgotten, On This Day, explicit learning states, and a dated timeline. Results explain their eligibility. Unknown dates are omitted from date-driven views; templates are excluded. Date-based ordering is deterministic; Random Note is deliberately random. Results are paged in groups of 20 from cached parsed metadata.
Reading history is opt-in and device-local: UUID plus last-opened timestamp, at most 1,000 entries retained for 180 days, never uploaded. Privacy controls enable/disable and clear it. Forgotten results exclude recently opened notes when tracking is enabled; when disabled, only explicit note age is used. No reading activity before opt-in is implied. No canonical metadata migration or production changes.

Milestone M — graph and related notes (2026-10-02)
Windows commands and Android note actions expose a current-note graph and up to eight related notes. Graph traversal is bidirectional over resolved wiki/Markdown links, with directed edges displayed, depths 1–3, folder/type filters, 40 client nodes (60 API maximum) and 240 edges. Limits are visible. Selecting a plotted node or its accessible numbered row opens it. Filters restrict traversal; the current note remains visible.
Related scores are deterministic: direct link +12; shared incoming source +4 each (maximum 20); shared tag +3 each (maximum 15); title/heading term +1 each (maximum 4); same non-root folder +1. Every score has an explanation. Ties sort by path. Templates are excluded from suggestions. All data is rebuilt from the existing link index and cached parsed metadata; there are no embeddings, AI calls, graph database or canonical-data migrations.
Full solution Windows/Android build: zero warnings/errors. All 181 tests pass, including graph edge direction, incoming traversal, filters, limits, invalidation, score explanations and deterministic ordering. L was verified with 179 passing tests before M. Native and physical-device acceptance remains separately reported.

## Final evolution verification — 2026-10-03

Milestones A–M are implemented across Core, API and both MAUI clients. Final dependency restore succeeded; the full solution built with zero warnings/errors; all **182 automated tests passed** (zero skipped). Tests exercise real temporary libraries, local Git remotes, HTTP authentication/receipts, restart recovery, derived indexing and renderer fixtures. An initial final build was blocked by the running audit server's file locks; stopping that isolated server allowed the clean rebuild.

A 10,000-note synthetic graph workload on this workstation measured 5,725 ms for initial link indexing and 437 ms for graph plus related-note generation during the full test run. Output stayed bounded at 40 graph nodes and eight recommendations. This is a synthetic measurement, not a mobile performance guarantee.

Windows native walkthrough used only a separate local fixture library and local bare remote: connected, opened the command palette with Ctrl+Shift+P, filtered and ran Rediscover, read its explanation, opened the result, rendered the local graph, clicked its node to open another tab, and opened the note overflow menu without a crash. The walkthrough found that drag-enabled explorer rows swallowed normal folder clicks; explicit row activation corrected that behavior. Offline download opening now skips remote link/asset metadata calls, local search runs its disk reads off the UI thread, and pending edit conflicts offer Resolve conflict directly.

Remaining qualification: no Android device/emulator was connected in this final session. Physical touch/IME, share-sheet delivery, process death/suspension, real Tailscale disconnection/reconnection, long-lived offline queue behavior, all display scales/accessibility and signed in-place update installation need device verification. Local Tesseract OCR requires the server's configured engine and is not claimed tested on this Windows machine. Production was not accessed or deployed.

Storage/migrations: canonical Markdown/library schema and asset identities are unchanged. Search schema 3 is derived/rebuildable. New workspace preferences, offline manifests/operation records and opt-in reading activity use versioned JSON beside existing drafts/cache; legacy data is retained. There is no SQLite or automatic destructive migration. Journals and replay receipts have no automatic expiry; plan disk capacity. Assets remain outside Git.

Shortcuts: Ctrl+Shift+P commands; Ctrl+N note; Ctrl+Shift+N folder; Ctrl+A in explorer selects visible rows; Ctrl/Shift click controls selection; Ctrl+B/I/K in the editor inserts emphasis/links; Ctrl+Shift+K inserts a wiki link. Server filters include `type:question status:open postgres`, `has:code`, `has:diagram`, `has:file`, `created:2026-01-01..2026-10-03`, `modified:*..2026-10-03`, `tag:` and `path:`. Offline search is plain-term matching over deliberately downloaded notes.

Intentionally excluded: full-library replication, offline structural mutations, cross-device preference synchronization, overlapping-file automatic Git merging, history-aware automatic asset cleanup, AI/embeddings and a web client. No push, tag, release publication, production deployment or signing-policy change was performed.

### Final local build handoff

The final modal adjustment bounds dialog content to the window height and lets long merge/action content scroll. Both platform targets were rebuilt after that change. Windows native folder-label expansion and collapse were also rechecked after the row-activation fix.

Local artifacts are under `artifacts/evolution-20261003/`: `windows/Slate.Lib.App.exe` is the self-contained unpackaged Release build (keep the entire sibling folder), and `android/dev.slate.lib-Signed.apk` is a standalone Debug/development-signed APK with embedded assemblies. These are local verification builds, not published production releases; a development APK does not establish compatibility with an installed production signing identity. Version and production signing configuration remain unchanged. New API-backed features require the matching backend implementation; the production backend was not updated.
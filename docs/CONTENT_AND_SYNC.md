# slate.lib — Content and synchronization contract

## Milestone A persisted contract — 2026-10-02

POST /v1/library/bulk/preview stages a request UUID, operation, selected paths and optional destination. Parent selections subsume descendants. Limits: 500 selected roots, 5,000 supported files, 64 MiB per selection; notes retain the existing 2 MiB limit. Payload transformations use parsed links, leaving code examples untouched. Copy/duplicate allocate fresh note IDs and retarget resolvable links within the copied selection. Moves preserve IDs and rebase outgoing relative links; reviewed incoming-link repair participates in the same operation journal (F).

POST /v1/library/bulk/apply requires the returned fingerprint and rechecks sources, directory inventory and destination collisions before publishing. Repeating a completed request returns its receipt. GET /v1/library/bulk/{uuid} reports prepared/completed/rolled-back state after an uncertain response. JSON schema 1 journals live under the configured Git state directory, outside canonical Markdown; standalone stores fall back to hidden .slate/operations. Applying journals recover on startup. Held originals are retained, with no automatic cleanup or promised Undo. Existing single-item routes/drafts remain compatible.


Status: implementation through evolution milestones A–M, 2026-10-03. This document owns persisted formats and consistency behavior. Unless marked later, the contract is required by the MVP. Limits are initial configurable defaults; clients obtain them from the service.

## 1. Canonical library layout

The knowledge repository root is the library root. It is separate from the application source repository.

```text
library/
  .git/
  .gitignore                 # includes /.assets/
  .gitattributes             # Markdown text normalized to LF
  .slate/library.json        # { "schemaVersion": 1, "libraryId": "<uuid>" }
  inbox/
    .gitkeep
    why-vacuum.md
  computer-science/databases/postgresql/mvcc.md
  projects/anythingtcg/overview.md
  books/
    .gitkeep
  ideas/
    .gitkeep
  questions/
    .gitkeep
  daily/2026-09-24.md
  .assets/                   # virtual in slate.lib; optional ignored local materialization
```

Only `inbox/` and `daily/` have built-in capture conventions; other sample folders are optional. They remain ordinary real folders. If a capture destination was removed/moved, the next capture recreates it and tells the user. Smart views are queries, never filesystem directories. Empty folders contain an empty `.gitkeep`; it is hidden from listings and ignored by search. Leaving that marker in a nonempty folder is harmless.

Bootstrap creates `.slate/library.json` once; its library ID is immutable, and unknown schema versions require an explicit migration. The server validates that `.gitignore` excludes `/.assets/` without excluding managed notes and that `.gitattributes` preserves the agreed Markdown text/LF policy without executable filters. External edits to these control files cannot bypass those invariants. The service fixes its own Git configuration, including disabling automatic CRLF conversion; attributes may normalize external CRLF to LF when committed. Revision/sync comparisons account for this declared line-ending normalization rather than treating a normalized blob as missing history.

Notes are UTF-8 `.md` files. App-created files use LF and one trailing newline. External imports accept UTF-8 BOM/CRLF and preserve existing source formatting except necessary metadata insertion or app edits. Git text normalization is defined by `.gitattributes`. Maximum note size is 2 MiB; larger text remains available in the external clone but is rejected for managed-library import with a diagnostic. The app does not silently truncate it.

Paths are library-relative `/`-separated Unicode NFC strings. Each segment is ≤ 100 characters and total path ≤ 220 characters. Preserve display case but require uniqueness using ordinal case-insensitive comparison of normalized paths, including folders. Reject absolute paths, empty or `.`/`..` segments in mutation targets, control characters, Windows-invalid `<>:"\\|?*`, trailing dot/space, and Windows device names such as `CON`/`NUL` even with an extension. App mutations cannot touch `.git`, `.slate`, `.assets`, `.gitignore`, or `.gitattributes`; `.gitkeep` is service-managed. Root and reserved files cannot be moved/deleted through the explorer. Spaces are allowed; suggested filenames use lowercase hyphenated titles.

Resolve link-relative `..` only within the library boundary. Decode URL components once, normalize, and reject escape, NUL, alternate data streams, symlinks/reparse points, submodules, and nonregular files. Never combine untrusted paths into a shell command. External candidates undergo the same containment/collision checks before checkout into an active tree. Small ordinary text support files may remain in Git and appear as read-only attachments, but are not indexed as notes. Reject executable modes and binary Git additions; upload binaries through the asset API instead. Repository hooks and filters supplied by content are never run.

## 2. Front matter and stable identity

Every managed note has a globally unique UUIDv4 `id` in YAML front matter. Paths are locations, not identities. IDs are stable through edits/moves/restores and are never reused for a different note. Apps generate an ID at creation, including eventual offline creation. Only the ID is required; defaults keep imported plain Markdown useful.

```markdown
---
id: 6f483c59-d4ab-4f1b-87b7-989da5fb0cef
title: Why does PostgreSQL need VACUUM?
aliases:
  - PostgreSQL VACUUM question
tags: [postgresql, databases]
type: question
status: open
created: 2026-09-24T14:20:00Z
updated: 2026-09-24T14:20:00Z
---

# Why does PostgreSQL need VACUUM?

How does it relate to [[id:972778ca-1d38-4f8a-a0fa-f19c4e3acd1a|MVCC]]?
```

| Field | Meaning and rules |
| --- | --- |
| `id` | Required UUID; changing an existing note's ID means delete + create, not rename |
| `title` | Optional display title; fallback first H1, then filename stem |
| `aliases` | Optional list of nonempty strings; duplicates within a note collapsed for lookup |
| `tags` | Optional list; trimmed Unicode strings, case-insensitive exact filter matching |
| `type` | `note` (default), `question`, `idea`, `daily`, `reference` |
| `status` | For questions: `open` (default) or `answered`; for other notes optional `learning`, `needs-review`, `done` |
| `created`, `updated` | Optional RFC 3339 UTC instants; apps populate on creation and update `updated` on content/metadata edits |
| `answered` | UTC instant required when question status is `answered`; remove when reopened |

Use a safe YAML parser with duplicate-key detection, bounded nesting/alias expansion, no custom object construction, and a 32-KiB front-matter limit. Unknown fields remain intact; clients must not deserialize and rewrite the entire document with only known fields. Invalid types/statuses/duplicate IDs produce actionable validation errors, not guessed values. Related notes are links in the body; project membership is a path prefix, not a separate entity.

Dates are optional for externally authored notes. Do not scan Git history to infer them in the MVP: unknown dates display as unknown and do not match date filters; Recently Modified sorts known `updated` dates first, then unknowns by path. App-created notes receive UTC dates; the client updates `updated` only for a user content/metadata edit and freezes the submitted bytes for any retry. External editors may maintain dates manually. Rename/move alone does not change `updated`. Daily filenames use the capturing device's local date. Deriving historical dates is a later history feature.

**Import without IDs:** when validating a fast-forward import or initial library bootstrap, plan a UUID for each otherwise valid new note lacking one. After activation, insert only the ID, preserve the body/other keys, and include the additions in the next normal batch commit. Persist the planned IDs in the small pending-operation record so recovery does not allocate different ones. Do not maintain hidden permanent IDs in a database. Malformed front matter, duplicate IDs (including copied files retaining IDs), or case collisions block import activation as a whole and show file-level diagnostics. Removing/changing an established ID is treated as identity replacement and requires an explicit operator-reviewed import; a missing ID on an existing path is not automatically assigned a new identity. The existing active library remains usable. Normalization of missing IDs is automatic; malformed data is never silently repaired. External contributors preserve IDs on moves and remove/change them when duplicating.

Stable IDs in front matter are chosen over path identity (breaks moves) and a separate identity database (loses identity on export/rebuild). User-facing titles and filenames can remain human-readable.

## 3. Links, aliases, and anchors

Support ordinary relative Markdown links and these wiki forms outside code spans/fences:

```markdown
[[CAP Theorem]]
[[CAP Theorem#Availability]]
[[computer-science/distributed/cap-theorem.md|CAP theorem]]
[[id:972778ca-1d38-4f8a-a0fa-f19c4e3acd1a#availability|Availability]]
[MVCC](../postgresql/mvcc.md)
```

The link picker generates explicit `id:` wiki links with readable labels; hand-authored title/path links remain supported. ID links keep their target when moved or renamed. Plain text editors can read the label/UUID; standard Markdown relative links remain available when generic-renderer compatibility is more important. The MVP portable copy preserves Markdown/wiki source and materializes the asset folder. Phase 4 adds export conversion of resolved wiki links into relative Markdown links; unresolved links remain readable text with an export warning. Markdown source and YAML remain independently usable without export; app-specific wiki navigation is an optional extension.

Resolution is deterministic:

1. `id:<uuid>` resolves exactly to that ID.
2. Wiki targets containing `/` or ending `.md` are explicit root-relative library paths (not relative to the source note). Match the normalized path exactly under the case-insensitive path rule.
3. Bare wiki targets match the union of display titles, filename stems, and aliases across the library, using NFC plus ordinal case-insensitive comparison and trimmed surrounding whitespace. One distinct ID resolves; zero is missing; multiple is ambiguous. There is no “nearest folder” or arbitrary first-match tie-breaker.
4. Ordinary Markdown note links resolve relative to the source note. URL-decode their path, enforce containment, then resolve the target ID. External `https`/`http` URLs are external links, not note identities.

Duplicate titles/aliases are allowed but can make text links ambiguous. Show all candidate titles/paths and let the user replace the link with an ID link. Do not create missing notes automatically; offer explicit creation in a selected folder. Broken links retain their source and show a warning on activation and in note details.

Heading anchors use one shared algorithm: extract heading plain text, NFC-normalize, lowercase invariantly, remove punctuation except hyphens/underscores, replace whitespace runs with `-`, trim hyphens, use `section` if empty. Allocate slugs in document order and append the smallest `-1`, `-2`, etc. suffix making the slug unique across all prior headings, including literal headings already ending in a number. Wiki heading text is slugified by this algorithm; explicit generated links use the emitted anchor. Heading edits can break anchors; note identity does not give headings their own IDs. Missing heading opens the correct note with “Heading no longer exists”. Renderer and resolver tests must use identical fixtures.

Backlinks derive from successfully resolved note links, including links in front-matter-independent Markdown bodies; ignore code examples and external URLs. Record source ID and optional heading/preview. Ambiguous/broken links are separate diagnostics, not edges. Orphans have no incoming resolved links from another note; self-links do not count. Metadata/name/path changes re-resolve affected text links even when the linking source file did not change.

## 4. Assets and portable references

An uploaded asset has a UUIDv4 identity and immutable bytes. Store it physically in `/srv/slate/assets/objects/<uuid>.<ext>` with metadata in `/srv/slate/assets/metadata/<uuid>.json`. Never place these bytes in the Git working tree. The extension is a safe lowercase type-derived extension or `.bin`, not an arbitrary upload filename. Store original filename separately.

Use ordinary Markdown **relative paths to the reserved virtual root `.assets/`**, for example in `computer-science/databases/postgresql/mvcc.md`:

```markdown
![Tuple visibility example](../../../.assets/096520ea-c403-42e8-b3fc-5aabb3467bca.png)
[Paper](../../../.assets/65d35820-3ffb-4b72-b087-c4cb1d374296.pdf)
```

The UUID basename is stable; directory-depth prefixes are computed for the source note. After normal relative resolution, paths rooted at `.assets/` map to the asset API by ID, not to the server Git filesystem. The renderer does not blindly request that relative URL. Asset references contain no host, credentials, or absolute server path. A portable copy materializes `.assets/` beside the exported notes; ordinary editors then open the relative image paths. The full-library export layout is identical to the knowledge checkout with an ignored `.assets/` folder populated. Assets must be restored separately when cloning Git alone.

This is chosen over host-specific URLs, a custom `asset://` scheme, Git LFS, or embedding large binaries in Git. An external Git move must adjust relative links just as ordinary Markdown requires; the importer reports stale paths and can suggest repair, but does not invent targets from malformed references. App moves rewrite relative references.

Upload flow: the client allocates an asset UUID → stream to staging with a default 25-MiB limit → verify size, media type, SHA-256 and safe image dimensions (≤ 40 megapixels) → flush/rename the bytes into their immutable destination → atomically publish metadata as the availability marker → return asset ID and relative-reference information. Only then insert/save a note reference. Interrupted uploads are not visible without metadata. Retry uses the same asset ID and expected hash: identical existing bytes return the same asset, differing bytes return `409`. A crash after byte publication leaves an incomplete asset that a matching retry can finish; it cannot replace an existing published asset. Inline display allows PNG/JPEG/WebP/GIF; SVG/HTML and unknown types download as attachments with `nosniff`, never active rendered content. PDFs use platform open/download in the MVP. Every download requires device authorization, including range requests.

Pasting an image while offline keeps a durable local draft attachment and a local pending marker until upload assigns the type-derived extension. The next save retries the same asset UUID and SHA-256; capture drafts replay automatically after reconnect, while note drafts retry when saved. Metadata failure cannot return a successful upload. A missing asset shows alt text plus “Image unavailable” while the rest of the note remains readable.

**Phase 3C implementation note:** `Data:AssetsPath` must resolve outside the Library root. Objects and JSON sidecars are written under `objects/` and `metadata/`; staging is on the same configured store. PNG/JPEG/WebP/GIF are inline, PDFs use the platform opener, and SVG/HTML/unknown input is served as a generic download. No automatic deletion runs in this phase, so partially referenced/orphaned immutable objects remain recoverable until a later reviewed garbage-collection policy.

Deleting a note does not delete its assets. V1 has no automatic asset garbage collection, because older Git revisions may still reference them. The implemented cleanup tool audits current-library references and explicitly warns that history or other devices may still need the bytes. It requires a reviewed deletion and minimum age; it is not history-aware garbage collection. Assets remain part of backups.

## 5. Revisions and minimal API

A note's UUID is identity. Its path is location. Its opaque strong ETag hashes the length-delimited ID, path and exact accepted source bytes. It is independent of Git/index state and changes on content/path changes. Returning to identical state may return the same ETag; this is acceptable optimistic concurrency, not a content-addressed note system. A folder ETag hashes sorted descendant paths/content hashes and markers, including preserved non-note files.

The service also exposes a **disposable library version**: a random process-start token plus an in-memory counter incremented after each accepted mutation/import. It need not be stored or replayed. A different version means visible content should be revalidated; it is not a durable sequence/cursor or a list of every change.

| `/v1` operation | Contract |
| --- | --- |
| Status/capabilities | Library ID/version, disposable search version, limits, indexing state, Git state, backup last result |
| List folder | Path/sort/page, ≤ 100 entries; current generation and item IDs/paths/ETags |
| Read note | ID → exact source, path, ETag; supports conditional reads |
| Create note | Client UUID, destination, complete source; ID and path must be unused |
| Update note | ID, full source, `If-Match`; preserve ID; return ETag |
| Create folder | Destination; existing directory is a harmless already-exists result; file collision fails |
| Rename/move/delete | ID/path plus note/subtree `If-Match`; explicit recursive delete; no overwrite |
| Search/quick open | Terms and supported filters; ≤ 50 hits/page with indexed revision, title/path/excerpt |
| Assets | Upload with client UUID/hash; authorized read/metadata by UUID |

Routes can use ordinary REST spelling; there is no generic command endpoint. Missing precondition returns `428`, stale ETag `412`, ID/path collision `409`, invalid source `422`, oversize `413`, and unavailable storage/recovery `503`. Missing/deleted note returns `404`. An update never recreates it. Errors identify the problem without exposing server paths/secrets. Pages include generation; if it changes, clients restart the page rather than maintaining server snapshot sessions.

**No durable request-receipt database in the MVP.** Freeze each submitted payload and allow only one request at a time per editor. If the response is lost, GET current state before any retry. A create with the same UUID/path/source, an update with the same current source/path, or a move with the expected ID at its destination is already satisfied. A missing deleted ID is already absent. Otherwise preserve the draft, show the current state and require review; never infer success from an unrelated file at the same path. ETags prevent a stale retry from overwriting intervening edits. Exact duplicate create requests may return the current matching note; any mismatch is a collision. An offline operation queue may justify durable receipts in phase 5, but is not implemented now.

## 6. Filesystem writes and recovery

The service is the only writer to its checkout. Keep an in-memory ID/path map, reconstructed from source at startup and updated synchronously under the writer lock. Reads/listings take that same lock briefly to capture consistent bytes/metadata. Search extracts from such snapshots; it cannot authorize writes or supply current state for link rewriting.

**Ordinary create/edit:** validate ID/path/source and compare ETag under the lock; write a unique sibling temporary file, flush it, atomically rename/replace it on the same filesystem, and flush the parent directory. Update the map/version, wake indexing/Git, then acknowledge. On Linux the implementation must actually support the required file/directory flushes; test on the deployment filesystem. A crash leaves either the old or complete new file. Temporary filenames use the reserved `.slate-tmp-<uuid>` pattern; exclude them from listings/Git staging and reject that pattern in external imports. Leftover unpromoted temporaries are ignored/cleaned on startup. No per-save before/after journal, durable event, or receipt is required. A no-op save leaves dates, revision and Git history unchanged.

**Saved** means source is durable on the home-server disk, not that GitHub or backup has it. Local disk/permission/flush errors never produce a successful save acknowledgement. Clients keep their draft until success or read-back verification. A crash before the response can leave a successful write; section 5 handles that uncertainty.

Creating a folder acknowledges only after its directory and `.gitkeep` are durable; retries may finish an empty markerless directory left by interruption.

**Multi-file operations:** retain one small filesystem-specific pending-operation directory under `state/pending/`, with source/destination paths, expected pre-state, complete needed post-state payloads, and operation kind. Flush payloads/manifest before the first mutation. Apply replacements/removals under the writer lock, flush them, then mark completion durably before accepting another operation. On restart, finish a prepared operation idempotently before serving content; a completed marker permits cleanup, never reapplication over later writes. If recovery cannot finish, remain unavailable for mutation/affected reads with an operator diagnostic. This exists for folder moves/link changes/import normalization, not every save; do not turn it into a generic transaction or event framework.

A crash can complete an operation whose caller saw no response. Read back its target state before trying it again. Keep cancellation available only before durable prepare. Do not expose half-moved trees. Delete completed payloads once recovery is no longer needed; Git holds checkpointed historical versions; the pending operation is only for crash recovery.

### File operations

- **Create:** offer a suggested filename; never replace an existing different note. Daily Note opens/creates `daily/YYYY-MM-DD.md`; a concurrent create opens the winning note after validation.
- **Edit:** preserve ID and unknown metadata. Title is separate from filename. A title edit adds the prior display title to aliases; avoid changing body/H1 implicitly. Preview displays the draft, not another canonical representation.
- **Rename/move a note:** preserve ID, add the old filename stem as an alias if changed, and adjust parsed relative links inside the moved note to preserve their existing targets. This includes relative asset URLs. Read current source under the lock; code/prose matches are not links. The renamed source and path change share the pending operation.
- **Move a folder:** preserve descendant IDs; adjust relative links inside moved notes using the complete old→new path mapping. Links between notes that both moved remain relative to their mapped targets. Validate the subtree ETag and all destination collisions before preparing. Reject moving into itself/descendants. Case-only renames use an intermediate name where necessary.
- **Incoming links:** ID links remain valid. Bare title/alias links remain valid while their names stay unique. **MVP does not rewrite path links in other notes.** Before a move, show known affected incoming path links and explain that they may need repair; a check against current source must not silently use a stale search graph. Revalidate source/subtree revision when executing. No redirects/path history table. Phase 4 adds an optional reviewed repair of resolved incoming Markdown/wiki path links. External moves follow the same ordinary Markdown limitation.
- **Delete:** confirm the file or folder/descendant count. Acquire the Git gate, then writer lock; checkpoint currently accepted content locally before removal, including a new note that has never been batched. If checkpoint fails, deletion fails without removing source. Apply deletion using a pending operation where multiple files are involved. Git batching records the deletion later. Incoming links become broken; assets remain. Immediate trash/Undo is not promised; the in-app history browser restores a new revision or recovers a deleted note (K).
- **Copy/duplicate (phase 4):** fresh UUIDs, noncolliding `-copy` suffixes, new created/updated dates, no inherited aliases by default. Preserve other content/metadata; remap links among a copied subtree, share immutable assets and links to notes outside it.
- **Cut/paste/multi-select (phase 4):** delayed move with captured source revisions, all selected items validated before application. No overwrite mode or cross-library operation in this specification.

Example: moving `databases/postgresql/mvcc.md` to `computer-science/databases/postgresql/mvcc.md` keeps its UUID and `[[PostgreSQL MVCC]]` resolution, and adjusts that source's asset prefix from `../../.assets/` to `../../../.assets/`. `[[PostgreSQL MVCC#Vacuum]]` still opens the same unchanged heading. `[MVCC](databases/postgresql/mvcc.md)` from another note requires repair; the UI must not promise otherwise. Deleting the destination leaves a readable broken link, not a new target selected arbitrarily.

## 7. Git that stays in the background

**Phase 3A implementation note:** local Git history is explicit (`--initialize-git`) and remains optional. The Git CLI is invoked without a shell behind one process-local gate. Managed changes batch after the configured quiet/max intervals and checkpoint before deletion, explicit Sync and shutdown. Sync fetches before push, permits only validated fast-forward activation, persists the last accepted remote head, and records old/new heads before activation. A remote outage retains local commits; divergence or a remote rewrite sets Needs attention and leaves both histories and the active files intact. History endpoints are read-only and never accept paths or arbitrary Git arguments.

One private remote, one branch (`main` initially), one service-owned checkout. Git commands run through one Git gate, with argument arrays, controlled configuration, disabled hooks/filters, verified SSH host, no shell interpolation and bounded network timeouts. Commands needing both gates always take Git gate before writer. Ordinary saves take writer only; Git network work never holds writer. No process outside the service edits this checkout while running.

### Commit and sync policy

- Commit after 30 seconds without an accepted change, capped at 2 minutes from the oldest pending change. Also checkpoint before an import, delete, operator maintenance, Sync now, or clean shutdown when possible. No-op saves create no commit; intermediate keystrokes/autosaves need not appear individually in history.
- Stage validated managed files/markers only. During normal running, changes inconsistent with the live map stop automation for diagnosis. At startup, parse/validate surviving uncommitted Markdown as recoverable local edits; without an audit log the service cannot distinguish those bytes from a stopped-service manual edit. Invalid content/unsupported entries still block automation.
- Sync polls every 60 seconds while healthy and runs after a batch or Sync now. **Fetch before every push.** Check for remote-history rewrites against the last accepted remote head before using the normal ancestry rules.
- Persist the last accepted remote head after a successful import/push; initialize it from the configured remote on first bootstrap. A failed status-record write triggers a conservative refetch/recheck, never a forced baseline reset.
- Equal heads: synced. Remote is an ancestor of local: push a captured local commit normally. Local is an ancestor of remote: validate and fast-forward import. Diverged or unrelated heads: pause Git integration/push for operator resolution, even if different files changed. Never force-push or automatically merge/rebase divergent history.
- Failed/rejected push leaves content and commits intact. Retry from fetch, with backoff 30 seconds up to 15 minutes. GitHub outage never blocks ordinary atomic saves or indexing. Local commits continue if Git is healthy.

This intentionally chooses predictable fast-forward behavior over a custom distributed Git merge engine. The owner should pull/sync before external editing and avoid editing simultaneously through apps when convenient; correctness does not depend on remembering to do so. Git's fast-forward-only behavior provides the primitive. [Git merge documentation](https://git-scm.com/docs/git-merge).

### Validated external import

1. Fetch objects/remote refs under the Git gate without the writer lock. Inspect the target tree/blobs for safe paths, valid schema/IDs and accepted file types before checking anything out. Missing IDs on new notes get a normalization plan; malformed/duplicate IDs and case collisions block the entire import. Broken links/missing assets warn but do not reject otherwise valid Markdown.
2. Take writer lock, checkpoint pending app edits, and reevaluate ancestry. A local save may have made the heads diverge; if so, leave active files untouched and report sync needs attention. Never install a result based on an old local head.
3. For a still-valid fast-forward, record old/new commit IDs, preserve the old head under a single reusable import-safety ref (replace it only after the preceding import is complete), and durably record `state/pending/` import intent and any planned normalization payloads. Begin only from a clean checkpointed checkout. Keep live-tree readers/writers gated during local activation.
4. Apply Git's fast-forward-only update, normalize new missing IDs with the prepared payloads, flush changed files, refresh the library map/version, and mark the import complete. Normalization is ordinary pending local content for the next commit. Notify the index of changed/deleted IDs. Only then serve the new tree.
5. If Git update fails or the process stops mid-activation, the pending import record prevents normal startup over partial state. If HEAD/tree match the expected old or new snapshot, finish the corresponding known step and normalization. Otherwise require operator recovery from the preserved commit/backup while writes stay blocked. Do not run an unattended hard reset against unknown state. This is a rare safe stop, not a promise of automatic recovery from every Git failure.

No candidate worktrees, semantic merge engine, server conflict database or in-app Git merge screen in the MVP. Normal valid external pushes still import automatically. The service may inspect Git blobs without materializing another full checkout.

### Divergence and repository failure runbook

On divergence, show **Saved locally; Git sync needs attention**, with heads/reason in operator details. Ordinary saves/local commits continue until the owner chooses maintenance. Preserve both refs; do not put conflict markers into the active tree.

To resolve: enter maintenance (clients retain new drafts), checkpoint and back up the server checkout, export its branch in a Git bundle, fetch that bundle into a separate operator clone alongside GitHub, merge with ordinary Git/VS Code and explicitly resolve conflicts, validate the resulting Markdown, then push a merge commit containing both frozen server and remote heads. Resume service sync: the server can now validate and fast-forward. If either head advanced, recheck rather than discard it; remain in maintenance or repeat with a fresh bundle. A remote rewind requires an explicit operator-approved new baseline after preserving both histories. The service never chooses that baseline itself.

If `.git` is damaged, retain the working tree/assets and make an untouched backup before running operator `git fsck`/repair or restoring/cloning into a separate directory. Recover local-only commits from backup/bundle when possible, overlay newer uncommitted Markdown only after review, validate, then replace the stopped service's checkout. Never delete/reclone over the only copy of local content. Disk corruption/failed recovery stops writes; a Git-only failure may allow ordinary disk saves but blocks sync and deletion checkpoints. Report these states separately.

### User-visible status

`Saved` refers to the acknowledged source revision. Index pending/Indexed is based on that revision's index entry. Global History pending/Committed locally and Sync pending/Synced/Needs attention describe the current library; show Synced only when there are no uncommitted files and the current local head is known to be on the remote. A push of an older captured head does not mark newer local work synced. Do not keep a per-autosave ledger mapping revisions to Git commits.

## 8. Incremental indexing and client refresh

**Phase 3A implementation note:** the Lucene index stores UUID, revision, boosted title/aliases/headings, filename/path terms, plain Markdown text, and exact tags/type/status. Results return only title, path, score, revision and a bounded plain-text snippet. Normal text, quoted phrases, `title:`, `path:` and exact `tag:` are implemented with the documented length/clause limits; raw Lucene syntax is never accepted. Create/update index one UUID directly. Structural mutations and Git imports reconcile current UUID/revision/path records, which updates/deletes only changed documents. Startup performs the same reconciliation; explicit `--rebuild-index` deletes and recreates derived search state.

Add/update note: parse current source and replace that ID's Lucene document. Move/rename: update path/aliases and rewritten source for the same ID. Folder move: update affected descendants. Delete: remove the ID and its outgoing edges; recompute incoming-link resolution. Use Git tree blob differences for imports and source/index revision comparison after restart. In-memory dirty IDs may be lost because the filesystem can reconstruct the work; no durable queue.

An index worker parses a consistent source/revision snapshot outside the writer lock, then briefly takes the writer lock for the final revision check and index update/publication together. If changed/deleted meanwhile, discard stale work and reschedule; old work must never resurrect a deleted document. Backlinks/ambiguous/broken links are derived. Re-resolve cached link targets when names/aliases/paths/IDs/headings change, including previously unresolved ones. Initially scanning extracted link tokens in memory is adequate; do not reparse every unchanged body or prebuild a reverse-dependency framework.

On restart, scan note identities/revisions to rebuild the live map, compare indexed revisions and only reindex mismatches. Missing/corrupt index means search temporarily unavailable while rebuilt; folder/ID reads and writes continue. Keep dirty IDs during rebuilding and verify revisions before publishing. Unexpected live-tree drift raises a warning; direct server-tree editing is unsupported. A full reindex is for missing/corrupt/schema-changed derived data or explicit rebuild, not each save.

Foreground clients poll the small status endpoint every 15 seconds and on resume. A library-version change triggers conditional refetch of the open note and visible folder page. Also expose a disposable search version incremented when indexed results are published; refresh the current query on either version change, including completion of previously delayed indexing. Other cached notes revalidate when opened. No durable change feed, retained tombstones, cursor expiry or full-library metadata download. A dirty editor keeps its base ETag/draft when new content appears. A deleted target shows missing and offers draft export/save-as-new; never automatically resurrect it. Server restart simply changes the version and triggers refresh.

### Search grammar

MVP: whitespace clauses ANDed; quoted analyzed phrases; plain terms across boosted title/alias/heading/path/body fields. Support `title:"consistent hashing"`, exact case-insensitive `tag:kubernetes`, `path:databases` (case-insensitive substring of normalized path), and `is:unanswered` (question with open/default status). Path substring semantics are deliberately simple; `path:projects/anythingtcg/` scopes a project. Quick open searches title/alias/path only. Unknown filters/unclosed quotes return a helpful error. Limit queries to 512 characters/32 clauses and escape user terms before constructing Lucene queries.

Phase 4 adds `type:question`, `status:learning`, `has:diagram`, `has:code`, `after:2026-01-01` and `before:2026-02-01`. `after` means updated at/after the next UTC midnight following that date; `before` means before the start of that date. Unknown dates do not match. Mermaid fences set diagram; other fenced/indented code sets code. These fields may be indexed earlier, but their filter UI is not an MVP gate. No raw Lucene syntax, regex, leading wildcards, fuzzy grammar, semantic search or LLM dependency.

## 9. Client state and deliberate offline work

**Phase 3B implementation note:** both clients use one `ClientStateStore` with atomic temporary-file-plus-replace JSON writes. Drafts live under app data partitioned by library UUID; cached notes live under disposable cache storage. Existing-note records retain ID/path, local source, base source and base ETag. New-note records retain a client UUID/target path. Quick Thought and Share records additionally retain the frozen `CaptureNoteRequest`; retry deletes them only after an acknowledged response. The preferences record contains at most 100 recent note IDs. Oldest cached note files are evicted above 500 MiB on Windows or 100 MiB on Android. Share handoff records use a small pre-library app-data staging directory and are promoted to normal capture drafts once a known library is available.

The server's `POST /v1/captures` accepts one client capture UUID plus bounded text/optional comment. It permits only `quick-thought` or `share`, chooses the canonical `inbox/` destination itself, generates a collision-safe date/slug/UUID filename, and writes ordinary Markdown with required UUID front matter. Repeating the same UUID and content returns the same note; changing content for an existing UUID conflicts. Capture uses the ordinary mutation hook, so search publication and Git pending behavior match any note creation.

Both clients use app-private files, partitioned by library ID. Store a small preferences JSON (server address, recents and favorites/pins); one atomic JSON record per cached note containing ID/path/source/ETag/fetched time; and a separate durable JSON draft containing local text, base text/ETag, desired path and pending attachment IDs. Credential tokens stay in secure storage. Note source in these files is a cache/draft, never an alternate server authority. No SQLite or local search index initially.

Use platform cache storage for disposable note/image records; app-data storage for drafts and staged share images. Publish attachment bytes before atomically replacing the draft record referencing them. On restart, keep referenced draft files and remove abandoned temporaries only when safe. Persist local drafts after 1 second idle and best-effort on lifecycle changes; acknowledge local persistence only after flush/atomic replacement. A kill may lose the last unflushed keystrokes, never an acknowledged local draft. Back/exit waits for local draft persistence or offers explicit discard.

Simple oldest-accessed cache eviction: Windows 500 MiB, Android 100 MiB. A bounded file scan is sufficient; no cache database/retained folder replica. The OS may evict cache files, so offline coverage is best effort. Drafts/base versions/attachments are protected outside this cap and never evicted. Report full storage instead. MVP preferences are device-local; keep at most 100 recent IDs. Folder listings are memory-only and require connectivity after restart; cached recent notes can still be opened offline.

Offline availability is deliberate: downloaded notes/assets, visible quota, and plain-term local search. Creates, edits and captures persist operation snapshots and upload dependencies before sending. A library-identity check precedes background reconciliation; conflicts retain drafts and require explicit base/local/current review. Structural operations require the server.

Versioned atomic JSON provides the current offline manifest and one record per queued operation. Original bases and request IDs are preserved. Server preparation/result receipts support retries after lost responses. Completed receipts are retained without automatic expiry; operators must budget storage. No SQLite migration, whole-library clone, structural offline replay, CRDT or last-writer-wins behavior is introduced.

## 10. Required failure examples

| Scenario | Outcome |
| --- | --- |
| Two devices edit ETag A; Windows saves B | Android receives `412`; draft survives and can be saved as a new note |
| GitHub unavailable | Source saves/search work; local commits accumulate and later fetch/push resumes |
| Response lost after atomic save | Read back ID/source/path before retry; no duplicate create or silent overwrite |
| Crash during folder move | Pending operation completes before content is served, or service reports recovery needed |
| Server and external clone both commit | Heads diverge; sync pauses for ordinary Git resolution; source is preserved |
| Interrupted Git activation | Preserved old/new heads and pending record prevent serving a partial tree; operator recovery if needed |
| Duplicate IDs/case collisions from Git | Entire import blocked with paths/reason; prior library remains usable |
| New note deleted before batching | Pre-delete commit preserves it; failed checkpoint prevents deletion |
| Upload succeeds but note save fails | Draft retains immutable asset ID; asset remains available/backed up |
| Client resumes after long absence | Revalidate current/open IDs; preserve drafts; no cursor history required |
| Index deleted or stale job finishes late | Rebuild or discard outdated result; reads/writes do not depend on index correctness |

Milestone B: app-data libraries/<library-uuid>/workspace-preferences.json uses schemaVersion 1 with favorites (UUID/title), pins (canonical path/label), searches (reserved for saved-query implementation) and legacyBookmarksMigrated. Legacy server-keyed MAUI bookmarks are imported once; originals, preferences.json recents and all drafts/caches stay intact. Explicit folder moves remap nested pins; external moves leave a recoverable missing pin. Limits: 1,000 favorites, 200 pins, 100 saved-query slots.

Milestone C metadata: questions use type: question / status: open; Answer changes status to answered and records answered/updated UTC timestamps, preserving created and unknown fields. Link capture uses type: link with an ordinary HTTP(S) URL. Templates retain unknown front matter, allocate a new UUID and new created/updated timestamps, and expand {{title}}/{{date}} only in the body. POST /v1/workflows/daily accepts a device-local DateOnly and returns the same note under concurrent creation. Existing Daily casing is retained for compatibility. Answer and saved-capture append endpoints recheck revisions; source captures are never deleted. DraftRecord adds nullable ReadyToSubmit: false protects unsubmitted new captures, null preserves earlier acknowledged retry behavior. A Share batch accepts eight files, 25 MiB each, 64 MiB total; copied bytes survive interrupted batches and are reviewed before publication.

Milestone D implemented (2026-10-02): GET /v1/views/{unanswered|modified|orphans|diagrams|code|learning|review} uses normal paged SearchPage results. Unanswered means type question and status open/absent. Learning statuses: learning, currently-learning, in-progress; review statuses: review, needs-review. Code means a parsed fenced block; diagram means a Mermaid fence. Orphans exclude templates/ and self-links. Recently Modified uses updated, then modified, then created metadata; unknown dates sort last. Calendar dates use UTC midnight; timestamps require an explicit offset. SavedSearch records live in library-scoped preferences schema 1; up to 100 queries, 80-character names and 512-character queries.

Current scope: milestones A–M are implemented. Dated phase/milestone records below are historical checkpoints, not statements that later completed features are still deferred. ROADMAP contains current verification and device limitations.


Milestone E implemented (2026-10-02): type: and status: are case-insensitive exact metadata values. date: aliases created:. created:/modified: accept YYYY-MM-DD, start..end, *..end or start..*; * alone matches known dates. modified falls back to created when no modified/updated value exists. has: supports image, file (asset attachment), code, diagram, backlinks, links and question. Clauses combine with AND. Quotes group phrases. Unknown/invalid filters return readable query errors. Calendar dates and timestamps with explicit offsets are trustworthy; malformed/custom date metadata stays preserved and unindexed.


Milestone F implemented (2026-10-02): BulkPreview includes optional source-revision-bound Repairs, and BulkApplyRequest requires explicit RepairIncoming=true to apply them. Limits: 100 affected incoming notes, 16 MiB of before/after source, 5,000 replacements per note. Approved repair sources are revalidated before any move occurs. Original bytes are retained. GET /v1/links/issues pages missing/ambiguous/missing-heading links. Wiki conversion preserves labels/fragments, excludes code and unresolved/ambiguous targets, and revalidates current source and target-derived output on apply. Markdown edges now contribute to orphan/has:backlinks membership, superseding the earlier wiki-only limitation.


Milestone G implemented (2026-10-02): > [!NOTE]- Title starts a collapsed callout and > [!NOTE]+ Title starts an expanded callout; other existing callout kinds work too. Body content remains ordinary quoted Markdown. Only parsed markers generate details/summary HTML; arbitrary source HTML remains disabled. Heading IDs use existing duplicate-aware slugs. Outline is bounded to the first 1,024 headings. Scroll/disclosure state is session-local derived state, not note metadata.


Milestone H — attachment lifecycle

Metadata, objects and cleanup receipts remain outside Markdown Git. Cleanup retains a tombstone/metadata receipt to reject reuse of removed asset UUIDs and recover lost acknowledgements; deletion never happens automatically. Current source references include reference-style Markdown and conservative literal asset IDs, including code examples. Invalid/unreadable Markdown aborts cleanup. Derived thumbnails/text live under Data:DerivedPath/attachments and are rebuildable. Thumbnails use an independent 128 MiB bounded cache. PDF extraction is limited to 25 MiB, 100 pages and 500,000 characters; images to 16 megapixels and 8192 pixels per side. Original bytes and hashes remain unchanged.

Milestone I — download and queue boundaries

Existing JSON drafts/cache/preferences remain readable in place; they are not implicitly converted into deliberately downloaded content. Legacy capture replay skips drafts owned by the new queue, including cancelled work. Offline selections are schema 1, at most 200; each folder download is bounded to 2000 notes/directories and 25 MiB per attachment. Default quota is 256 MiB, configurable from 16 to 2048 MiB. Downloads verify attachment size/hash, reserve disk budget before publication, and retain prior downloads after failure. Quota never evicts drafts or pending-upload siblings. Search is deterministic plain-term matching across downloaded titles, paths and source; server Lucene filter syntax remains online. Receipt metadata is retained for recovery; no automatic pruning of pending work.

Milestone J — preservation rules

Merge work retains original local draft/base data and attachment dependencies before review. Recovered drafts now restore BaseMarkdown together with BaseRevision. Resolved queued edit conflicts are explicitly superseded only after a revalidated merged save, retaining the original request record. Historical divergence is separate from text conflicts: controlled history merge accepts different-file changes only and validates combined IDs, paths and library identity. Overlapping-file divergence remains an actionable manual-recovery state.

Milestone K — recovery boundaries

Restore never resets HEAD or rewrites Git history. Identical-source restoration can be a no-op. Ordinary current restore preserves the note identity; restore-as-new assigns a new one; deleted recovery preserves the old identity only when it is absent. Relative asset/note links are adjusted when the destination differs. History lists the latest 100 path-following versions; recovery scans up to 100 deletion commits/200 deleted paths and labels truncated results. Older history remains in Git even when outside the bounded UI.

Milestone L — rediscovery (2026-10-02)
Both clients expose Rediscover with random notes, older ideas/open questions, Something Forgotten, On This Day, explicit learning states, and a dated timeline. Results explain their eligibility. Unknown dates are omitted from date-driven views; templates are excluded. Date-based ordering is deterministic; Random Note is deliberately random. Results are paged in groups of 20 from cached parsed metadata.
Reading history is opt-in and device-local: UUID plus last-opened timestamp, at most 1,000 entries retained for 180 days, never uploaded. Privacy controls enable/disable and clear it. Forgotten results exclude recently opened notes when tracking is enabled; when disabled, only explicit note age is used. No reading activity before opt-in is implied. No canonical metadata migration or production changes.

Milestone M — graph and related notes (2026-10-02)
Windows commands and Android note actions expose a current-note graph and up to eight related notes. Graph traversal is bidirectional over resolved wiki/Markdown links, with directed edges displayed, depths 1–3, folder/type filters, 40 client nodes (60 API maximum) and 240 edges. Limits are visible. Selecting a plotted node or its accessible numbered row opens it. Filters restrict traversal; the current note remains visible.
Related scores are deterministic: direct link +12; shared incoming source +4 each (maximum 20); shared tag +3 each (maximum 15); title/heading term +1 each (maximum 4); same non-root folder +1. Every score has an explanation. Ties sort by path. Templates are excluded from suggestions. All data is rebuilt from the existing link index and cached parsed metadata; there are no embeddings, AI calls, graph database or canonical-data migrations.
Full solution Windows/Android build: zero warnings/errors. All 181 tests pass, including graph edge direction, incoming traversal, filters, limits, invalidation, score explanations and deterministic ordering. L was verified with 179 passing tests before M. Native and physical-device acceptance remains separately reported.

# Slate MCP tools

Tool discovery is scope-filtered. `slate.admin` satisfies every policy. All returned Markdown, titles, snippets, metadata, links, and intelligence findings are untrusted user data.

Every tool returns the structured `McpResult<T>` envelope: `success`, `libraryId`, `operation`, `data`, `warnings`, `truncated`, `pagination`, `degraded`, `references`, and `error`. A reference uses the canonical UUID as identity and may also include path, title, revision, and heading. Examples below show the `arguments` object passed to `tools/call`; the MCP SDK supplies the surrounding JSON-RPC envelope.

## Read and discovery

All tools in this table require `slate.read` and are read-only.

| Tool | Purpose | Inputs and limits | Data returned | Example arguments |
|---|---|---|---|---|
| `get_library_status` | Inspect the bound canonical library and MCP capabilities. | None. | Canonical status, library UUID, note count, Git/index state, protocol and feature switches. | `{}` |
| `browse_library` | List one folder page without recursive expansion. | `path` ≤ 1,024 chars; zero-based `page` ≤ 10,000. | Canonical `FolderPage` with entries and `nextPage`. | `{"path":"Projects/Slate","page":0}` |
| `get_library_outline` | Build a bounded breadth-first structural outline. | `path` ≤ 1,024 chars; `maxDepth` 0–8; `maxItems` 1–500. | Folder/note entries and explicit truncation. | `{"path":"Projects","maxDepth":3,"maxItems":150}` |
| `search_notes` | Search with hybrid, semantic, or canonical lexical mode. | `query` ≤ 500 chars; `mode` is `hybrid`, `semantic`, or `lexical`; page 0–100; page size 1–50. | UUID, path, title, excerpt, revision, requested/effective mode, rank data and degradation reason. | `{"query":"postgres indexing","mode":"hybrid","page":0,"pageSize":20}` |
| `read_note` | Read one note by stable UUID. | `noteId`. Response is bounded by the configured upstream-response limit. | Exact canonical `LibraryNote` Markdown and metadata. | `{"noteId":"00000000-0000-0000-0000-000000000001"}` |
| `read_notes_batch` | Read a small UUID-selected batch. | `noteIds`; 1–`MaximumBatchSize` values, 20 by default. | Canonical notes and references; any missing UUID fails explicitly. | `{"noteIds":["00000000-0000-0000-0000-000000000001"]}` |
| `get_note_links` | Inspect outgoing links and backlinks. | `noteId`. | Canonical resolved, broken, and ambiguous link information. | `{"noteId":"00000000-0000-0000-0000-000000000001"}` |
| `get_related_notes` | Retrieve deterministic canonical related-note suggestions. | `noteId`. | Related notes with canonical evidence/reasons. | `{"noteId":"00000000-0000-0000-0000-000000000001"}` |
| `get_note_history` | Inspect bounded Git-backed note history without invoking Git in MCP. | `noteId`. | Canonical history metadata. | `{"noteId":"00000000-0000-0000-0000-000000000001"}` |
| `get_knowledge_graph` | Explore a bounded link graph around a note. | `noteId`; `depth` 1–4; `limit` 1–100; optional `folder` and `type`. | Canonical nodes, edges, and limit indicator. | `{"noteId":"00000000-0000-0000-0000-000000000001","depth":2,"limit":50}` |
| `get_library_views` | Read a supported canonical smart view. | `view` ≤ 50 chars; page 0–100; page size 1–50. | Canonical `SearchPage`. | `{"view":"unanswered","page":0,"pageSize":20}` |
| `get_bulk_operation_status` | Inspect a durable bulk-operation journal. | `operationId`. | Canonical bulk result/status. | `{"operationId":"00000000-0000-0000-0000-000000000002"}` |

Hybrid and semantic search automatically returns lexical results with `degraded` set when the optional intelligence service cannot be reached or returns invalid data. It does not silently label lexical results as semantic.

## Intelligence and learning

All tools in this table require `slate.analyze` and are read-only. Intelligence tools call the existing private `slate.lib.web` intelligence routes. Learning tools combine bounded canonical search, structure, related-note, and gap evidence deterministically; they do not call a second LLM or assign mastery scores.

| Tool | Purpose | Inputs and limits | Data returned | Example arguments |
|---|---|---|---|---|
| `analyze_library_health` | Return maintenance findings. | Optional `category`, `priority`, `project`; page ≥ 0; `limit` 1–100. | Existing health findings, evidence, pagination, and scan limits. | `{"category":"links","priority":"high","page":0,"limit":30}` |
| `find_knowledge_gaps` | Find thin or missing coverage. | Optional `kind`, `project`, `concept`, `importance`; page ≥ 0; `limit` 1–100. | Existing gap findings, evidence, importance, and actions. | `{"project":"Slate","importance":"high","page":0,"limit":20}` |
| `find_knowledge_issues` | Find possible contradictions and stale claims. | `scopeKind`; optional `path` or `noteId`. | Existing issue analysis with source evidence and uncertainty. | `{"scopeKind":"folder","path":"Projects/Slate"}` |
| `find_knowledge_overlap` | Find duplicate, overlapping, or fragmented material. | `scopeKind`; optional `path` or `noteId`. | Existing overlap groups and consolidation evidence. | `{"scopeKind":"library"}` |
| `find_link_opportunities` | Find missing relationships. | `scopeKind`; optional `path` or `noteId`. | Suggested source/target pairs, reasons, ambiguity, and evidence. | `{"scopeKind":"note","noteId":"00000000-0000-0000-0000-000000000001"}` |
| `get_concept_overview` | Open an existing virtual concept page. | `concept` ≤ 100 chars. | Concept identity, member notes, relationships, projects, and evidence. | `{"concept":"retrieval augmented generation"}` |
| `get_project_brain` | Retrieve a deterministic project snapshot. | Canonical project `path` ≤ 1,024 chars. | Overview evidence, decisions, risks, timeline, and recent work. | `{"path":"Projects/Slate"}` |
| `get_thought_evolution` | Trace evidence over time. | `scopeKind`; optional `topic`, `path`, or `noteId`. | Observed timeline evidence kept separate from interpretations. | `{"scopeKind":"topic","topic":"API versioning"}` |
| `triage_inbox` | Propose conservative inbox destinations. | None. | Existing triage suggestions; no notes are moved. | `{}` |
| `analyze_learning_coverage` | Summarize documented coverage for a topic. | `topic` ≤ 300 chars; `sourceLimit` 1–30. | Source-linked topics, counts, evidence signals, gaps, and uncertainty. | `{"topic":"Kubernetes networking","sourceLimit":20}` |
| `recommend_what_to_learn_next` | Recommend evidence-backed next learning actions. | `topic` ≤ 300 chars. | Prioritized topics, reasons, existing sources, and concrete activities. | `{"topic":"backend engineering"}` |
| `build_learning_path` | Order source material into a dependency-aware path. | `topic` ≤ 300 chars. | Ordered steps, purpose, and canonical references. | `{"topic":"OAuth 2.1"}` |
| `generate_self_test` | Generate deterministic source-linked questions. | `topic` ≤ 300 chars; `count` 1–12. | Questions, evidence hints, and note references; no scoring claim. | `{"topic":"binary search","count":8}` |

If the intelligence service is disabled or unavailable, intelligence tools return `intelligence_not_configured` or `intelligence_unavailable`. Canonical read tools remain available.

## Content writes

These tools require `slate.write`; `create_folder` is listed in organization because it requires `slate.organize`. The operator must also set `Mcp:EnableWrites=true`. Canonical save, Git checkpoint, and remote synchronization remain distinct states.

| Tool | Side effect and safety | Inputs and limits | Data returned | Example arguments |
|---|---|---|---|---|
| `create_note` | Creates through the canonical path and frontmatter rules; never overwrites. | `folderPath` ≤ 1,024 chars; `name` ≤ 200; optional `title` ≤ 300; Markdown ≤ configured max. | New canonical note UUID, path, revision and reference. | `{"folderPath":"Projects/Slate","name":"mcp-rollout.md","title":"MCP rollout","initialMarkdown":"# MCP rollout\n"}` |
| `update_note` | Replaces Markdown only with exact `If-Match` revision. | `noteId`, complete `markdown`, `revision` ≤ 256 chars. | Updated canonical note and new revision. | `{"noteId":"00000000-0000-0000-0000-000000000001","markdown":"# Revised\n","revision":"source-revision"}` |
| `append_to_note` | Reads then appends; fails before writing when the revision changed. | `noteId`, appended `markdown`, exact `revision`; combined body must fit configured max. | Updated note and revision. | `{"noteId":"00000000-0000-0000-0000-000000000001","markdown":"## Next step\nShip it.\n","revision":"source-revision"}` |
| `preview_note_edit` | Does not write; signs note UUID, source revision, exact proposed hash, and expiry. | `noteId`, full `proposedMarkdown` within max. | Path, revision, character/line changes, SHA-256, expiry, signed proposal token. | `{"noteId":"00000000-0000-0000-0000-000000000001","proposedMarkdown":"# Reviewed replacement\n"}` |
| `apply_note_edit` | Applies only the exact unexpired preview and still uses canonical revision checking. | `proposalToken` ≤ 4,096 chars; exact `proposedMarkdown`. | Updated canonical note and revision. | `{"proposalToken":"reviewed-token","proposedMarkdown":"# Reviewed replacement\n"}` |
| `capture_to_inbox` | Uses canonical idempotent capture flow. | Caller-generated `captureId`; `content`; optional `comment`; canonical `kind` such as `quick-thought`. | Captured note UUID, path and revision. | `{"captureId":"00000000-0000-0000-0000-000000000003","content":"Investigate WAL tuning","kind":"quick-thought"}` |
| `create_daily_note` | Uses canonical date naming and collision behavior. | ISO `date`. | Existing or newly created daily note. | `{"date":"2026-10-08"}` |
| `answer_open_question` | Uses canonical question workflow with revision protection. | `noteId`, `answer`, exact `revision`. | Updated question note and revision. | `{"noteId":"00000000-0000-0000-0000-000000000001","answer":"Because…","revision":"source-revision"}` |
| `create_learning_plan_note` | Creates a deterministic source-linked plan only when explicitly called. | `topic`, `folderPath`, `name`; same note limits as `create_note`. | New canonical plan note and revision. | `{"topic":"OAuth 2.1","folderPath":"Learning","name":"oauth-plan.md"}` |

Successful mutations send a bounded best-effort event to existing intelligence. Event failure never rolls back a committed canonical write and never triggers a full paid rebuild.

## Organization and reviewed operations

All tools in this table require `slate.organize`. They also require writes to be enabled. Broad or destructive operations use canonical preview/apply state plus a separate signed, expiring MCP review token.

| Tool | Side effect and safety | Inputs and limits | Data returned | Example arguments |
|---|---|---|---|---|
| `create_folder` | Creates through canonical path validation. | `parentPath` ≤ 1,024 chars; `name` ≤ 200. | Canonical mutation receipt. | `{"parentPath":"Projects/Slate","name":"MCP"}` |
| `rename_library_item` | Renames one note or folder canonically. | `path` ≤ 1,024 chars; `newName` ≤ 200. | Mutation receipt and resulting path. | `{"path":"Projects/Slate/Old.md","newName":"Architecture.md"}` |
| `move_library_item` | Moves one item; canonical collision and descendant checks apply. | `sourcePath`, `destinationFolderPath`, each ≤ 1,024 chars. | Mutation receipt and resulting path. | `{"sourcePath":"Inbox/Idea.md","destinationFolderPath":"Projects/Slate"}` |
| `copy_library_item` | Copies one item through canonical APIs. | `sourcePath`, `destinationFolderPath`, each ≤ 1,024 chars. | Mutation receipt and resulting path. | `{"sourcePath":"Templates/ADR.md","destinationFolderPath":"Projects/Slate"}` |
| `duplicate_library_item` | Creates and applies a canonical additive duplicate journal operation. | `path` ≤ 1,024 chars. | Durable bulk operation result. | `{"path":"Projects/Slate/Architecture.md"}` |
| `preview_bulk_operation` | Creates canonical immutable plan and signs its operation/fingerprint. Delete requires extra controls. | Caller `operationId`; `operation`; 1–`MaximumBulkItems` paths; optional destination/new name. | Canonical preview, fingerprint, expiry and signed proposal token. | `{"operationId":"00000000-0000-0000-0000-000000000002","operation":"move","paths":["Inbox/A.md"],"destinationFolderPath":"Projects/Slate"}` |
| `apply_bulk_operation` | Applies only the exact reviewed operation/fingerprint/token. | `operationId`, `fingerprint`, `proposalToken`; optional `repairIncomingLinks`. | Durable canonical operation result. | `{"operationId":"00000000-0000-0000-0000-000000000002","fingerprint":"reviewed-fingerprint","proposalToken":"reviewed-token","repairIncomingLinks":true}` |
| `restore_note` | Uses canonical Git history and restore modes. | `noteId`, `commit`, `mode`; optional revision/folder/name. | Restored canonical note UUID/path/revision. | `{"noteId":"00000000-0000-0000-0000-000000000001","commit":"abc123","mode":"copy","folder":"Archive"}` |
| `preview_link_repair` | Previews one exact source link against source and target revisions. | `sourceNoteId`, `sourceRevision`, `linkStart`, `targetNoteId`, `targetRevision`. | Canonical text preview; no write. | `{"sourceNoteId":"00000000-0000-0000-0000-000000000001","sourceRevision":"rev-a","linkStart":42,"targetNoteId":"00000000-0000-0000-0000-000000000004","targetRevision":"rev-b"}` |
| `apply_link_repair` | Applies only while both source and target revisions still match. | Same revision-bound inputs as preview. | Updated source note and revision. | `{"sourceNoteId":"00000000-0000-0000-0000-000000000001","sourceRevision":"rev-a","linkStart":42,"targetNoteId":"00000000-0000-0000-0000-000000000004","targetRevision":"rev-b"}` |

Delete exists only as `preview_bulk_operation(operation="delete")` followed by `apply_bulk_operation`. In addition to `slate.organize`, the caller must hold `slate.delete` or `slate.admin`, and the operator must set `Mcp:EnableDestructiveOperations=true`. It is disabled in the supplied configuration.

## Failure handling

| Error code | Meaning and response |
|---|---|
| `canonical_auth_missing`, `canonical_auth_failed` | Operator configuration problem; never ask the user for the internal device token through tool arguments. |
| `not_found` | UUID, path, revision object, or journal does not exist. |
| `conflict` | Destination or identity collision; inspect the folder before trying a different explicit destination. |
| `stale_revision` | Re-read and create a new proposal. Never resubmit a stale write blindly. |
| `invalid_request` | Argument, bound, proposal token, path, write switch, or destructive-policy validation failed. |
| `intelligence_not_configured`, `intelligence_unavailable` | Optional intelligence did not run; continue with canonical read/search where appropriate. |
| `canonical_unavailable`, `upstream_unavailable`, `timeout` | Dependency outage or timeout. Non-idempotent writes are not automatically retried. |
| `upstream_rate_limited` | Honor the retryable flag and back off. |
| `response_too_large` | Narrow the query, folder, graph, note batch, or analysis scope. |
| `cancelled` | Client cancelled the independent stateless request. |

Follow `pagination.nextPage` when present. If `truncated=true`, narrow the folder, graph, batch, or analysis filters. `degraded` means a useful fallback was returned. No result asserts that GitHub synchronization completed merely because a canonical Markdown mutation succeeded.

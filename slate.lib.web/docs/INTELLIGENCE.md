# Slate web intelligence architecture

## Link opportunity analysis

Smart Linking works without generation. It masks frontmatter, fenced and inline code, quotations, URLs, logs, existing Markdown links, and existing wiki links before extracting mentions. Exact titles and aliases dominate resolution; normalized titles, deterministic target headings, project locality, shared claims, authored graph paths, and co-reference are supporting signals. Ambiguous exact matches remain explicit target choices.

False-positive controls exclude templates, generic short concepts, self-links, equivalent authored targets, weak daily-note relationships, and related-but-distinct pairs without navigational evidence. Exact duplicates, near duplicates, duplicate captures, and possibly absorbed pairs defer to Knowledge Overlap. Partial overlap can retain a link opportunity with an overlap label. Knowledge Issue pairs carry a disagreement warning and require an explicit Link anyway action. Supersession evidence creates a separate Better target review and never replaces an authored link automatically.

Bounds are 180 notes per analysis, 24 mentions per note, 6 targets per mention, 18 relationship candidates per note, 24 graph neighbors, semantic retrieval for at most 24 source notes with 8 neighbors each, 15 stored suggestions per source, and 360 findings per request. A semantic neighbor must still agree with project and claim/concept evidence; vector similarity alone never creates a suggestion. Diagnostics report notes, candidates, categories, review totals, deterministic/model counts, job state, and whether a bound was reached. Model classifications are currently unnecessary; deterministic discovery remains the production fallback.

## Implemented retrieval boundary

`EmbeddingProvider` exposes query and document embedding, provider name, model, dimensions, and availability. Implementations are disabled by default, an OpenAI-compatible server adapter restricted to an explicit origin allowlist, and a normalized deterministic provider for tests.

Markdown chunking is deterministic and understands frontmatter, headings, paragraphs, lists, and fenced code. Chunks and large code sections are bounded. Metadata includes note ID, path, title, heading, ordinal, content hash, revision, tags, type, and status. Paths are metadata rather than embedding input, so moving unchanged content reuses vectors.

For document embeddings the provider receives the note title, current heading, and one bounded chunk. For queries it receives only free text after all canonical filter clauses are removed. `path:`, `tag:`, `type:`, and `status:` can filter derived rows directly; queries using canonical-only date or content-presence filters fall back to Lucene so those filters keep their existing meaning. The provider never receives tokens, server URLs, Git data, assets, unrelated notes, or browser storage. Enabling a provider does not automatically embed the library; an operator must confirm a rebuild.

The API surface is `POST /api/intelligence/search`, `POST /api/intelligence/ask`, `POST /api/intelligence/project-brain`, `POST /api/intelligence/evolution`, `GET /api/intelligence/status`, `POST /api/intelligence/events`, and confirmation-gated `POST /api/intelligence/rebuild`. Search accepts bounded page/page-size values and never sends a vector or full index to the browser. Diagnostic search may expose lexical rank, semantic rank, fused score, matched source, heading, and chunk ID. Raw vectors are never returned. Ask returns bounded Server-Sent Events for retrieval state, source metadata, text deltas, final validated text, citations, and optional provider usage.

## Boundary

The intelligence layer is optional and web-owned. The existing Slate backend remains the only canonical source of Markdown, paths, UUIDs, assets, Git history, lexical results, links, and mutations. Derived records can always be deleted and rebuilt. No generated output changes canonical content until the user reviews and explicitly applies a normal Slate mutation.

Core Slate must work when intelligence is disabled or unavailable.

## Selected derived store

Milestone 3 uses PostgreSQL 16 with the `pgvector` extension for optional derived search data.

This choice is intentionally conservative: PostgreSQL has mature migrations, transactions, filtering, operational tooling, and bounded vector similarity in one service. `pgvector` avoids a second vector database and supports metadata filters needed for library, path, project, type, status, and date scopes. The deployment will use one private service and one derived-data volume. Loss of that volume must affect only intelligence features; a rebuild repopulates it from Slate.

SQLite remains appropriate for browser-local preferences and selected offline content, but a browser database is not suitable for server-side provider secrets or shared background indexing. A dedicated graph database is unnecessary because explicit Slate graph traversal already exists and is bounded by the canonical backend.

## Provider interfaces

Provider-specific embedding code implements the bounded `EmbeddingProvider` contract described above. `GenerationProvider` independently exposes full and streamed generation, provider and model identity, availability, and input capability. Implementations are disabled, deterministic fake outside production, and an OpenAI-compatible endpoint restricted to loopback or an explicit HTTPS origin allowlist. Real providers are selected only by server configuration.

Provider keys are server-side environment/secret values. They are never returned by API responses, rendered into client bundles, stored in IndexedDB, or accepted in URLs. Notes are untrusted document data and cannot issue tool instructions or authorize mutations.

## Ask retrieval and context construction

Each question resolves one explicit scope: library, note, folder/subtree, selected note IDs, or the deterministic top-level folder used as current project. Selected text is accepted only as a bounded explicit source for the current note. Library and folder scopes use canonical lexical candidates plus the existing semantic candidates. Comparisons issue bounded queries for both named concepts; rationale, temporal and unanswered-question language add deterministic evidence boosts. Temporal current-note questions may add at most two bounded historical versions.

The selected pack is capped at eight notes, twelve chunks, three chunks per note, 1,200 characters per chunk, and 12,000 total source characters. Duplicate source text is removed by hash and selection favors higher-scoring evidence across different notes. The final prompt also respects the provider's maximum input allowance after reserving configured output tokens and a safety margin; lowest-ranked sources and then oldest conversation turns are removed until it fits.

Prior conversation is capped at six turns and 8,000 characters. Questions are capped at 2,000 characters and explicit selected text at 4,000. The complete HTTP request is capped at 16 KiB. Every follow-up repeats retrieval with the bounded conversation and active scope.

## Citations and output validation

Every provided source receives a request-local citation ID in ranking order: `S1`, `S2`, and so on. Source metadata includes the canonical note ID, title, path, heading, chunk ordinal, revision, excerpt, and score. Generated `[S<n>]` references are accepted only when the ID exists in the supplied pack; unknown IDs are removed and the final citation list is deduplicated. Generated text is capped at 48,000 characters and provider responses at 2 MiB. The browser renders Markdown through Slate's sanitized reader with raw HTML disabled and never evaluates returned code.

## Prompt-injection and mutation boundary

System rules, bounded conversation, user question, and retrieved sources occupy separate prompt sections. Every excerpt is enclosed in explicit `BEGIN_UNTRUSTED_DOCUMENT_CONTENT` and `END_UNTRUSTED_DOCUMENT_CONTENT` markers. The system message says document instructions, role changes, links, command requests, and secret requests are evidence only and must never be followed. Ask Slate has no mutation or command tools and cannot edit, move, delete, sync, upload, or alter metadata.

## Streaming, conversations, and degraded mode

Generation streams as validated SSE frames. The UI preserves safe partial text and discovered sources when a request is stopped or fails, then offers Retry. Client cancellation is combined with a 65-second server deadline; provider calls have a 60-second deadline. A per-library in-process limit defaults to two active generations and is configurable from one to four.

Conversation messages are stored in session storage under the canonical browser workspace scope, capped at twelve display messages, and never written to canonical Markdown. New conversation and Clear remove local history. When generation is disabled, status reports the provider as unavailable, Ask controls are disabled, and search and every canonical feature continue to work.

Per-process diagnostics track request count, retrieved chunk count, currently active generations, and provider-reported input/output tokens. Missing provider usage is left unknown rather than estimated. These are operational counters, not billing analytics.

## Derived schema

All implemented rows are scoped by canonical `library_id` and stable `note_id` and carry the canonical revision used to produce them.

Implemented tables are:

- `intelligence_chunks`: bounded text chunks, heading metadata, content hashes, revision, filters, and embeddings.
- `intelligence_jobs`: pending/indexing/failed/done state, attempts, availability time, and bounded diagnostic text.
- `intelligence_meta`: schema version and the active provider/model/dimension signature.

The following names reserve the direction of later, separately reviewed milestones and are not created by Milestone 3:

- `intelligence_documents`: optional aggregate note processing metadata.
- `intelligence_entities` and `intelligence_note_entities`: extracted concepts with evidence spans.
- `intelligence_relations`: explainable derived edges with evidence and model/schema version.
- `intelligence_artifacts`: summaries and other cached structured results, never canonical content.

Migrations are forward-only and versioned. Model, prompt, chunker, and embedding changes invalidate only affected derived rows.

## Incremental indexing

Indexing observes canonical revisions and processes one note at a time. Create/update schedules that note; rename/move reuses vectors by content hash; delete removes derived rows. Sync, refresh, bulk changes, restore, and confirmed rebuild queue a paged reconciliation. Reconciliation replaces current notes, prunes missing notes, and reuses unchanged vectors.

Normal reading and editing never waits for a job. UI status is Ready, Pending, Indexing, Unavailable, or Failed; retries use bounded exponential backoff and an operator can rebuild explicitly.

## Feature flags

Semantic indexing and search are disabled until an embedding provider and derived database are configured. Ask Slate and Project Brain synthesis are independently disabled until a generation provider is configured. Their read-only deterministic views remain available. OCR, daily brief, writing/rewrite features, autonomous actions, and note mutation remain absent. The client uses server-reported availability rather than assuming a provider exists.

Library Health itself requires no generation provider. It aggregates current canonical diagnostics and already-persisted derived outputs. A missing optional source produces a named partial-results message and leaves available categories usable.

## Library Health finding model

Health findings are schema-versioned and deterministic. The common record carries its source system, kind, category, priority, evidence level, canonical note IDs/paths, project and concept scope, timestamps, specialist route, review state, fingerprint, and reasons. Priority is derived from impact and evidence: unresolved canonical references, duplicate identities, missing assets, strong current conflicts, exact duplicates, and persistent index failures need attention; plausible consolidation, answers, stale material, relationships, and unused assets are worth reviewing; weak structural observations remain informational.

The endpoint bounds canonical notes to 120, link issues and assets to 80 each, derived records to 360 per source, total findings to 800, and result pages to 50. It does not trigger claim extraction, overlap comparison, semantic retrieval, or concept extraction. Source failures are isolated with settled reads so one unavailable derived table cannot hide canonical maintenance findings.

Source ownership remains explicit: canonical Link Health owns broken and ambiguous links; Knowledge Issues owns contradictions, supersession, stale claims, and possible answers; Knowledge Overlap owns duplicate, absorbed, and consolidation findings; Smart Linking owns missing and better relationships; Concepts owns derived identity review; canonical notes and assets supply the health-only structure, metadata, and asset checks; intelligence status supplies persistent job failures.

Health-only orphan detection requires no meaningful incoming authored links and no resolved outgoing authored links. Daily, Inbox, template, root README/index, explicitly standalone, and explicit reference/system notes are excluded. Empty-note checks remove frontmatter and headings first, so concise meaningful prose is not flagged. Asset checks compare canonical asset IDs and reference counts: missing targets need attention, valid unreferenced assets are worth reviewing, and incomplete stored metadata needs attention. No asset is removed automatically.

Open, Resolved, Dismissed, and Not Relevant are normalized for presentation. Knowledge Issues, Overlap, and Smart Linking retain their own review records and richer states such as Keep Separate. Only health-specific findings use the library-scoped browser store. Fingerprints include canonical identity plus revision, hashes, ranges, or asset metadata, so material evidence changes create a new review item while unrelated changes preserve dismissal.

## Project Brain retrieval and caching

The project boundary is the selected folder subtree. Discovery is metadata-first and capped at 120 folders and 2,000 entries; at most 36 candidate note bodies, 16 note histories, 80 timeline events, and 40 graph nodes are processed for one snapshot. Large projects are labeled bounded and remain searchable through the existing hybrid search with an explicit path filter.

Resume Project selects at most 16 excerpts and 16,000 source characters, with at most three excerpts per source class and two per note. It balances overview/readme, recent, architecture, decision, question, failure, risk, experiment, and important-source evidence. Recent and explicitly current evidence is preferred for Current State; older evidence remains visible in decisions and timeline. Source IDs, canonical revisions, and citation validation use the Ask Slate model.

Generated artifacts use an in-process bounded cache keyed by canonical library ID, normalized project path, selected source revisions plus latest history commits, provider, model, synthesis kind, and Project Brain schema version. Relevant canonical intelligence events clear that library's cache. Manual refresh bypasses the generated-artifact entry. The cache contains derived text and citations only and can be dropped without affecting Markdown or Git.
## Evolution of Thought

Evolution retrieval combines current canonical notes with a bounded set of Git snapshots. Topic scopes reuse lexical and semantic retrieval; note and selected-note scopes use their exact identities; folder and project scopes first enumerate a bounded canonical subtree and then rank relevant notes. The current view always comes from the newest canonical revision.

Dates are accepted only from Git commit timestamps, explicit `created`, `updated`, `modified`, `answered`, or `answered-at` metadata, or an explicit ISO-style date in note content. Path order, result order, and file names never become dates. Events without one of those authorities appear under **Date unknown**.

Normalization removes line-ending differences, frontmatter ordering noise, comments, trailing whitespace, and repeated blank lines. Very small typo-like edits are suppressed. Structural or explicit evidence produces factual event types; materially different text without explicit evidence produces **Possible shift**. Rationale is shown only when a rationale/why section or an explicit reason sentence exists; otherwise the interface says **Reason not documented.**

Generation receives only deterministic event summaries, current-view excerpts, and bounded sources inside untrusted-document delimiters. It must use the fixed evolution headings and citations. Invalid citations are removed. When generation is disabled or fails, the timeline, comparisons, current view, sources, and Ask shortcuts remain usable.

## Claims, contradictions, and stale signals

Claim extraction is deterministic first. It reads structured front matter, relevant headings, and explicit patterns such as `uses`, `no longer uses`, `requires`, `version`, `port`, `decision`, and `replaced by`. It excludes fenced code, block quotes, explicit examples, and casual prose. Stored claim identity includes note/revision, heading, normalized subject/predicate/object, project and component context, content hash, extraction method, and evidence level.

Supported findings are direct reversal, value, version, status, architecture, decision, and requirement conflict; possible answered question; and explicit supersession. Equal values, materially different service/project contexts, examples, quotations, weak wording changes, and historical/current evolution are suppressed. Age alone never makes content stale. A stale signal requires explicit supersession, a newer same-context conflict, a newer version/configuration, or a possible later answer to an open question.

Authority is visible and deterministic: explicit metadata and supersession, current revision, decision/architecture headings, then timestamps when both sources document dates. No hidden authority score or fake certainty percentage is shown. Optional model classification considers at most eight otherwise ambiguous pairs per job, two at a time, after deterministic context and temporal checks. Its strict JSON result is schema-validated and cached by library, claim hashes, provider, model, and analysis schema. Invalid output and provider failures are discarded; provider-disabled operation still reports all deterministic categories.

Review state uses stable claim-and-content fingerprints. Resolve, dismiss, and snooze metadata lives outside Markdown. A material source change produces a new fingerprint and therefore reopens only that pair; unrelated note changes do not.

## Duplicate, overlap, and consolidation signals

The overlap signature contains canonical note/revision identity, normalized content hash, note intent, trusted timestamp, project path, bounded headings and sections, extracted claim keys, and word count. Exact hash equality is deterministic. Near duplicates require multiple signals: substantial section coverage and lexical containment plus compatible structure or claims. Partial overlap requires at least one strong section/claim match while preserving distinct material.

Absorption requires most sections from the older note to occur in a newer current note and the newer note to retain additional material; chronology alone never qualifies. Fragmentation is a low-severity consolidation opportunity restricted to small, same-project notes with a focused common subject and little copied content. Templates, daily notes, index/MOC summaries, question/answer pairs, weak standard headings, quotation-like evidence, and repeated code without broader matching prose are suppressed or downranked.

Shared sections and unique sections on each side are stored directly in the finding. The merge planner copies only source-unique sections into browser-local editable state and never calls a canonical mutation. Optional provider input contains at most eight bounded headings/section excerpts per side inside untrusted-document delimiters; malformed or failed classifications are discarded.
## Entity and concept intelligence

Concept extraction is deterministic and local. It masks frontmatter bodies, fenced and inline code, block quotations, URLs, UUIDs, and common log or stack-trace lines before examining repeated technical names. Explicit titles, aliases, tags, headings, KnowledgeClaim subjects, authored links, and canonical project ancestry are stronger evidence than raw text occurrences. Generic nouns and arbitrary filenames are suppressed.

Memberships are classified as Primary, Strong, Supporting, or Mention and preserve their explainable evidence. Key-note ranking prefers dedicated notes, documented claims, decisions, questions, authored relationships, project breadth, and current dated evidence. Relationships use authored links, repeated co-reference, and canonical project co-usage. They are labeled conservatively as related, used with, part of, depends on, contrasts with, replaces, project co-usage, or co-referenced; unclear relationships remain Related.

Limits are 240 notes per bounded pass, 18 concepts per note, 12 aliases per concept, 40 relationship candidates and 20 stored relationships per concept, 36 page sources, 16 project usages, 21 graph nodes, and 2,000 concepts per response. Concept pages remain fully usable without generation. Optional future summaries must use a bounded evidence pack and citations; notes are untrusted input and cannot alter extraction behavior.

## Coverage and gap inference

Knowledge Gap Finder uses an explainable model rather than an opaque score. Breadth comes from note and project count; depth from relevant explanatory prose and substantial sources; structure from overview, architecture, rationale, operational, and recovery sections; connectivity from existing concept relationships and authored-link suggestions; activity from repeated questions; fragmentation from several small sources without a dominant explanation. High importance is reserved for broad, decision-bearing, cross-project, or repeatedly questioned concepts.

False-positive controls exclude Daily, Inbox, template, log, and system notes; strip frontmatter, code, logs, quoted material, and URLs; suppress incidental concepts and small projects; require higher evidence for framework or language names; accept a dedicated strong source or several collectively strong sources; reuse Overlap for fragmented content and Smart Linking when knowledge exists but is disconnected. Core inference is deterministic. Semantic availability is diagnostic enrichment only, and opening the workspace never invokes a remote model.

Hard bounds are 240 notes, 500 concepts, 24 members per concept, 80 projects, 40 notes per project, 12 relationships and 12 questions per concept, 24 bridge findings, 240 total findings, 50 results per page, and source-read concurrency 6. Diagnostics expose analyzed concepts and projects, finding categories, deterministic/model counts, cache hits, pending/failed jobs, bounds, semantic availability, and PostgreSQL availability.
## Inbox triage

Inbox classification is deterministic by default. Direct question syntax, explicit metadata, decision phrases, idea phrases, problem vocabulary, actionable opening phrases, experiment/learning language, and URL density are evaluated after frontmatter, fenced code, inline code, quoted prose, and stack-log lines are masked. Ties and weak evidence produce `Unknown` rather than a forced type. Related-note ranking can add a bounded semantic-neighbor signal for at most eight captures per pass; provider failure simply removes that signal.

Related-note ranking combines lexical overlap with the already-derived Overlap, Smart Link, and Concept evidence. Project and destination suggestions require either several strong related notes or one unusually strong body of evidence, use existing paths only, and retain close alternatives. Knowledge Gap matching is informational and never marks a gap addressed. Every suggested action carries its evidence fingerprint and a plain-language reason.

Opening Inbox does not invoke a generation provider. Optional model classification remains outside the core path; diagnostics report zero model classifications. Semantic-only matches can be absent in degraded mode while the deterministic workflow remains usable.

# Privacy and trust boundaries

## Smart Linking

Mention extraction, target resolution, graph discovery, claim comparison, and insertion validation use canonical notes and disposable derived data inside the Slate web deployment. Browser-local Open, Inserted, Dismissed, and Not Relevant decisions are not written into Markdown. No remote generation provider is required.

If a future optional classifier is enabled, it may receive only bounded source and target excerpts plus minimal relationship metadata. Full-library context, credentials, and canonical write access are excluded. Note text and model output remain untrusted data and are never executed.

## Semantic search data handling

Embedding and database credentials are read only by the Next.js server. They are never put in `NEXT_PUBLIC_*`, browser storage, status payloads, search responses, or logs. Provider errors become stable operational messages; request bodies and note text are not logged.

When an external provider is enabled, the exact text sent is the title, heading, and one bounded Markdown chunk for indexing, or the free-text query after supported metadata filters are removed for searching. No provider call occurs while intelligence is disabled. Non-loopback provider endpoints require HTTPS and an explicit server-side origin allowlist; redirects and browser-supplied provider URLs are rejected. Derived chunks and vectors can be deleted and rebuilt without changing notes or Git history.

Slate's Markdown library, assets, and Git history remain on the configured Slate server. Browser-local state contains the device token, drafts, recent items, layout/preferences, favorites, pins, saved searches, and later explicitly selected offline material. Browser data is scoped to the server/library identity where applicable.

The browser sends the device token only in an `Authorization` header to the same-origin proxy. The proxy accepts only configured Slate server origins and enumerated API paths. Tokens never appear in URLs, logs, analytics, or rendered HTML.

When intelligence is disabled, no note text is sent to an AI provider. When a provider-backed feature is enabled, its settings must state which operations can send bounded note excerpts or selected assets externally. Retrieval sends the smallest useful cited subset rather than the whole library. Provider credentials stay on the server.

Do not log device tokens, provider secrets, full note bodies, raw prompts containing private source text, or generated answers containing private content. Diagnostics may record counts, durations, provider/model identifiers, job state, canonical IDs/revisions, and sanitized failure categories.

Text inside notes is untrusted content, including apparent instructions. It cannot override system rules, invoke tools, authorize network access, or approve canonical mutations. AI output is a proposal or derived view until the user explicitly applies it.

Users can disable every intelligence feature, delete all derived intelligence rows, and rebuild the derived index. These actions must not delete or rewrite Markdown, assets, or Git history.

## Ask Slate data handling

Ask Slate runs only after an explicit user action. A remote generation provider receives the question, at most six bounded prior turns, system safety rules, and only the relevant source excerpts selected for that request. It does not receive the entire library, the browser token, the Slate server URL, Git credentials, provider credentials, assets, raw vectors, or unrelated notes. Source packs are capped at eight notes, twelve chunks and 12,000 source characters before the provider's own context allowance is applied.

Short conversations remain in browser session storage, scoped to the connected Slate workspace, and are capped at twelve displayed messages. Closing the browser session may remove them. They are not synced and are never appended to a note automatically. Generated answers and source excerpts can contain private library information, so they are not logged by the application.

Notes and selected text are untrusted data. Their apparent instructions remain inside document delimiters, cannot invoke tools, and cannot authorize network access or a canonical mutation. Generated Markdown is size-limited, citation-validated, sanitized, and rendered with raw HTML disabled. The status API exposes provider/model names and bounded counts only; it never returns the generation key or configured endpoint credentials.

## Project Brain data handling

Project Brain deterministic views read bounded canonical metadata, selected note bodies, and selected history through the same authenticated server route. If generation is enabled, only the bounded Project Brain source pack is sent to the configured provider. Notes outside the selected subtree are displayed separately and are not sent as project evidence. Generated project views are read-only derived data; they are never written to Markdown, Git, browser drafts, or conversation storage. Revision-aware cached synthesis is held in server memory and is invalidated by canonical intelligence events.
## Historical intelligence

Evolution of Thought reads canonical notes and their bounded Git snapshots through the configured Slate server. Historical content is sent to a generation provider only when the user requests synthesis and a provider is configured. The request contains selected excerpts, deterministic change records, canonical paths, revision identities, and trusted dates; it does not include device tokens or unrelated library history. Without generation, all evolution analysis remains deterministic on the Slate web server.

## Knowledge analysis

Deterministic claim extraction and comparison run on the Slate web server and use bounded excerpts. Derived claims and issue payloads may be stored in the configured intelligence PostgreSQL database; they remain library-isolated and can be rebuilt without touching Markdown or Git history. Review decisions are stored in the current browser workspace and are not written into notes.

Milestone 7 does not require a remote generation provider. When optional pair classification is configured, only bounded Claim A, Claim B, and minimal subject/context metadata leave the server. Full notes, credentials, embeddings, and unrelated sources are not sent. Each excerpt is explicitly delimited as untrusted data, generated output is strict-schema validated, and neither source text nor generated classifications can supply executable instructions.

## Overlap analysis

Exact, near, section, absorption, capture, and fragmentation analysis runs deterministically on bounded current-note content. Derived note signatures and overlap payloads may be stored in the library-isolated PostgreSQL intelligence database and may be deleted or rebuilt without affecting Markdown or Git. Keep Separate, Resolved, and Dismissed decisions plus manual merge drafts remain in the current browser workspace.

When an optional remote classifier is enabled, it receives only minimal note metadata and at most eight bounded relevant section excerpts from each candidate. It does not receive credentials, unrelated notes, the whole library, raw vectors, or a canonical mutation capability. Sources are delimited as untrusted data, output is schema-validated, and generated merge organization is never applied automatically.
## Concept pages

Concept extraction, identity normalization, memberships, ranking, relationships, diagnostics, and correction reviews do not require a generation provider. Derived identities may be stored in the configured PostgreSQL intelligence database and can be rebuilt from canonical notes. Browser-local merge, alias, and keep-separate reviews do not modify Markdown.

If a later optional concept overview uses remote generation, it may send only a bounded set of relevant excerpts and source metadata. It must not send the full concept corpus, raw embeddings, credentials, or unrelated notes. The current deterministic concept page performs no external enrichment and never fetches internet definitions.

## Library Health

Library Health performs deterministic aggregation on the Slate web server. Opening or refreshing it does not send note content to an embedding or generation provider. Health-specific review decisions are stored only in browser-local, library-scoped storage. Existing contradiction, overlap, and link review decisions remain in their existing browser-local stores and are merely reflected in the maintenance queue.

The feature never rewrites Markdown, inserts links, deletes assets, changes metadata, or commits Git history. Specialist workspaces retain their existing explicit review and mutation boundaries. Diagnostic responses contain bounded findings, canonical identifiers, paths, counts, and sanitized failure categories; tokens and provider credentials are excluded.

## Knowledge Gap Finder

Gap analysis is an internal-library observation performed deterministically on bounded current note evidence. It does not use the internet, compare the library with an external curriculum, or infer a person's knowledge. Opening, filtering, reviewing, or rebuilding gaps makes no remote generation request. Derived records may be stored in the configured library-isolated PostgreSQL database and can be discarded and rebuilt without changing Markdown or Git.

Review outcomes are stored in library-scoped browser storage. Create Draft produces an editable session-only template containing source links and existing questions; it is never submitted to the canonical note API automatically. Source text remains untrusted and cannot turn gap analysis or draft preview into an instruction. Any future optional classifier or grounded drafting feature must send only bounded relevant excerpts, use strict output validation, and preserve source attribution.
## Inbox analysis

Opening Inbox performs deterministic analysis against bounded canonical notes already available to the Slate server. When semantic search is configured, it may embed bounded text for at most eight captures to retrieve existing indexed neighbors; this does not call the text-generation provider. It does not automatically send capture text to a remote generation model. Capture text remains untrusted document content: phrases that resemble commands cannot trigger move, merge, delete, rename, append, save, or classification writes.

Ask Slate is invoked only by an explicit user action. That request contains the selected capture text, capped at 4,000 characters, and at most six explicitly selected related note identities. Any future optional classifier must remain opt-in and send only bounded capture text with minimal context.

# Privacy and trust boundaries

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

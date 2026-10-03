# Privacy and trust boundaries

Slate's Markdown library, assets, and Git history remain on the configured Slate server. Browser-local state contains the device token, drafts, recent items, layout/preferences, favorites, pins, saved searches, and later explicitly selected offline material. Browser data is scoped to the server/library identity where applicable.

The browser sends the device token only in an `Authorization` header to the same-origin proxy. The proxy accepts only configured Slate server origins and enumerated API paths. Tokens never appear in URLs, logs, analytics, or rendered HTML.

When intelligence is disabled, no note text is sent to an AI provider. When a provider-backed feature is enabled, its settings must state which operations can send bounded note excerpts or selected assets externally. Retrieval sends the smallest useful cited subset rather than the whole library. Provider credentials stay on the server.

Do not log device tokens, provider secrets, full note bodies, raw prompts containing private source text, or generated answers containing private content. Diagnostics may record counts, durations, provider/model identifiers, job state, canonical IDs/revisions, and sanitized failure categories.

Text inside notes is untrusted content, including apparent instructions. It cannot override system rules, invoke tools, authorize network access, or approve canonical mutations. AI output is a proposal or derived view until the user explicitly applies it.

Users can disable every intelligence feature, delete all derived intelligence rows, and rebuild the derived index. These actions must not delete or rewrite Markdown, assets, or Git history.

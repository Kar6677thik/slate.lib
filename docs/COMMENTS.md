# Reader comments and questions

Reader comments let the owner leave a question on the exact words they are reading, or on a whole note, and let an AI agent answer through MCP. The agent replies in the thread, or writes a full page and links it under the comment.

## How other products do it

| Approach | Example | What it does well | Why Slate does not copy it directly |
| --- | --- | --- | --- |
| Paragraph comments | Wattpad, Medium's early side comments | One tap per paragraph; a count bubble in the margin makes busy passages visible | A word or a short phrase is often the actual question, so whole paragraphs are too coarse |
| Inline highlight + responses | Medium highlights, Kindle notes | Precise passage, quoted back in the response | Medium responses are public posts; Kindle notes do not hold a conversation |
| Anchored threads in an editor | Google Docs, Notion | Threads with replies and resolve | Anchors are invisible markers in the document model; Markdown has no such model, and putting markers in Markdown pollutes notes |
| Inline markup in the text | CriticMarkup `{>> … <<}`, Obsidian `%% … %%` | Plain text, travels with the file | Every question rewrites the note, conflicts with editing, clutters reading, and makes history noisy |
| Standoff web annotation | Hypothesis, W3C Web Annotation Data Model | The document is never changed; anchors are quotes with context and survive edits elsewhere | Needs re-anchoring logic, which Slate now has |

Slate uses the standoff approach with the W3C **TextQuoteSelector**: the selected text (`exact`), up to 32 characters before it (`prefix`), and up to 32 after it (`suffix`). It reads like the Wattpad and Medium patterns (highlights plus a count bubble per paragraph), and a YouTube-style discussion sits under every note.

## Storage

Comments live beside notes in the Library, one file per note:

```text
library/.slate/comments/<note-uuid>.json
```

```json
{
  "schemaVersion": 1,
  "noteId": "2b77366d-aa70-4eac-8e46-05a5f4150ee7",
  "threads": [
    {
      "id": "…",
      "anchor": { "kind": "text", "exact": "Write-ahead logging", "prefix": "(MVCC)\n", "suffix": " (WAL)\n" },
      "status": "open",
      "createdAt": "…", "updatedAt": "…", "resolvedAt": null,
      "comments": [
        { "id": "…", "author": { "kind": "user", "name": "You" }, "body": "Why write the log first?", "createdAt": "…" },
        { "id": "…", "author": { "kind": "agent", "name": "Claude" }, "body": "…", "createdAt": "…",
          "linkedNote": { "noteId": "…", "title": "WAL and crash recovery", "path": "…" } }
      ]
    }
  ]
}
```

- **Keyed by note UUID.** Renames and moves never orphan comments. Restoring a deleted note from history brings its comments back.
- **Markdown is never touched.** Asking a question can't conflict with editing, and note history stays clean.
- **Git-synchronized.** Comment files are managed paths: they are committed with the note batch, pushed, and imported. Imports validate each file's schema, note identity and a 1 MiB limit. A malformed file stops synchronization the same way an invalid note does.
- **Limits.** 10,000 characters per comment, a 1,000-character quote, 500 threads per note, 200 comments per thread.
- **Copies.** Copied or duplicated notes start without comments, because they get new UUIDs. Comments of a deleted note stay in Git history.

## Anchoring and re-anchoring

The browser captures the quote from **rendered** text, where it can see exactly what the reader selected. Formatting is invisible there, so selecting across `**bold**` works.

- **Web client.** On every render it searches the rendered text again, ignoring whitespace differences. When the quote occurs more than once, the occurrence whose surroundings best match `prefix`/`suffix` wins. Highlights are drawn with the CSS Custom Highlight API, so the note's DOM is never modified.
- **Api.** It locates the same quote in the Markdown source: first an exact search, then a search over a projection with Markdown punctuation removed and whitespace collapsed. It returns the one-based `line` and that source line as `context`, so an MCP agent sees where the question sits without rendering anything.
- **Detached threads.** If the passage is edited away, the thread is reported as `detached`. It stays visible in the comments drawer with its original quote shown struck through.

## Thread states

| State | Meaning |
| --- | --- |
| `awaitingReply` | Open and the latest comment is the owner's. This is the agent's work queue. |
| answered | Open and the latest comment is an agent's |
| `resolved` | The owner closed it. It stays visible with a quieter dotted underline and margin bubble. Replying again as the owner reopens it. Agents never resolve on their own. |
| `hidden` | The owner chose **Hide**. The thread is kept but leaves highlights, bubbles and the discussion until **Show hidden** is turned on and it is unhidden. Hidden threads are never "awaiting reply". Replying as the owner unhides it. |

Comments are never removed by resolving; only an explicit delete removes one.

## API

| Route | Purpose |
| --- | --- |
| `GET /v1/comments?status=open\|resolved\|all&awaitingReply=true&page=0` | Library-wide threads, newest activity first, 50 per page |
| `GET /v1/notes/{id}/comments` | One note's threads with anchor state, line and context |
| `POST /v1/notes/{id}/comments` | Start a thread: `{ anchor, body, author? }` |
| `POST /v1/notes/{id}/comments/{threadId}/replies` | Reply: `{ body, author?, linkedNoteId? }` |
| `POST /v1/notes/{id}/comments/{threadId}/status` | `{ "status": "open" \| "resolved" }` |
| `POST /v1/notes/{id}/comments/{threadId}/visibility` | `{ "hidden": true \| false }` |
| `POST /v1/notes/{id}/comments/{threadId}/comments/{commentId}/delete` | Delete a reply, or the whole thread when it is the first comment |

## MCP workflow

1. `list_comment_questions` returns the open threads waiting for an answer, with the quote, line and line text.
2. `read_note` (and `search_notes` if needed) gives the agent context.
3. The agent answers in one of two ways:
   - `reply_to_comment` for an answer that fits in a comment.
   - `answer_comment_with_note` for a question that deserves its own page. It creates a note next to the commented note (or in `folderPath`), ends it with a `[[id:…]]` backlink to the source note, and replies in the thread with the new page attached.
4. `set_comment_thread_status` only when the owner asks.

Every note in a result carries the web-app `url`, including `linkedNote`, so the agent can link to the answer from elsewhere.

A prompt that works in Claude or ChatGPT:

> Go through my open Slate reader questions. Answer short ones with a reply. If a question needs a real explanation, write a page with `answer_comment_with_note` and keep the reply to a two-sentence summary. Sign as Claude.

## Notifications

Slate keeps an activity feed so you can find replies and new pages without hunting for them. It is a page (the bell in the sidebar, **Activity** on phones), never a pop-up; the navigation shows an unread count.

- **Every comment is its own entry.** It shows who wrote it, the note, the quoted passage and a preview. Clicking it opens the note in Read mode on that exact thread: the passage drawer for passage questions, or a scroll to the thread in the discussion. An attached answer page has its own card.
- **New pages are bundled.** A bulk copy or duplicate is always one entry. Separate creations or captures from the same device or agent within 10 minutes are merged into one entry while it is unread, with up to 50 pages listed and a total count. A page is never announced twice.
- **Your own activity is left out.** The feed hides what the requesting device itself did, so the web app doesn't notify you about your own comments. **Include this device** shows everything.
- **Who did it.** MCP sends the agent's name with `X-Slate-Actor` when it writes an answer page, so the entry reads "Claude created a page"; other changes show the device label.
- **Read state.** Opening the page marks everything read after a moment; entries that were new stay marked until you leave.

The feed is server state, stored as `notifications.json` in the Git state directory (`Git:StatePath`) and capped at 1,000 entries. It is not Library content, so it is not committed or synchronized, and a damaged file is treated as empty. Recording a notification never changes whether the write it describes succeeded.

| Route | Purpose |
| --- | --- |
| `GET /v1/notifications?kind=comment\|notes&includeOwn=false&page=0` | Newest first, 30 per page, with the unread count |
| `GET /v1/notifications/summary` | `{ "unread": n }` for the navigation badge |
| `POST /v1/notifications/read` | `{ "all": true }` or `{ "ids": [...] }` |

## Web client

- **Read mode.** Select text and choose **Ask about this**. Commented passages are underlined, and each paragraph with open threads gets a count bubble in the right margin; a green dot means at least one is answered. Clicking a highlight or a bubble opens the threads.
- **Comments drawer.** A non-modal side panel on desktop and a bottom sheet on phones. The toolbar's comment button opens all passage threads.
- **Discussion.** Every note ends with a discussion section for whole-page questions and notes to future you. It sorts newest or oldest first, collapses long reply chains, and shows linked pages as cards.
- **Refresh.** Comments refetch on window focus and every minute, so an answer written from another app shows up without a reload.

# slate.lib.web implementation

All web implementation, tests, artifacts and deployment templates live in this folder. Backend/native source is read-only for this task.

## Endpoint and capability map

Existing contracts inspected: `LibraryContracts.cs`, `LibraryApiClient.cs`, `Program.cs`, `LibraryMutations.cs`. JSON uses camelCase. All `/v1` requests require bearer authorization.

| Capability        | Existing endpoint                           | Web behavior                                            |
| ----------------- | ------------------------------------------- | ------------------------------------------------------- |
| Connection/status | GET /v1/status                              | Validate identity; report server/version/sync state     |
| Lazy folders      | GET /v1/library?path=&page=                 | Expand only requested folders, append pages             |
| Note read/save    | GET/PUT /v1/notes/{uuid}                    | Keep full canonical source; If-Match plus revision body |
| Create            | POST /v1/notes, /v1/folders                 | Collision errors preserved                              |
| File operations   | POST /v1/library/rename, move, copy, delete | Explicit dialogs; details count before deletion         |
| Search            | GET /v1/search?q=&page=&pageSize=20         | Server ranking/snippets/filters                         |
| Capture           | POST /v1/captures                           | Durable UUID, timestamp and identical retry body        |
| Attachments       | POST /v1/assets?id=&filename=               | Raw bytes, content type, X-Slate-Sha256                 |
| Asset read        | GET /v1/assets/{uuid}, /metadata            | Authenticated blob URLs; never token URLs               |
| Knowledge links   | GET /v1/notes/{uuid}/links                  | Resolved/missing/ambiguous targets and backlinks        |
| History           | GET /v1/notes/{uuid}/history[/{commit}]     | Read-only historical source                             |
| Git               | POST /v1/sync                               | Human-readable state; no automatic destructive recovery |

Native clients also have bulk previews/link repair, smart views, offline replay, merge, restore, rediscovery and graph APIs. This first web pass implements the requested ten phases, not every native advanced tool. No substitute canonical library or business database.

## Architecture

Next App Router serves a client workspace with a same-origin bounded proxy. No CORS additions to ASP.NET are needed. Operators configure exact `SLATE_ALLOWED_SERVERS`; browser input cannot turn the proxy into arbitrary SSRF. Tokens remain browser-side and are forwarded per request; no server-side credential store. Requests never follow redirects or cache authenticated responses. Session storage is default, persistent storage is explicit. IndexedDB holds only unsaved drafts, not a replicated library. React Query owns server state; focused context owns tabs and UI. No Zustand needed.

## Phase verification

Each checkpoint runs lint, strict typecheck, focused tests and a production build before proceeding. Final browser checks use deterministic API fixtures; no production host is contacted.

Phase 1: lint/typecheck/3 tests/production build passed. The initial lint compatibility issue was resolved before Phase 2.

Phase 2: lint/typecheck/4 tests/production build passed. Native backend was not changed.

Phase 3: lint/typecheck/5 tests/production build passed, including serialized IndexedDB draft writes.

Phase 4: lint/typecheck/5 tests/production build passed. Capture retries retain UUID, timestamp, and submitted body.

Phase 5: lint/typecheck/5 tests/production build passed. Copy/duplicate use backend-generated identities; dirty open items block structural mutations.

Phase 6: lint/typecheck/6 tests/production build passed. Asset bytes use authenticated blob requests, with no tokens in URLs.

Phase 7: lint/typecheck/7 tests/production build passed after explicitly declaring KaTeX CSS. React lint plugin currently requires ESLint 9; TypeScript is pinned to the supported 5.9 compiler.

Phase 8: lint/typecheck/8 tests/production build passed. Historical versions are read-only; server-local state is not mislabeled as Synced.

Phase 9: lint/typecheck/8 tests/production build passed. PWA caches static files only; browser drafts remain isolated from canonical data.

Phase 10: lint/typecheck/14 focused tests/production build passed. Final Chromium acceptance: 22 passed, one optional development-server integration check skipped. Axe checks passed for the exercised desktop/mobile states. Reviewed light, dark, phone reader and phone editor screenshots. Added container/k3s templates and setup documentation without deploying. See `VERIFICATION.md` for coverage and remaining environment-specific checks.

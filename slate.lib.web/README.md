# slate.lib.web

A responsive web client for the existing Slate ASP.NET Core API. The server remains the source of truth. This folder is independent of the Windows/Android projects and does not change the backend.

## Run locally

Use Node.js 24 and pnpm 11.19.0. From this folder:

```powershell
pnpm install --frozen-lockfile
Copy-Item .env.example .env.local
pnpm dev
```

Open http://localhost:3000. Before connecting, edit `.env.local`:

```dotenv
NEXT_PUBLIC_SLATE_DEFAULT_SERVER=https://your-existing-slate-backend
SLATE_ALLOWED_SERVERS=https://your-existing-slate-backend
```

`NEXT_PUBLIC_SLATE_DEFAULT_SERVER` is a non-secret form default, embedded at build time. `SLATE_ALLOWED_SERVERS` is a server-only, comma-separated list of exact approved backend base URLs, read at runtime. Both accept HTTP only for loopback development. Restart after changing configuration. Enter a device token in the connection form; never put it in an environment file, URL or source file.

For a k3s deployment, `SLATE_UPSTREAM_URL` may map that approved HTTPS identity to one operator-controlled internal HTTP service URL. The proxy supplies `X-Forwarded-Proto: https` only for this explicit mapping. The backend must trust only the actual web-pod CIDR; never configure unrestricted forwarded-header trust.

When the web app is behind a reverse proxy, set `SLATE_PUBLIC_ORIGIN` to its exact public HTTPS origin. Browser writes and sync requests use it for strict same-origin checks instead of the container's internal request origin.

The Next server must be able to reach the backend, including private DNS and Tailscale routing. A browser being connected to Tailscale does not automatically give a container that connectivity. No CORS change to Slate is required: requests pass through the same-origin `/api/slate/` proxy.

## Build and verify

```powershell
pnpm check
pnpm exec playwright install chromium
pnpm test:e2e
pnpm start
```

`check` runs lint, strict TypeScript, focused tests and the production build. `start` serves the standalone build on loopback port 3000, loading `.env.local` if present. Set `PORT` and `SLATE_WEB_HOST` to override. The start helper copies only generated/public web assets into `.next/standalone`; it never touches library data.

Browser tests run the production build on port 3100 using isolated, stateful API fixtures. They cover the requested six screen sizes, editing, conflict preservation, draft recovery, file operations, capture, attachments, links/history, mobile navigation, themes and accessibility. Generated screenshots are in `output/playwright`; HTML reports are in `playwright-report`.

Optional real integration is **read-only** and skipped unless `SLATE_TEST_SERVER` and `SLATE_TEST_TOKEN` are supplied. Use a development library, not production. It checks status and root listing only; it does not replace manual end-to-end validation against your server. Do not capture real tokens in shared test traces or terminal transcripts.

## Workspace

- Desktop: resizable navigation, document and details panes; UUID tabs; light/dark/system themes.
- Mobile: full-page notes, bottom navigation and sheets for library, details, settings and actions.
- Editing: CodeMirror, Write/Read/Split, formatting, paste/drop/file uploads, explicit revision-protected saves.
- Reading: GFM, tables/tasks, syntax-highlighted code, callouts, math, diagrams, links and authenticated assets. Arbitrary HTML and external images are not loaded.
- Library: lazy paged folders, create/rename/move/copy/duplicate/delete, search, Inbox and capped local Recent.
- Details: backlinks, outgoing links, metadata, read-only history and server Git status.

Shortcuts: `Ctrl/Cmd+K` opens search outside the editor and inserts a Markdown link inside it; `Ctrl/Cmd+S` saves; `Ctrl/Cmd+B/I` formats; `Ctrl/Cmd+F` searches in the editor; `Ctrl+Shift+C` captures. `Ctrl+Tab`, `Ctrl+Shift+Tab` and `Ctrl+W` are handled where the browser permits interception; browser-reserved shortcuts may take precedence. Middle-click closes a document tab.

## Work and privacy

Tokens use session storage by default. “Remember this device” opts into local storage. Browser storage is accessible to scripts running on this origin and is not an encrypted vault. Use a trusted device and private HTTPS hosting. Disconnect removes the saved credential and in-memory API cache; it preserves unsaved drafts.

IndexedDB stores unsaved note/capture drafts scoped by server and library UUID. Recovery is explicit; it never writes to the server automatically. Capture retries keep the same UUID, timestamp and submitted text. A stale revision preserves the local draft and offers review. Browser storage can be cleared or evicted and is not a backup.

The PWA caches static assets only. It does **not** cache authenticated API responses or claim full offline editing/synchronization. Offline navigation shows a reconnect page. Unsaved drafts can be recovered after reconnecting. Install through the browser's app installation menu on HTTPS; physical Android Chrome installation still requires a device check.

## Deployment templates

`Dockerfile` uses this folder as build context. `deploy/k3s/web.yaml` is the root-owned production template for the separate `slate-web` namespace. It runs as a non-root user with a read-only root filesystem, fixed resources, health probes, private-registry credentials and NodePort `30519` for the local Cloudflare tunnel.

The browser uses `https://lib.karthiksurkanti.in` as its server identity. The server-side proxy maps that identity to the internal `slate.slate.svc.cluster.local` backend and validates browser write origins against the public HTTPS origin. The backend and native-client Tailscale route remain available.

Pushes to `main` validate the web client, publish immutable SHA and moving `main` images to GHCR, stage only the exact image reference on Megatron, and call the root-owned `slate-deploy-web` helper. The helper validates the SHA/path pair, renders the root-owned manifest, checks disk and filesystem constraints, waits for rollout, and verifies the authenticated sync boundary. It never applies storage resources or modifies the Library.

To repeat browser verification against a running local container, set its loopback URL and run:

```powershell
$env:SLATE_CONTAINER_URL = 'http://127.0.0.1:YOUR_MAPPED_PORT'
pnpm exec playwright test --config playwright.container.config.ts
```

This configuration does not start a second web server and accepts only loopback HTTP URLs. It uses isolated API fixtures and writes its report under `output/docker/playwright-report`.

See `docs/IMPLEMENTATION.md` for the endpoint map and phase checkpoints, and `docs/VERIFICATION.md` for tested coverage and limits.

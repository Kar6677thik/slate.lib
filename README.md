# slate.lib

A personal Markdown library. Phase 3D packages the Windows and Android clients, runs the API as a single-replica k3s service over private Tailscale HTTPS, automates releases and immutable container deployment, and adds tested encrypted backup/restore. Markdown files remain canonical and binary assets remain outside Git.

## Projects

- `src/Slate.Lib.Api` — safe filesystem operations, Lucene search, Git coordination/history, authentication and `/v1` endpoints.
- `src/Slate.Lib.App` — native Windows desktop shell plus Android Library/Search/Inbox/Recent navigation, editor, capture, Share target and settings.
- `src/Slate.Lib.Core` — API contracts/client, Markdown handling, atomic draft records, pending-capture retry and bounded recent-note cache shared by both clients.
- `tests/Slate.Lib.Tests` — temporary-directory and HTTP integration tests.
- `samples/library` — five real Markdown notes, including three levels of folders.

The product documents in [docs](docs/ROADMAP.md) describe the broader product. Phases 1 through 3D are implemented. Production setup, credentials, rollback and recovery are in [the operations runbook](docs/OPERATIONS.md).

## Prerequisites

Windows 10 version 1809 or later, x64; .NET SDK **10.0.401** (pinned in `global.json`); Git CLI; the `maui-windows` and `android` workloads; Android SDK/API 36 for Android builds; and the Microsoft Edge WebView2 Evergreen Runtime. Windows 11 is the verified development host. A Visual Studio installation is optional for these CLI commands.

Install the workload once from an administrator PowerShell if required by your .NET installation:

```powershell
dotnet workload install maui-windows android --skip-manifest-update
```

Validated tooling: workload set `10.0.400-manifests.b0700452`, Android SDK workload `36.1.69`, MAUI Controls `10.0.110`, Lucene.NET `4.8.0-beta00018`, Markdig `1.4.0`, YamlDotNet `18.1.0`, HtmlSanitizer `9.2.1039`, Mermaid `12.0.0`, KaTeX `0.18.9`, highlight.js `11.12.0`, and Git `2.51`. Browser libraries are pinned and embedded for offline rendering.

## Build and test

Run from the repository root. Close the app and stop the backend before rebuilding; Windows locks running executables.

```powershell
dotnet restore slate.lib.sln
dotnet build slate.lib.sln --no-restore
dotnet test tests/Slate.Lib.Tests/Slate.Lib.Tests.csproj --no-build --no-restore
```

The API/core/tests can also be built independently of the Windows application:

```powershell
dotnet test tests/Slate.Lib.Tests/Slate.Lib.Tests.csproj
```

## Run locally

First-time setup, from the repository root:

```powershell
dotnet run --project src/Slate.Lib.Api --no-build --no-launch-profile -- --initialize-library
dotnet run --project src/Slate.Lib.Api --no-build --no-launch-profile -- --initialize-git
dotnet run --project src/Slate.Lib.Api --no-build --no-launch-profile -- --create-device-token Windows
```

The third command prints a new device token once. Copy it into the app's **Device token** field. Do not commit it. The server stores only its SHA-256 hash in `.local/server/devices.json`. Use `--list-devices`, `--revoke-device <label>`, or `--rotate-device-token <label>` for lifecycle management; rotation prints its replacement once and revocation takes effect on the next request.

Start the backend in one terminal:

```powershell
dotnet run --project src/Slate.Lib.Api --no-build --no-launch-profile
```

Start the Windows app from another terminal at the repository root:

```powershell
dotnet run --project src/Slate.Lib.App --no-build --no-launch-profile
```

Use `http://localhost:5188`, enter the token, and click **Connect**. Expand **Computer Science → Databases**, then select **PostgreSQL**. Try the relative **Indexes** link and **Distributed Systems → Consensus → Raft**. The client remembers the URL in preferences and the token in Windows secure storage. **Refresh** asks the server to rescan the filesystem and reloads the folder list. Optional `SLATE_SERVER` and `SLATE_TOKEN` process environment variables supply initial settings for development.

Use **+ Note** and **+ Folder** to create content in the selected folder. Notes support **Write**, **Preview** and **Split** modes. Search is server-side and opens with `Ctrl+Shift+F`; results open the existing editor. **History** shows committed versions as read-only Markdown. **Sync** flushes pending Git work, fetches before pushing, and imports only validated fast-forwards. Right-click an explorer item for file actions. The remaining shortcuts are `Ctrl+S`, `Ctrl+N`, `Ctrl+Shift+N`, `Ctrl+C`, `Ctrl+X`, `Ctrl+V`, `F2` and `Delete` in their documented focus scopes.

Windows now writes a protected draft after one idle second and autosaves an existing online note after two idle seconds. **Quick Thought** writes ordinary Markdown under `inbox/`; **Inbox** and **Recent** are convenient views over real notes and the bounded local cache. A stale save retains its draft and still receives the server's normal 412 response.

## Run Android locally

Generate a separate device token, start the backend, and connect an API 29-or-newer emulator or Android device:

```powershell
dotnet run --project src/Slate.Lib.Api --no-build --no-launch-profile -- --create-device-token Android
dotnet run --project src/Slate.Lib.Api --no-build --no-launch-profile
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
& $adb devices
& $adb reverse tcp:5188 tcp:5188
dotnet build src/Slate.Lib.App/Slate.Lib.App.csproj -f net10.0-android -t:Run
```

In Android **Settings**, use `http://localhost:5188` after `adb reverse` and enter the Android token. For a physical device without USB reverse, expose the backend through the specified private HTTPS endpoint and use that URL; non-loopback plain HTTP is rejected by the client. A normal debug build also produces `src/Slate.Lib.App/bin/Debug/net10.0-android/dev.slate.lib-Signed.apk`, which can be installed with:

```powershell
& $adb install -r .\src\Slate.Lib.App\bin\Debug\net10.0-android\dev.slate.lib-Signed.apk
```

The Android bottom navigation exposes **Library**, **Search**, **Inbox**, and **Recent**. **Capture** opens Quick Thought from every page. Folder rows have an overflow action for rename, move, copy, duplicate, and delete; Move/Copy use a drill-down folder picker. Editing uses an Edit/Preview switch and explicit Save, with online existing-note autosave after two idle seconds. Android registers `ACTION_SEND` targets for text/URLs, images, PDFs and generic single-file streams. Shared bytes are copied while the URI grant is valid, shown on the capture screen, and retained with the local draft until upload and capture both succeed.

Draft JSON uses atomic replacement in app data. Pending Quick Thoughts and Shares retain the same capture UUID through retry, and the server maps that UUID to exactly one ordinary Markdown note. Recent keeps at most 100 IDs. Its disposable note cache evicts least-recent files above 100 MiB on Android or 500 MiB on Windows; cached reads are labeled as offline and potentially stale.

The Lucene index lives under configured `Data:DerivedPath/search`, outside the Library repository. It is disposable. Rebuild it with the server stopped:

```powershell
dotnet run --project src/Slate.Lib.Api --no-build --no-launch-profile -- --rebuild-index
```

Local Git history works without a remote. To enable private remote synchronization after `--initialize-git`, configure the repository with ordinary Git; the server uses `Git:RemoteName` and `Git:Branch`:

```powershell
git -C D:\Notes\Library remote add origin git@github.com:OWNER/PRIVATE-KNOWLEDGE.git
git -C D:\Notes\Library push -u origin main
```

Slate commits accepted mutations after 30 quiet seconds, capped at two minutes, and flushes before Sync, deletion and clean shutdown. It fetches before every push. Remote-ahead history is validated and fast-forwarded; rewritten, unrelated or divergent history stops synchronization without changing the active files. Resolve rare divergence in a separate clone, preserve both heads, push the reviewed merge, then use **Sync** again. Git credentials and SSH host verification remain server/OS configuration and never enter API responses or Markdown.

The executable is also directly runnable:

```powershell
& .\src\Slate.Lib.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Slate.Lib.App.exe
```

## Use another library

`src/Slate.Lib.Api/appsettings.json` uses normal ASP.NET Core configuration. Relative paths resolve against the API project/content root. Override the root with an environment variable, keeping it set when you start the backend:

```powershell
$env:Library__RootPath = 'D:\Notes\Library'
dotnet run --project src/Slate.Lib.Api --no-build --no-launch-profile -- --initialize-library
dotnet run --project src/Slate.Lib.Api --no-build --no-launch-profile
```

The directory must already exist. The explicit initialization command creates `.slate/library.json` and inserts UUIDv4 front matter only into notes missing IDs. It validates all discovered notes before changing files and preserves other metadata/body text. Existing IDs are retained; duplicate IDs and malformed metadata fail validation. Run setup with the server stopped and use a disposable copy for initial imports. This bootstrap is not the later transactional Git import workflow.

Notes must be UTF-8, at most 2 MiB, with at most 32 KiB of YAML front matter. Display titles use `title`, then the first H1, then the filename. Paths use `/`, Unicode NFC, at most 100 characters per segment/220 total, and portable Windows-safe names. Dot-prefixed entries and non-Markdown files are hidden. Symlinks/junctions are rejected. The library directory and its ancestors must be controlled by the server operator; concurrent untrusted filesystem writers are outside this local single-owner slice.

Existing note body changes are read from disk when reopened. Direct edits in the live server checkout while Slate runs remain unsupported; edit another clone and push, then use **Sync**. Manual **Refresh** remains available for development. Writes use optimistic revisions and atomic same-directory replacement. Search publication occurs before a successful mutation response; Git/network failure never turns an accepted filesystem save into a failed save.

## API and reader

All `/v1` endpoints require `Authorization: Bearer <device-token>`. Health probes are intentionally anonymous:

| Route | Response |
| --- | --- |
| `GET /health/live` | Process liveness only |
| `GET /health/ready` | Library identity, auth database and writable canonical storage readiness |
| `GET /v1/status` | Library/server/link versions, uptime, asset/search/Git/backup state and note count |
| `GET /v1/devices` | Device labels, IDs, created/last-used/revoked timestamps; never hashes |
| `GET /v1/updates?platform=windows` | Latest signed-client manifest when a newer version exists |
| `GET /v1/releases/{filename}` | Authenticated release download restricted to manifest artifacts |
| `GET /v1/library?path=Computer%20Science&page=0` | Immediate children, folders first; at most 100 entries, `nextPage` or null |
| `GET /v1/notes/{uuid}` | ID, relative path, title, original Markdown, revision |
| `GET /v1/notes/{uuid}/links` | Resolved, ambiguous and broken outgoing wiki links plus derived backlinks |
| `GET /v1/notes/by-path?path=...` | Same note response for relative Markdown links |
| `POST /v1/notes` | Create a Markdown note with a fresh UUID |
| `POST /v1/captures` | Idempotently create a Quick Thought or text/URL Share in `inbox/` using the client capture UUID |
| `POST /v1/assets?id={uuid}&filename=...` | Stream an immutable asset with client UUID and optional `X-Slate-Sha256` |
| `GET /v1/assets/{uuid}` | Authorized ranged asset bytes with safe content disposition |
| `GET /v1/assets/{uuid}/metadata` | Safe filename, detected media type, size, hash and extension |
| `PUT /v1/notes/{uuid}` | Save Markdown; requires the current revision in `If-Match` |
| `POST /v1/folders` | Create a folder |
| `GET /v1/library/item?path=...` | Inspect an item before destructive UI actions |
| `POST /v1/library/rename` | Rename a note or folder |
| `POST /v1/library/move` | Move a note or folder into a destination folder |
| `POST /v1/library/copy` | Copy a note or folder; copied notes receive fresh UUIDs |
| `POST /v1/library/delete` | Delete a note or an explicitly recursive folder |
| `POST /v1/library/refresh` | Rescan the real library filesystem |
| `GET /v1/search?q=postgres&page=0&pageSize=20` | Ranked title/path/snippet results; supports text, phrases, `title:`, `path:` and exact `tag:` |
| `POST /v1/search/rebuild` | Deliberately rebuild the disposable search index |
| `POST /v1/git/flush` | Commit currently pending managed Library changes |
| `POST /v1/sync` | Flush, fetch, fast-forward import or push; returns current Git state |
| `GET /v1/notes/{uuid}/history` | Read-only Git commit history for the current note |
| `GET /v1/notes/{uuid}/history/{commit}` | Historical Markdown for that stable note identity |

Note responses include an ETag and support `If-None-Match`. Stale saves return 412; name/path collisions return 409. Missing files return 404, unsafe paths 400, invalid content 422 and unavailable files 503. API responses never contain physical server paths. Requests use loopback HTTP for development; the client requires HTTPS for any other host and does not follow redirects. Production uses Tailscale Serve to terminate tailnet HTTPS and proxy the loopback-only NodePort.

Markdig produces standard Markdown, footnotes, alerts and tables. The shared renderer adds stable duplicate-aware heading anchors, resolved wiki navigation, visible broken/ambiguous links, backlinks, responsive authenticated images, syntax highlighting, Mermaid and KaTeX. It materializes pinned browser bundles into app cache and loads generated local files on both clients. Raw HTML stays disabled and sanitized; the page CSP blocks network, objects and forms, Mermaid runs in strict mode, and only the bundled scripts execute. Generic attachments download through the authenticated API and open with the platform handler.

## Verification scope

The automated suite covers the filesystem/search/Git/history and Phase 3C behavior plus device-token lifecycle and migration, anonymous health/readiness boundaries, authenticated status privacy, strict release versions, manifest failures, update comparisons/download checksums and update-outage isolation. Windows MSIX and Android APK release targets build. Kubernetes output is rendered and client-validated. The disposable recovery drill performs an encrypted backup, deletes the source, restores it, checks every restic data pack, starts the restored API and verifies search, backlinks and asset bytes. Production credentials, a live k3s rollout, signed publisher trust on physical devices, Tailscale networking and the 20,000-note performance qualification remain operator/device checks.

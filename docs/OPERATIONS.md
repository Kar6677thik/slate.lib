# Slate operations

## Milestone A recovery storage — 2026-10-02

Bulk operation journals and retained originals use <Git:StatePath>/operations/<request-uuid>. Keep this on the same filesystem as the Library so tree renames remain atomic; production /data/state and /data/library already share the volume. No automatic recovery-directory deletion runs. Monitor free space and include this state in backups. Prepared previews may consume up to the bounded selection size. An interrupted applying operation rolls back before service startup; if destination bytes changed independently, startup stops for operator review and preserves both trees. Do not manually replace/delete either tree to clear the error. The authenticated operation-status endpoint distinguishes an acknowledged completion from a pending/rolled-back attempt.


This runbook operates the Phase 3D single-user deployment. Commands assume the repository root, a Linux k3s node, and `/srv/slate/data` as the persistent data root.

## Data and recovery boundaries

| Path | Contents | Authority / recovery |
| --- | --- | --- |
| `library/` | Markdown, `.slate/library.json`, and `.git` | Canonical source and history; backed up |
| `assets/` | Immutable binary assets and sidecars | Canonical; backed up because Git cannot recreate it |
| `auth/devices.json` | Hashed device credentials and revocation state | Canonical server state; backed up |
| `state/git/` | Git recovery records | Canonical operational state; backed up |
| `state/backup-status.json` | Last backup result | Operational status only |
| `releases/` | Signed client packages and manifest | Re-creatable release mirror |
| `/derived` | Lucene index | Disposable; rebuild after restore |

Keep the restic password, repository credentials, client signing keys, and Git SSH key in separately recoverable password-manager or offline storage. Losing a signing key prevents seamless upgrades for that platform. Losing the restic password makes backups unreadable.

## Development and release builds

```powershell
dotnet restore slate.lib.sln
dotnet build slate.lib.sln --no-restore
dotnet test tests/Slate.Lib.Tests/Slate.Lib.Tests.csproj --no-build --no-restore
docker build -t slate-api:local .
docker build -t slate-backup:local -f deploy/backup/Dockerfile .
kubectl kustomize deploy/k3s
```

Client version and Android version code live in `Directory.Build.props`. A release tag must exactly match `v<SlateVersion>`. CI builds signed clients, creates checksums and `release-manifest.json`, publishes an immutable GitHub release, and mirrors it into the server release directory.

For a local unsigned Windows validation package and development-signed Android APK:

```powershell
dotnet publish src/Slate.Lib.App/Slate.Lib.App.csproj -f net10.0-windows10.0.19041.0 -c Release -p:RuntimeIdentifierOverride=win-x64
dotnet publish src/Slate.Lib.App/Slate.Lib.App.csproj -f net10.0-android -c Release
```

Production builds require the signing environment variables documented under **Secrets inventory**. Never commit signing material.

Cut the next release by updating only `SlateVersion` and the monotonically increasing integer `SlateVersionCode` in `Directory.Build.props`, committing that change, then tagging that exact commit:

```powershell
git add Directory.Build.props
git commit -m "Release Slate 0.4.1"
git tag -a v0.4.1 -m "Slate 0.4.1"
git push origin main
git push origin v0.4.1
```

The release workflow rejects a tag that differs from `SlateVersion`. After it succeeds, verify the GitHub release assets and the authenticated `/v1/updates?platform=windows&currentVersion=0.4.0` response. Retain old GitHub releases and server artifacts for manual diagnosis or rollback.

## Prepare the home server

Install k3s, Docker or another OCI builder for image publication, Git, and Tailscale. Give the data-bearing k3s node the persistent directory and ownership expected by the non-root container:

```bash
sudo install -d -o 1654 -g 1654 \
  /srv/slate/data/library /srv/slate/data/assets /srv/slate/data/auth \
  /srv/slate/data/state/git /srv/slate/data/releases
```

Copy the existing library, including its `.git` directory and `.slate/library.json`, into `library/`. Do not point two Slate processes at the same data root.

Create runtime secrets directly in the cluster; `secrets.example.yaml` is a field guide and is intentionally excluded from the kustomization. Main CI substitutes both placeholder image references with the exact published commit tags before sending and applying the kustomization. For a manual deployment, replace those two placeholders yourself first.

`Networking__TrustedProxyNetworks` in `deploy/k3s/config.yaml` is the default k3s pod/bridge CIDR (`10.42.0.0/16`). Set it to the actual cluster CIDR if yours differs. This trust lets only the node-local Tailscale Serve proxy hop supply `X-Forwarded-Proto`; do not replace it with an unrestricted proxy setting. If the node SNATs local NodePort traffic to one exact node address outside that CIDR, put that address in `Networking__TrustedProxies` as a comma-separated value.

```bash
kubectl apply -f deploy/k3s/namespace.yaml
kubectl -n slate create secret docker-registry slate-registry \
  --docker-server=ghcr.io --docker-username=OWNER --docker-password="$GHCR_READ_TOKEN"
kubectl -n slate create secret generic slate-git-ssh \
  --from-file=id_ed25519="$HOME/.ssh/slate" \
  --from-file=known_hosts="$HOME/.ssh/known_hosts"
kubectl -n slate create secret generic slate-backup \
  --from-literal=RESTIC_REPOSITORY='s3:s3.example.invalid/slate' \
  --from-literal=RESTIC_PASSWORD='REPLACE_ME' \
  --from-literal=AWS_ACCESS_KEY_ID='REPLACE_ME' \
  --from-literal=AWS_SECRET_ACCESS_KEY='REPLACE_ME'
# For a manual deployment only, after replacing both image placeholders:
kubectl apply -k deploy/k3s
kubectl -n slate rollout status deployment/slate
```

The deployment uses one replica, `Recreate`, a retained host-path volume, and a filesystem lock. A second writer would violate the library and Git transaction model.

## Private HTTPS

The NodePort listens on the home server. Tailscale Serve terminates tailnet-trusted HTTPS and proxies only the loopback NodePort:

```bash
sudo tailscale serve --bg http://127.0.0.1:30518
tailscale serve status
```

Use the reported `https://<machine>.<tailnet>.ts.net` URL in each client. Do not expose the NodePort on the public router. Slate accepts forwarded HTTPS only from a loopback proxy; non-loopback clients require HTTPS.

## Device credentials

Create one credential per physical device. The plaintext token is printed once; the server persists only its SHA-256 hash.

```bash
kubectl -n slate exec deployment/slate -- dotnet Slate.Lib.Api.dll --create-device-token 'Windows laptop'
kubectl -n slate exec deployment/slate -- dotnet Slate.Lib.Api.dll --create-device-token 'Android phone'
kubectl -n slate exec deployment/slate -- dotnet Slate.Lib.Api.dll --list-devices
kubectl -n slate exec deployment/slate -- dotnet Slate.Lib.Api.dll --revoke-device 'Lost phone'
kubectl -n slate exec deployment/slate -- dotnet Slate.Lib.Api.dll --rotate-device-token 'Windows laptop'
```

Rotation revokes the old secret and prints its replacement once. Revocation affects the next request. Cached local content cannot be remotely erased.

## Health and diagnosis

Liveness is intentionally shallow and anonymous. Readiness confirms the library identity, token database, and writable library/asset storage. Operational status requires a bearer token and never returns physical paths or token hashes.

```bash
curl -fsS https://HOST/health/live
curl -fsS https://HOST/health/ready
curl -fsS -H "Authorization: Bearer $SLATE_TOKEN" https://HOST/v1/status
kubectl -n slate logs deployment/slate
kubectl -n slate get pods,pvc,cronjob
```

A GitHub outage does not fail liveness or ordinary local reads/writes. A missing token database fails readiness so an unprovisioned instance cannot silently enter service.

## CI deployment and rollback

A push to `main` tests the source, builds backend and backup images, pushes immutable `<40-character-commit-SHA>` tags to GHCR, substitutes those exact tags into a temporary deployment bundle, joins the tailnet, and sends the bundle over SSH. The remote command applies the kustomization and waits for rollout. GitHub Actions never receives the personal library.

Rollback changes only the image; it does not replace the persistent volume:

```bash
kubectl -n slate set image deployment/slate api=ghcr.io/OWNER/REPOSITORY:PREVIOUS_40_CHARACTER_COMMIT_SHA
kubectl -n slate rollout status deployment/slate
kubectl -n slate get pods -o wide
curl -fsS https://HOST/health/ready
```

If the new version changed a persisted format incompatibly, first scale to zero and restore the matching backup. Preserve the failed data root for investigation.

## Client installation and updates

The authenticated manifest is `/v1/updates?platform=windows|android`. Clients check no more than once per 24 hours after a successful connection, or on demand in Settings. They compare strict semantic versions, show notes, download through the authenticated API, verify byte count and SHA-256, then open the normal OS installer. **Later** dismisses only that version. Network failure does not block library use.

For first Windows installation, trust the certificate that signed the MSIX, then install it:

```powershell
Import-Certificate .\Slate-0.4.0.cer -CertStoreLocation Cert:\CurrentUser\TrustedPeople
Add-AppxPackage .\Slate-0.4.0.msix
```

For Android, allow installs from the initiating app when prompted, or use ADB:

```powershell
adb install -r .\Slate-0.4.0.apk
```

Every later build must use the same publisher identity/key and a higher version/version code.

When the Windows certificate approaches expiry, obtain a replacement for the same package publisher, update `WINDOWS_PFX_BASE64` and `WINDOWS_PFX_PASSWORD`, and distribute its public certificate through the same trusted channel before the first package signed by it. Preserve the retired private key with the release records. Android has no equivalent key rotation for this sideloaded package identity; preserve the original keystore.

## Backup and restore

The daily CronJob scales Slate to zero, waits for the pod to release the data root, runs encrypted restic backup and retention (`7 daily`, `4 weekly`, `6 monthly`), verifies the repository, records status, and restores the prior replica count even on failure.

Run and inspect an immediate backup:

```bash
job="slate-backup-$(date +%s)"
kubectl -n slate create job --from=cronjob/slate-backup "$job"
kubectl -n slate logs -f "job/$job"
kubectl -n slate exec deployment/slate -- cat /data/state/backup-status.json
```

Restore only into an empty disposable target first. The script refuses a non-empty destination:

```bash
kubectl -n slate scale deployment/slate --replicas=0
kubectl -n slate wait --for=delete pod -l app.kubernetes.io/name=slate --timeout=180s
sudo mv /srv/slate/data /srv/slate/data.failed-$(date +%s)
sudo install -d -o 1654 -g 1654 /srv/slate/data
export RESTIC_REPOSITORY='s3:...'
export RESTIC_PASSWORD_FILE='/root/slate-restic-password'
sudo -E ./scripts/restore-slate.sh /srv/slate/data
sudo chown -R 1654:1654 /srv/slate/data
kubectl -n slate scale deployment/slate --replicas=1
kubectl -n slate rollout status deployment/slate
```

After restore, check readiness, search, a known backlink, Git history, and an asset hash before enabling Git sync. The derived index is intentionally absent and rebuilds from canonical Markdown.

The repository also contains `scripts/Test-DisasterRecovery.ps1`, which makes a disposable encrypted repository, deletes its source, restores it, runs `restic check --read-data`, starts the restored API, and verifies search, backlinks, and asset bytes.

## Secrets inventory

| Secret | Location | Loss / exposure impact |
| --- | --- | --- |
| Device plaintext tokens | OS secure storage only | Revoke/rotate that device; never recover from server hash |
| `devices.json` | PVC and encrypted backup | Restore to retain devices; exposure reveals hashes, not plaintext |
| Git deploy key and known hosts | `slate-git-ssh` Kubernetes Secret | Rotate key; sync unavailable until replaced |
| GHCR read token | `slate-registry` Kubernetes Secret | Rotate; existing pod runs but cannot pull new images |
| Restic repository/password/object credentials | `slate-backup` Secret plus separate recovery copy | Exposure reveals backup access; loss can make recovery impossible |
| Windows PFX/password | GitHub Actions secrets plus offline recovery copy | Loss breaks trusted Windows update continuity |
| Android keystore/alias/passwords | GitHub Actions secrets plus offline recovery copy | Loss prevents upgrades under the same Android package identity |
| Tailscale OAuth client | GitHub Actions secrets, tag restricted | Rotate and audit tailnet ACLs |
| Home-server SSH key/known hosts | GitHub Actions secrets | Rotate and limit the account/sudo commands |

Required Actions secret names are `TS_OAUTH_CLIENT_ID`, `TS_OAUTH_SECRET`, `HOME_SERVER_HOST`, `HOME_SERVER_USER`, `HOME_SERVER_SSH_KEY`, `HOME_SERVER_KNOWN_HOSTS`, `WINDOWS_PFX_BASE64`, `WINDOWS_PFX_PASSWORD`, `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`, and `ANDROID_STORE_PASSWORD`.

Milestone B preferences are app-private device data, outside the knowledge repository. Preserve app data through updates. Unknown/corrupt preference schemas surface an error without replacement; drafts and cached content remain separate. Missing pins require the user to choose an existing Library folder or remove the pin; no filesystem repair is performed.

Milestone C Share staging uses app-private incoming-shares JSON and incoming-assets bytes, preserving handled handoff records. On restart interrupted batches surface their copied subset with a warning; inaccessible original URIs are never retained as attachments. Existing drafts are preserved when a handoff is promoted again. No OCR, URL crawl or new server dependency is involved. Templates are created/edited as ordinary Markdown notes in templates/; no sample templates or notes are imported.

Milestone D implemented (2026-10-02): Smart views require no additional service. Derived search schema upgrades automatically during reconciliation. Metadata dates are never inferred from filesystem timestamps. Existing Markdown and client drafts remain untouched. Saved searches are local preferences; back up workspace-preferences.json with client state.

Current scope: milestones A–M are implemented. Dated phase/milestone records below are historical checkpoints, not statements that later completed features are still deferred. ROADMAP contains current verification and device limitations.


Milestone E implemented (2026-10-02): schema 3 search documents reconcile automatically; no canonical-data migration is required. New search filters use existing authenticated endpoints. Index state remains disposable. A client can retrieve at most 50 results per page and 2,000 pages; narrow broad queries rather than increasing unbounded server allocations.


Milestone F implemented (2026-10-02): operation journals also retain link-held originals and link-payload proposed source. Interrupted moves and approved link repairs recover together before serving. If independently changed bytes prevent safe recovery, startup stops with both versions retained. Do not remove journal contents to bypass that review. No new external dependency or canonical migration is needed.


Milestone G implemented (2026-10-02): no server dependency or library migration. Clients use a versioned local renderer bundle with the navigation script; content security policy still prohibits remote scripts/connections. WebView sessions that disable storage gracefully omit scroll restoration. Device verification should cover long-note scrolling, drawer dismissal, anchors, and reduced motion.


Milestone H — local extraction dependency and limits

The application Docker image now installs tesseract-ocr and tesseract-ocr-eng. Other server installations need the tesseract executable with English data on PATH and the dotnet runtime. No deployment has been performed. Extraction is explicit, one worker at a time; busy requests return a retryable conflict. Failed/interrupted extraction is visible and can be retried. PDF and thumbnail paths have automated real-file and isolated-worker coverage. OCR requires validation on the target server; Tesseract is absent on this Windows development host. Cleanup checks current notes only, warns about history/pending-device references, and retains removal receipts; back up assets before operational cleanup.

Milestone I — offline operation behavior

The foreground client retries pending work on connection and at 30-second intervals while running; Android background execution is subject to the OS. Each replay verifies server library identity, sends attachments before the note, retains failures, and stops automatic retry on conflicts. Manual Retry is in Offline work. Downloads are explicit, not a background whole-library mirror. Offline downloads may become stale and need explicit refresh. Active queue records and server receipts must not be manually edited; retain them with app/server-state backups. No server deployment or canonical library migration was performed.

Milestone J — operational recovery

Use Review independent sync conflicts only after a divergent-state report. Preview creates a candidate Git object and a state receipt without changing HEAD. Apply checks both heads and newly created/untracked managed notes before changing the worktree. Existing pending-import recovery and safety refs protect interruption. After a successful combination, normal Sync publishes the new commit. Same-file changes, unrelated histories, malformed trees or remote rewrites remain untouched and require the documented separate-clone recovery. Native merge UX still needs device verification.

Milestone K — restore operations

Historical API restore revalidates current revision and history ancestry before writing; it commits through the existing Git owner. A commit failure can occur after a valid current revision was saved: inspect current state rather than retrying blindly. Recovery previews use the parent of a deletion commit and exclude UUIDs already present. Missing binary attachments cannot be recovered from Markdown Git; keep independent asset backups. No production recovery was executed.

Milestone L — rediscovery (2026-10-02)
Both clients expose Rediscover with random notes, older ideas/open questions, Something Forgotten, On This Day, explicit learning states, and a dated timeline. Results explain their eligibility. Unknown dates are omitted from date-driven views; templates are excluded. Date-based ordering is deterministic; Random Note is deliberately random. Results are paged in groups of 20 from cached parsed metadata.
Reading history is opt-in and device-local: UUID plus last-opened timestamp, at most 1,000 entries retained for 180 days, never uploaded. Privacy controls enable/disable and clear it. Forgotten results exclude recently opened notes when tracking is enabled; when disabled, only explicit note age is used. No reading activity before opt-in is implied. No canonical metadata migration or production changes.

Milestone M — graph and related notes (2026-10-02)
Windows commands and Android note actions expose a current-note graph and up to eight related notes. Graph traversal is bidirectional over resolved wiki/Markdown links, with directed edges displayed, depths 1–3, folder/type filters, 40 client nodes (60 API maximum) and 240 edges. Limits are visible. Selecting a plotted node or its accessible numbered row opens it. Filters restrict traversal; the current note remains visible.
Related scores are deterministic: direct link +12; shared incoming source +4 each (maximum 20); shared tag +3 each (maximum 15); title/heading term +1 each (maximum 4); same non-root folder +1. Every score has an explanation. Ties sort by path. Templates are excluded from suggestions. All data is rebuilt from the existing link index and cached parsed metadata; there are no embeddings, AI calls, graph database or canonical-data migrations.
Full solution Windows/Android build: zero warnings/errors. All 181 tests pass, including graph edge direction, incoming traversal, filters, limits, invalidation, score explanations and deterministic ordering. L was verified with 179 passing tests before M. Native and physical-device acceptance remains separately reported.

# Slate operations

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

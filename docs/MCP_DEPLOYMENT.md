# Slate MCP deployment

Pull-request CI builds the solution, runs MCP tests, renders the manifests, and verifies the container. A push to `main` that changes MCP or shared build inputs publishes an immutable `${GITHUB_SHA}` image and deploys MCP through the installed helper. MCP-only changes do not redeploy the API or website. The helper uses the root-owned manifest template and existing Kubernetes secret; it does not synchronize edits to the private environment file or change the Cloudflare route.

## Build locally

```powershell
dotnet build src/Slate.Lib.Mcp/Slate.Lib.Mcp.csproj
dotnet test tests/Slate.Lib.Mcp.Tests/Slate.Lib.Mcp.Tests.csproj
docker build -f src/Slate.Lib.Mcp/Dockerfile -t slate-lib-mcp:local .
kubectl kustomize deploy/mcp/k3s
```

## Configuration

| Setting | Meaning |
|---|---|
| `Mcp__PublicOrigin` | Public HTTPS origin, without `/mcp` |
| `Mcp__CanonicalBaseUrl` | Fixed internal canonical API origin |
| `Mcp__CanonicalPublicUrl` | Approved public Slate identity forwarded to the web intelligence route |
| `Mcp__IntelligenceBaseUrl` | Optional fixed internal web service origin |
| `Mcp__WebAppUrl` | Optional HTTPS origin of the Slate web app. When set, every note in a tool result carries `url` (`{origin}/?note={id}`) and the server instructions tell clients to copy it rather than build one. When unset, no `url` is returned. Startup fails if it is not a plain HTTPS origin |
| `Mcp__AllowedCanonicalHosts__*` | Explicit host allowlist for the canonical origin |
| `Mcp__AllowedIntelligenceHosts__*` | Explicit host allowlist for the intelligence origin |
| `Mcp__ExpectedLibraryId` | Required production cross-library guard |
| `OAuth__Authority` | HTTPS authorization-server issuer |
| `OAuth__Audience` | Expected access-token audience |
| `OAuth__Resource` | The MCP endpoint's canonical URL, `https://mcp.karthiksurkanti.in/mcp`. Clients send it as the OAuth `resource` and check that it matches the server they connected to |
| `OAuth__AuthorizationServers__*` | Issuers advertised in protected-resource metadata |
| `OAuth__AllowedSubjectIds__*` | Required production allowlist of user IDs from the JWT `sub` claim |
| `OAuth__AllowedCallerIds__*` | Optional `azp` or `client_id` allowlist |
| `Secrets__CanonicalDeviceToken` | Dedicated internal Slate device token |
| `Secrets__ProposalSigningKey` | Random key for stateless review tokens |

Use at least 32 random bytes for the signing key. Store both secrets in a real Kubernetes Secret or external secret manager. Do not commit the filled secret manifest.

Sign-in is provided by **Orbit Identity** (`orbit-cloud/deploy/identity`), which replaced Auth0 on 3 November 2026. Set:

- `OAuth__Audience` and `OAuth__Resource`: `https://mcp.karthiksurkanti.in/mcp`, which must be identical to Orbit Identity's `Identity__Resources__0__Uri`.
- `OAuth__Authority` and `OAuth__AuthorizationServers__0`: `https://auth.karthiksurkanti.in/`, with its trailing slash.
- `OAuth__AllowedSubjectIds__0`: Orbit Identity's owner subject (`Identity__OwnerSubject`).

Production startup fails unless at least one `OAuth__AllowedSubjectIds__*` entry is configured. Machine-to-machine subjects and client-credentials grants are always rejected, even if mistakenly allowlisted. MCP needs egress to public HTTPS to fetch the issuer's discovery document and signing keys.

Orbit Identity knows all six Slate scopes. Clients link without registration, using Client ID Metadata Documents. The initial MCP challenge requests `slate.read slate.analyze`, but Orbit Identity's consent screen offers every scope and pre-ticks all except `slate.delete` and `slate.admin`. The owner therefore decides write and organize access at consent, not the client. The protected-resource metadata omits `slate.delete` while destructive operations are disabled. MCP authorizes only `scope`/`scp`, preserving per-client scope narrowing. Links, scopes and revocation are managed in Orbit.

## Kubernetes rollout

Routine organization requires `slate.organize`, granted at Orbit Identity's consent screen. This enables folder creation, move/rename, reviewed bulk changes, revision-checked link repair, and history restoration without granting `slate.delete` or `slate.admin`. Keep `Mcp__EnableDestructiveOperations=false`; delete plans remain blocked even when organization is enabled.

`sync_library` also requires `slate.organize` and enabled writes. It uses the canonical `/v1/sync` endpoint and returns the resulting Git state and commit heads. `LocalOnly` includes a warning that no remote sync occurred. Conflict, error, uninitialized history, and pending states return an explicit unsuccessful result. After a timeout or pending response, inspect `get_library_status` before retrying. Sync never performs a reset, force-push, or conflict override.

1. Build and push an immutable image tag.
2. Copy `deploy/mcp/k3s/secrets.example.yaml` outside the repository, fill it, and apply it separately.
3. Update the image placeholder in `deployment.yaml` or use a Kustomize image override.
4. Adjust the NetworkPolicy ingress namespace if Cloudflare Tunnel is not in a namespace named `cloudflare`.
5. Apply `kubectl apply -k deploy/mcp/k3s`.
6. Wait for rollout and verify both health endpoints through the ClusterIP.
7. Route Cloudflare Tunnel hostname `mcp.karthiksurkanti.in` to `http://slate-mcp.slate-mcp.svc.cluster.local:8080`.
8. Verify public TLS, host forwarding, OAuth metadata, a 401 challenge on unauthenticated `/mcp`, and authenticated tool discovery.
9. Connect clients (Claude, Codex) only after the read-only acceptance checks pass.

The provided service is ClusterIP. It has no library PVC. The pod is non-root, read-only, seccomp-confined, capability-free, and does not mount a Kubernetes service-account token.

## Cloudflare route

Example ingress entry for an existing `cloudflared` configuration:

```yaml
ingress:
  - hostname: mcp.karthiksurkanti.in
    service: http://slate-mcp.slate-mcp.svc.cluster.local:8080
  - service: http_status:404
```

Preserve the real external scheme and host. Configure only the actual cluster proxy CIDR in `Networking__TrustedProxyNetworks`; do not trust all forwarded headers.

## Rotation and rollback

- Rotate the internal device token from `Slate.Lib.Api`, update the Secret, and restart MCP. Revoking it disables MCP without affecting other clients.
- Rotate the proposal signing key to invalidate all outstanding note and bulk previews.
- Roll back the Deployment image. The MCP pod has no durable state, so rollback does not require data migration.
- If intelligence is unhealthy, leave the MCP canonical read/write service running and repair `slate.lib.web` independently.
- If OAuth validation is unhealthy, keep the endpoint closed; do not enable anonymous production access.

## Operational checks

- A successful sign-in does not prove MCP access. If discovery receives HTTP 401 and MCP logs say `The user is not approved for this Slate library`, the token passed signature, issuer, audience, and lifetime validation but failed the subject/machine-identity check. Compare Orbit Identity's `Identity__OwnerSubject` with the private approved-subject entry and the deployed secret without logging either value. Do not weaken the allowlist or approve a new identity without the owner's authorization.
- The root-owned deploy template `/usr/local/lib/slate-mcp/mcp.yaml` carries its own copy of the `deploy/mcp/k3s/config.yaml` values, including `OAuth__Audience`/`OAuth__Resource`. Every CI deploy re-applies it, so keep it in step with the repository.
- Changes to `/home/kar_thik/.config/slate/mcp.env` require refreshing the MCP Kubernetes secret and restarting only `deployment/slate-mcp` in the `slate-mcp` namespace. Existing pods do not reload environment variables when a secret changes. Preserve unrelated private settings and keep destructive operations disabled.
- If an approved token returns HTTP 200 with `"tools": []`, check whether its `scope`/`scp` contains any Slate scope. The SDK excludes tools whose scope policy fails. MCP logs the authorized tool count, or a warning with claim types only when discovery is empty. Check claims locally without sending access tokens to public token decoders or pasting tokens or identifiers into a chat.
- Alert on readiness failures, repeated `canonical_auth_failed`, `library_identity_mismatch`, rate-limit spikes, and OAuth validation failures.
- Avoid request/response body logging at the proxy and application layers.
- Confirm the canonical device token has only the Slate access needed by this service.
- Re-run authenticated discovery and a stale-revision test after SDK or protocol upgrades.

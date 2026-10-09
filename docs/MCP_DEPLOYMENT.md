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
| `Mcp__AllowedCanonicalHosts__*` | Explicit host allowlist for the canonical origin |
| `Mcp__AllowedIntelligenceHosts__*` | Explicit host allowlist for the intelligence origin |
| `Mcp__ExpectedLibraryId` | Required production cross-library guard |
| `OAuth__Authority` | HTTPS authorization-server issuer |
| `OAuth__Audience` | Expected access-token audience |
| `OAuth__Resource` | Auth0 protected-resource identifier ending in `/mcp`; it may differ from the network endpoint when preserving an existing Auth0 API identifier |
| `OAuth__AuthorizationServers__*` | Issuers advertised in protected-resource metadata |
| `OAuth__AllowedSubjectIds__*` | Required production allowlist of Auth0 user IDs from the JWT `sub` claim |
| `OAuth__AllowedCallerIds__*` | Optional `azp` or `client_id` allowlist |
| `Secrets__CanonicalDeviceToken` | Dedicated internal Slate device token |
| `Secrets__ProposalSigningKey` | Random key for stateless review tokens |

Use at least 32 random bytes for the signing key. Store both secrets in a real Kubernetes Secret or external secret manager. Do not commit the filled secret manifest.

For Auth0, set `OAuth__Audience` and `OAuth__Resource` to the exact API identifier `https://mcp.lib.karthiksurkanti.in/mcp`. Set `OAuth__Authority` and `OAuth__AuthorizationServers__0` to the exact issuer from the Auth0 discovery document, including its trailing slash. Production startup fails unless at least one `OAuth__AllowedSubjectIds__*` entry is configured. Each entry must be an exact Auth0 User ID; machine-to-machine subjects and client-credentials grants are always rejected even if mistakenly allowlisted.

Define `slate.read`, `slate.analyze`, `slate.write`, `slate.organize`, `slate.delete`, and `slate.admin` as Auth0 API permissions. The protected-resource metadata omits `slate.delete` while destructive operations are disabled. Enable Auth0's Resource Parameter Compatibility Profile so the MCP `resource` parameter produces a JWT for the configured API identifier.

The OAuth client must request Slate scopes. The initial MCP challenge requests `slate.read slate.analyze`; for a connection that also edits and organizes notes, set the connector's default scopes to `slate.read slate.analyze slate.write slate.organize`. Do not request `slate.admin` or `slate.delete` for routine use. Reconnect after changing scopes so Auth0 issues a new token.

ChatGPT's advanced OAuth settings distinguish default scopes from **base scopes**, which are requested for every authorization. When every selected tool has OAuth scope tags, ChatGPT requests those tool scopes plus base scopes instead of the defaults. Because MCP discovery filters tools using the current token, a read-only catalog can keep requesting read-only tokens. For an owner-approved write connection, set base scopes to `slate.read slate.analyze slate.write` (one per line). Add `slate.organize` only when organization access is intended. In Auth0's Slate API **Application Access** tab, the ChatGPT application's **User-Delegated Access** grant must permit those same scopes. Prefer a per-application grant over broadening defaults for all third-party applications; leave Client Access and Anonymous Access disabled.

If Auth0 RBAC is enabled for this API, create a Slate role with the intended permissions and assign it to the approved Auth0 user. Auth0 places the intersection of requested scopes and user permissions in the token's `scope` claim. Enabling “Add Permissions in the Access Token” is optional and does not substitute for requesting scopes: its `permissions` claim contains user permissions beyond those delegated to the client. MCP intentionally authorizes only `scope`/`scp`, preserving per-client scope narrowing. See [Auth0 RBAC configuration](https://auth0.com/docs/get-started/apis/enable-role-based-access-control-for-apis) and [OpenAI OAuth challenges](https://developers.openai.com/plugins/build/auth).

## Kubernetes rollout

Routine organization requires `slate.organize` in both the Auth0 user-delegated grant and the connection's base scopes. This enables folder creation, move/rename, reviewed bulk changes, revision-checked link repair, and history restoration without granting `slate.delete` or `slate.admin`. Keep `Mcp__EnableDestructiveOperations=false`; delete plans remain blocked even when organization is enabled.

`sync_library` also requires `slate.organize` and enabled writes. It uses the canonical `/v1/sync` endpoint and returns the resulting Git state and commit heads. `LocalOnly` includes a warning that no remote sync occurred. Conflict, error, uninitialized history, and pending states return an explicit unsuccessful result. After a timeout or pending response, inspect `get_library_status` before retrying. Sync never performs a reset, force-push, or conflict override.

1. Build and push an immutable image tag.
2. Copy `deploy/mcp/k3s/secrets.example.yaml` outside the repository, fill it, and apply it separately.
3. Update the image placeholder in `deployment.yaml` or use a Kustomize image override.
4. Adjust the NetworkPolicy ingress namespace if Cloudflare Tunnel is not in a namespace named `cloudflare`.
5. Apply `kubectl apply -k deploy/mcp/k3s`.
6. Wait for rollout and verify both health endpoints through the ClusterIP.
7. Route Cloudflare Tunnel hostname `mcp.karthiksurkanti.in` to `http://slate-mcp.slate-mcp.svc.cluster.local:8080`.
8. Verify public TLS, host forwarding, OAuth metadata, a 401 challenge on unauthenticated `/mcp`, and authenticated tool discovery.
9. Connect ChatGPT only after the read-only acceptance checks pass.

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

- A successful Auth0 login does not prove MCP access. If discovery receives HTTP 401 and MCP logs say `The Auth0 user is not approved for this Slate library`, the token passed signature, issuer, audience, and lifetime validation but failed the subject/machine-identity check. Compare the exact user ID in the corresponding successful Auth0 login with the private approved-subject entry and the deployed secret without logging either value. Do not weaken the allowlist or approve a new identity without the owner's authorization.
- Changes to `/home/kar_thik/.config/slate/mcp.env` require refreshing the MCP Kubernetes secret and restarting only `deployment/slate-mcp` in the `slate-mcp` namespace. Existing pods do not reload environment variables when a secret changes. Preserve unrelated private settings and keep destructive operations disabled.
- If an approved token returns HTTP 200 with `"tools": []`, check whether its `scope`/`scp` contains any Slate scope. The SDK excludes tools whose scope policy fails. MCP logs the authorized tool count, or a warning with claim types only when discovery is empty. Check claims locally without sending access tokens to public token decoders or pasting tokens or identifiers into a chat.
- Alert on readiness failures, repeated `canonical_auth_failed`, `library_identity_mismatch`, rate-limit spikes, and OAuth validation failures.
- Avoid request/response body logging at the proxy and application layers.
- Confirm the canonical device token has only the Slate access needed by this service.
- Re-run authenticated discovery and a stale-revision test after SDK or protocol upgrades.

# Connect Slate MCP to ChatGPT

This guide was verified against OpenAI's current custom MCP documentation on 2026-10-08. Recheck workspace permissions and current product support before a production rollout.

OpenAI's [custom MCP server guide](https://developers.openai.com/api/docs/guides/custom-mcp-server) says ChatGPT can connect to remote MCP servers with read and write tools using streaming HTTP or SSE. Slate uses streaming HTTP at:

```text
https://mcp.lib.karthiksurkanti.in/mcp
```

## Required server state

Before connecting ChatGPT:

1. Deploy `Slate.Lib.Mcp` and verify `/health/live` and `/health/ready` through the intended route.
2. Configure the Auth0 issuer, the `https://mcp.lib.karthiksurkanti.in/mcp` audience and resource, and at least one exact Auth0 User ID in the allowed-subject list.
3. Configure a dedicated canonical Slate device token in the MCP Secret. Do not reuse a browser, Windows, or Android token.
4. Set `Mcp:ExpectedLibraryId` to the canonical library UUID.
5. Keep `Mcp:EnableDestructiveOperations=false` for initial rollout.
6. Verify `/.well-known/oauth-protected-resource` over public HTTPS.

The external authorization server must follow the [OpenAI plugin authentication guidance](https://developers.openai.com/plugins/build/auth): protected-resource metadata, authorization-server discovery, PKCE, the resource parameter, and a supported client-registration approach. ChatGPT can use CIMD, dynamic client registration, or a pre-registered client. Slate MCP is the OAuth resource server; it does not mint user tokens.

The server validates Auth0's JWT signature, issuer, audience, lifetime, requested scopes, and exact `sub` allowlist. Auth0 machine-to-machine identities are rejected. Enable the tenant's Resource Parameter Compatibility Profile so ChatGPT's RFC 8707 `resource` parameter is mapped to the API audience.

Recommended consent scopes:

- Start with `slate.read slate.analyze`.
- Add `slate.write` for note creation and revision-safe edits.
- Add `slate.organize` for folders, moves, bulk plans, restores, and link repair.
- Grant `slate.delete` only to a separate elevated role if deletion is enabled later.
- Reserve `slate.admin` for operations staff.

## Add the server in ChatGPT

Current OpenAI steps:

1. Use ChatGPT on the web and open **ChatGPT Plugins**.
2. Select the plus button, then **Add custom MCP server**.
3. Enter a clear name such as `Slate Library`.
4. Under Connection, enter `https://mcp.lib.karthiksurkanti.in/mcp`.
5. Choose OAuth authentication and complete sign-in and consent.
6. Review the risk warning and create the plugin.
7. In a chat, select or mention the installed Slate plugin.
8. Begin with `Show my Slate library status and do not make changes.`
9. Confirm that the library UUID and note count match the expected server before granting write scopes.

Workspace permissions, security restrictions, and Lockdown settings can prevent adding or using custom servers. The current OpenAI guide specifically directs this setup through ChatGPT on the web. Test the exact account and workspace you intend to use; do not assume another ChatGPT surface has the same custom-plugin behavior.

## Private alternative: Secure MCP Tunnel

If the MCP endpoint should not be public, OpenAI documents a [Secure MCP Tunnel](https://developers.openai.com/api/docs/guides/secure-mcp-tunnels). Run the tunnel client inside the trusted network and point it at the private HTTP service `http://slate-mcp.slate-mcp.svc.cluster.local:8080/mcp`. In ChatGPT's Add custom MCP server flow, choose **Tunnel** instead of entering a public URL.

The tunnel is outbound-only, so it removes the public listener. OAuth discovery can traverse the tunnel, while any identity-provider pages used during authorization still need to be reachable by the user and ChatGPT as described by the tunnel documentation.

## Acceptance checks in ChatGPT

1. Ask for library status and browse the root.
2. Search for a known title, then read it by note ID.
3. Confirm a read-only user cannot discover write tools.
4. Create a disposable note with a write-scoped user.
5. Preview an edit and inspect its source revision and expiry.
6. Change that note elsewhere, then confirm the old apply returns `stale_revision`.
7. Run a health or gap analysis and confirm source references belong to the expected library.
8. Confirm a user without `slate.delete` cannot prepare a delete, and that deletion is blocked while the server switch is false.

ChatGPT connectivity has not been tested until an operator supplies a real OAuth provider and connects the deployed endpoint. Local tests cover authenticated discovery and the 2026-07-28 wire requirements. A disposable-library smoke test runs independent stateless calls through MCP and the real canonical API for browse, search, read, create, update, stale-conflict preservation, reviewed bulk move, links, UUID persistence, and learning analysis.

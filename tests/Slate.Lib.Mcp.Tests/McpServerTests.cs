using System.Net;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Slate.Lib.Core;
using Slate.Lib.Mcp.Clients;
using Slate.Lib.Mcp.Configuration;
using Slate.Lib.Mcp.Security;
using Slate.Lib.Mcp.Services;
using Slate.Lib.Mcp.Tools;
using Xunit;

namespace Slate.Lib.Mcp.Tests;

public sealed class McpProtocolTests : IClassFixture<McpFactory>
{
    private readonly HttpClient client;
    public McpProtocolTests(McpFactory factory) => client = factory.CreateClient();

    [Fact]
    public async Task McpEndpointRejectsAnonymousRequests()
    {
        using var response = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list", @params = new { } });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("oauth-protected-resource", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProtectedResourceMetadataIsPublicAndAdvertisesScopes()
    {
        var document = await client.GetFromJsonAsync<JsonElement>("/.well-known/oauth-protected-resource");
        Assert.Equal("http://localhost/mcp", document.GetProperty("resource").GetString());
        Assert.Contains("slate.read", document.GetProperty("scopes_supported").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("slate.write", document.GetProperty("scopes_supported").EnumerateArray().Select(value => value.GetString()));
        Assert.DoesNotContain("slate.delete", document.GetProperty("scopes_supported").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task AuthenticatedToolDiscoveryReturnsSlateTools()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "tools/list",
                ["params"] = new Dictionary<string, object?>
                {
                    ["_meta"] = new Dictionary<string, object?>
                    {
                        ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                        ["io.modelcontextprotocol/clientCapabilities"] = new Dictionary<string, object?>()
                    }
                }
            })
        };
        request.Headers.Authorization = new("Bearer", "test-mcp-token");
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", "tools/list");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        Assert.Contains("get_library_status", text, StringComparison.Ordinal);
        Assert.Contains("apply_note_edit", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CanonicalDeviceToken", text, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(ReadJsonRpcPayload(text));
        var tools = document.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
        Assert.NotEmpty(tools);
        Assert.All(tools, tool =>
        {
            var schemes = tool.GetProperty("_meta").GetProperty("securitySchemes").EnumerateArray().ToArray();
            var scheme = Assert.Single(schemes);
            Assert.Equal("oauth2", scheme.GetProperty("type").GetString());
            Assert.NotEmpty(scheme.GetProperty("scopes").EnumerateArray());
        });
    }

    [Fact]
    public async Task AuthenticatedToolCallExecutesAgainstCanonicalBoundary()
    {
        using var request = McpRequest("tools/call", new Dictionary<string, object?>
        {
            ["name"] = "get_library_status",
            ["arguments"] = new Dictionary<string, object?>()
        });

        using var response = await client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {payload}");
        Assert.Contains(McpFactory.LibraryId.ToString(), payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"success\":true", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not-returned", payload, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2025-03-26")]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    public async Task InitializeHandshakeClientsCanDiscoverTools(string protocolVersion)
    {
        using var initialize = LegacyRequest("initialize", new
        {
            protocolVersion,
            capabilities = new { },
            clientInfo = new { name = "initialize-client", version = "1" }
        }, id: 1);
        using var initialized = await client.SendAsync(initialize);
        var initializeBody = await initialized.Content.ReadAsStringAsync();
        Assert.True(initialized.IsSuccessStatusCode, $"Initialize: {initialized.StatusCode}: {initializeBody}");
        using var initializeDocument = JsonDocument.Parse(ReadJsonRpcPayload(initializeBody));
        Assert.Equal(protocolVersion, initializeDocument.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
        var session = initialized.Headers.TryGetValues("Mcp-Session-Id", out var sessions) ? sessions.Single() : null;

        using var notification = LegacyRequest("notifications/initialized", new { }, protocolVersion: protocolVersion, session: session);
        using var notificationResponse = await client.SendAsync(notification);
        Assert.True(notificationResponse.IsSuccessStatusCode, $"Initialized: {notificationResponse.StatusCode}: {await notificationResponse.Content.ReadAsStringAsync()}");

        using var list = LegacyRequest("tools/list", new { }, id: 2, protocolVersion: protocolVersion, session: session);
        using var response = await client.SendAsync(list);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Tools/list: {response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(ReadJsonRpcPayload(body));
        Assert.Contains(document.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray(),
            tool => tool.GetProperty("name").GetString() == "get_library_status");
    }

    private static HttpRequestMessage LegacyRequest(string method, object parameters, int? id = null, string? protocolVersion = null, string? session = null)
    {
        var body = new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters };
        if (id is not null) body["id"] = id;
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new("Bearer", "test-mcp-token");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (protocolVersion is not null) request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", protocolVersion);
        if (session is not null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
        return request;
    }

    private static HttpRequestMessage McpRequest(string method, Dictionary<string, object?> parameters)
    {
        parameters["_meta"] = new Dictionary<string, object?>
        {
            ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
            ["io.modelcontextprotocol/clientCapabilities"] = new Dictionary<string, object?>()
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 2,
                ["method"] = method,
                ["params"] = parameters
            })
        };
        request.Headers.Authorization = new("Bearer", "test-mcp-token");
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", method);
        if (parameters.TryGetValue("name", out var toolName) && toolName is string name)
            request.Headers.TryAddWithoutValidation("Mcp-Name", name);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return request;
    }

    private static string ReadJsonRpcPayload(string body)
    {
        if (!body.StartsWith("event:", StringComparison.Ordinal)) return body;
        var data = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .First(line => line.StartsWith("data:", StringComparison.Ordinal));
        return data["data:".Length..].Trim();
    }
}

public sealed class ProposalProtectorTests
{
    [Fact]
    public void NoteProposalIsBoundToExactMarkdown()
    {
        var protector = CreateProtector();
        var created = protector.Create(Guid.NewGuid(), "revision-1", "# Reviewed\n");
        var result = protector.Validate(created.Token, "# Reviewed\n");
        Assert.Equal("revision-1", result.SourceRevision);
        Assert.Throws<ArgumentException>(() => protector.Validate(created.Token, "# Changed\n"));
    }

    [Fact]
    public void BulkProposalRejectsFingerprintSubstitution()
    {
        var protector = CreateProtector();
        var operationId = Guid.NewGuid();
        var created = protector.CreateBulk(operationId, "move", "fingerprint-1");
        Assert.Equal("move", protector.ValidateBulk(created.Token, operationId, "fingerprint-1").Operation);
        Assert.Throws<ArgumentException>(() => protector.ValidateBulk(created.Token, operationId, "fingerprint-2"));
    }

    private static ProposalProtector CreateProtector()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Secrets:ProposalSigningKey"] = "test-proposal-signing-key-with-enough-entropy" }).Build();
        return new(configuration, Options.Create(new SlateMcpOptions()), new TestEnvironment());
    }
}

public sealed class SubjectAccessPolicyTests
{
    private const string ApprovedSubject = "approved-user";

    [Fact]
    public void ExplicitlyApprovedUserIsAllowed()
    {
        var principal = Principal(new Claim("sub", ApprovedSubject));

        Assert.True(SubjectAccessPolicy.IsAllowed(principal, [ApprovedSubject]));
    }

    [Fact]
    public void UnlistedUserIsRejected()
    {
        var principal = Principal(new Claim("sub", "different-user"));

        Assert.False(SubjectAccessPolicy.IsAllowed(principal, [ApprovedSubject]));
    }

    [Fact]
    public void EmptyAllowlistFailsClosed()
    {
        var principal = Principal(new Claim("sub", ApprovedSubject));

        Assert.False(SubjectAccessPolicy.IsAllowed(principal, []));
    }

    [Fact]
    public void MissingSubjectIsRejected()
    {
        Assert.False(SubjectAccessPolicy.IsAllowed(Principal(new Claim("scope", "slate.read")), [ApprovedSubject]));
    }

    [Theory]
    [InlineData("client-id@clients", null)]
    [InlineData("approved-user", "client-credentials")]
    [InlineData("approved-user", "client_credentials")]
    public void MachineIdentityIsRejectedEvenWhenItsSubjectIsAllowlisted(string subject, string? grantType)
    {
        var claims = new List<Claim> { new("sub", subject) };
        if (grantType is not null) claims.Add(new("gty", grantType));

        Assert.False(SubjectAccessPolicy.IsAllowed(Principal([.. claims]), [subject]));
    }

    [Fact]
    public void ProductionConfigurationRequiresAtLeastOneAllowedSubject()
    {
        var settings = ProductionSettings();

        var error = Assert.Throws<InvalidOperationException>(() => SubjectAccessPolicy.ValidateProductionConfiguration(settings));

        Assert.Contains("AllowedSubjectIds", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionConfigurationRequiresAudienceToMatchResource()
    {
        var settings = ProductionSettings();
        settings.AllowedSubjectIds = [ApprovedSubject];
        settings.Audience = "https://wrong.example/mcp";

        var error = Assert.Throws<InvalidOperationException>(() => SubjectAccessPolicy.ValidateProductionConfiguration(settings));

        Assert.Contains("Audience", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionConfigurationRequiresExactAdvertisedIssuer()
    {
        var settings = ProductionSettings();
        settings.AllowedSubjectIds = [ApprovedSubject];
        settings.AuthorizationServers = ["https://auth.example.test"];

        var error = Assert.Throws<InvalidOperationException>(() => SubjectAccessPolicy.ValidateProductionConfiguration(settings));

        Assert.Contains("AuthorizationServers", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteProductionConfigurationIsAccepted()
    {
        var settings = ProductionSettings();
        settings.AllowedSubjectIds = [ApprovedSubject];

        SubjectAccessPolicy.ValidateProductionConfiguration(settings);
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

    private static OAuthOptions ProductionSettings() => new()
    {
        Authority = "https://auth.example.test/",
        AuthorizationServers = ["https://auth.example.test/"],
        Audience = "https://mcp.example.test/mcp",
        Resource = "https://mcp.example.test/mcp"
    };
}

public sealed class OrbitJwtAuthenticationTests : IClassFixture<OrbitMcpFactory>
{
    private readonly HttpClient client;

    public OrbitJwtAuthenticationTests(OrbitMcpFactory factory) => client = factory.CreateClient();

    [Fact]
    public async Task ApprovedUserTokenIsAccepted()
    {
        using var response = await Send(OrbitMcpFactory.Token(OrbitMcpFactory.ApprovedSubject));

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PublicEndpointCanDifferFromTheTokenAudience()
    {
        using var anonymous = await client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list", @params = new { } });
        var metadata = await client.GetFromJsonAsync<JsonElement>("/.well-known/oauth-protected-resource");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("Bearer resource_metadata=\"https://mcp.karthiksurkanti.in/.well-known/oauth-protected-resource\", scope=\"slate.read slate.analyze\"", anonymous.Headers.WwwAuthenticate.ToString());
        Assert.Equal(OrbitMcpFactory.Audience, metadata.GetProperty("resource").GetString());
    }

    [Fact]
    public async Task ApprovedChatGptOriginReachesAuthenticationBoundary()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "test", version = "1" } } })
        };
        request.Headers.Add("Origin", "https://chatgpt.com");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownOriginIsRejectedBeforeAuthentication()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "test", version = "1" } } })
        };
        request.Headers.Add("Origin", "https://attacker.example");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnapprovedUserTokenIsRejected()
    {
        using var response = await Send(OrbitMcpFactory.Token("unapproved-user"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("machine-client@clients", null)]
    [InlineData("approved-machine-grant", "client-credentials")]
    public async Task MachineTokenIsRejectedEvenWhenSubjectIsAllowlisted(string subject, string? grantType)
    {
        using var response = await Send(OrbitMcpFactory.Token(subject, grantType: grantType));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongAudienceIsRejected()
    {
        using var response = await Send(OrbitMcpFactory.Token(OrbitMcpFactory.ApprovedSubject, audience: "https://wrong.example/mcp"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongIssuerIsRejected()
    {
        using var response = await Send(OrbitMcpFactory.Token(OrbitMcpFactory.ApprovedSubject, issuer: "https://wrong.example.test/"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("JWT")]
    [InlineData("jwt")]
    [InlineData("id_token+jwt")]
    public async Task TokenThatIsNotAnAccessTokenIsRejected(string tokenType)
    {
        using var response = await Send(OrbitMcpFactory.Token(OrbitMcpFactory.ApprovedSubject, tokenType: tokenType));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(SecurityAlgorithms.HmacSha256)]
    [InlineData(SecurityAlgorithms.RsaSha256)]
    public async Task TokenSignedWithAnotherAlgorithmIsRejectedEvenByATrustedKey(string algorithm)
    {
        using var response = await Send(OrbitMcpFactory.Token(OrbitMcpFactory.ApprovedSubject, algorithm: algorithm));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenWithoutExpiryIsRejected()
    {
        using var response = await Send(OrbitMcpFactory.Token(OrbitMcpFactory.ApprovedSubject, expires: false));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("openid profile")]
    [InlineData("slate.readonly")]
    public async Task ApprovedTokenWithoutSlateScopesDiscoversNoTools(string? scope)
    {
        using var response = await Send(OrbitMcpFactory.Token(OrbitMcpFactory.ApprovedSubject, scope: scope));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await DiscoveredTools(response));
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("scp")]
    public async Task ReadScopeDiscoversOnlyReadTools(string claimType)
    {
        using var response = await Send(OrbitMcpFactory.Token(OrbitMcpFactory.ApprovedSubject, scope: "slate.read", scopeClaimType: claimType));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tools = await DiscoveredTools(response);
        Assert.Contains(tools, tool => tool.GetProperty("name").GetString() == "get_library_status");
        Assert.DoesNotContain(tools, tool => tool.GetProperty("name").GetString() == "apply_note_edit");
        Assert.All(tools, tool => Assert.Contains("slate.read", tool.GetProperty("_meta").GetProperty("securitySchemes")[0].GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString())));
    }

    [Fact]
    public async Task PermissionsClaimsDoNotExpandRequestedScopes()
    {
        using var response = await Send(OrbitMcpFactory.Token(OrbitMcpFactory.ApprovedSubject, scope: "slate.read", permissions: ["slate.read", "slate.write", "slate.organize"]));
        var tools = await DiscoveredTools(response);
        Assert.NotEmpty(tools);
        Assert.DoesNotContain(tools, tool => tool.GetProperty("name").GetString() == "apply_note_edit");

        using var permissionsOnly = await Send(OrbitMcpFactory.Token(OrbitMcpFactory.ApprovedSubject, scope: null, permissions: ["slate.read", "slate.write"]));
        Assert.Equal(HttpStatusCode.OK, permissionsOnly.StatusCode);
        Assert.Empty(await DiscoveredTools(permissionsOnly));
    }

    [Fact]
    public async Task OrganizationGrantDiscoversRequestedToolsWithoutDeleteOrAdmin()
    {
        string[] organizationTools = ["create_folder", "move_library_item", "rename_library_item", "preview_bulk_operation", "apply_bulk_operation", "preview_link_repair", "apply_link_repair", "restore_note", "sync_library"];
        using var writeOnly = await Send(OrbitMcpFactory.Token(OrbitMcpFactory.ApprovedSubject, scope: "slate.read slate.analyze slate.write"));
        var writeNames = (await DiscoveredTools(writeOnly)).Select(tool => tool.GetProperty("name").GetString()).ToArray();
        Assert.All(organizationTools, name => Assert.DoesNotContain(name, writeNames));

        using var organized = await Send(OrbitMcpFactory.Token(OrbitMcpFactory.ApprovedSubject, scope: "slate.read slate.analyze slate.write slate.organize"));
        var tools = await DiscoveredTools(organized);
        Assert.All(organizationTools, name => Assert.Contains(tools, tool => tool.GetProperty("name").GetString() == name));
        Assert.Contains(tools, tool => tool.GetProperty("name").GetString() == "get_bulk_operation_status");
        var sync = Assert.Single(tools, tool => tool.GetProperty("name").GetString() == "sync_library");
        Assert.Contains("slate.organize", sync.GetProperty("_meta").GetProperty("securitySchemes")[0].GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()));
    }

    [Fact]
    public async Task EmptyDiscoveryLogsClaimTypesWithoutPrivateValues()
    {
        using var factory = new OrbitMcpFactory();
        using var diagnosticClient = factory.CreateClient();
        using var response = await Send(OrbitMcpFactory.Token(OrbitMcpFactory.ApprovedSubject, scope: null, permissions: ["private-permission-value"]), diagnosticClient);
        Assert.Empty(await DiscoveredTools(response));
        var entry = Assert.Single(factory.DiscoveryLogs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains("no authorized tools", entry.Message);
        Assert.Contains("sub", entry.Message);
        Assert.Contains("permissions", entry.Message);
        Assert.DoesNotContain(OrbitMcpFactory.ApprovedSubject, entry.Message);
        Assert.DoesNotContain("private-permission-value", entry.Message);
    }

    [Fact]
    public async Task ReadScopeListsTheWorkflowPrompts()
    {
        using var response = await SendRpc(OrbitMcpFactory.Token(OrbitMcpFactory.ApprovedSubject, scope: "slate.read"), "prompts/list");
        var names = (await RpcResult(response)).GetProperty("prompts").EnumerateArray().Select(prompt => prompt.GetProperty("name").GetString()).ToArray();
        Assert.Equal(["capture_and_file", "link_note", "process_inbox", "write_note"], names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TokenWithoutSlateScopesListsNoPrompts()
    {
        using var response = await SendRpc(OrbitMcpFactory.Token(OrbitMcpFactory.ApprovedSubject, scope: "openid profile"), "prompts/list");
        Assert.Empty((await RpcResult(response)).GetProperty("prompts").EnumerateArray());
    }

    [Theory]
    [InlineData("capture_and_file", "thought", "Redis evicts keys with LRU by default")]
    [InlineData("process_inbox", null, null)]
    [InlineData("link_note", "note", "Kubernetes")]
    [InlineData("write_note", "topic", "Raft leader election")]
    public async Task PromptsOnlyNameToolsTheServerHas(string prompt, string? argument, string? value)
    {
        var token = OrbitMcpFactory.Token(OrbitMcpFactory.ApprovedSubject, scope: "slate.read slate.analyze slate.write slate.organize");
        using var toolsResponse = await Send(token);
        var tools = (await DiscoveredTools(toolsResponse)).Select(tool => tool.GetProperty("name").GetString()!).ToHashSet(StringComparer.Ordinal);
        var arguments = argument is null ? new Dictionary<string, object?>() : new Dictionary<string, object?> { [argument] = value };
        using var response = await SendRpc(token, "prompts/get", new() { ["name"] = prompt, ["arguments"] = arguments });
        var text = string.Join("\n", (await RpcResult(response)).GetProperty("messages").EnumerateArray().Select(message => message.GetProperty("content").GetProperty("text").GetString()));
        if (value is not null) Assert.Contains(value, text);
        var named = System.Text.RegularExpressions.Regex.Matches(text, @"\b[a-z]+(?:_[a-z]+)+\b").Select(match => match.Value).Distinct().ToArray();
        Assert.NotEmpty(named);
        Assert.All(named, name => Assert.Contains(name, tools));
    }

    private static async Task<JsonElement> RpcResult(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        if (body.StartsWith("event:", StringComparison.Ordinal))
            body = body.Split('\n', StringSplitOptions.RemoveEmptyEntries).First(line => line.StartsWith("data:", StringComparison.Ordinal))[5..].Trim();
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.TryGetProperty("error", out var error), error.ToString());
        return document.RootElement.GetProperty("result").Clone();
    }

    private static async Task<JsonElement[]> DiscoveredTools(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (body.StartsWith("event:", StringComparison.Ordinal))
            body = body.Split('\n', StringSplitOptions.RemoveEmptyEntries).First(line => line.StartsWith("data:", StringComparison.Ordinal))[5..].Trim();
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().Select(tool => tool.Clone()).ToArray();
    }

    private Task<HttpResponseMessage> Send(string token, HttpClient? target = null) => SendRpc(token, "tools/list", target: target);

    private async Task<HttpResponseMessage> SendRpc(string token, string method, Dictionary<string, object?>? parameters = null, HttpClient? target = null)
    {
        var body = new Dictionary<string, object?>(parameters ?? [])
        {
            ["_meta"] = new Dictionary<string, object?>
            {
                ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                ["io.modelcontextprotocol/clientCapabilities"] = new Dictionary<string, object?>()
            }
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = body })
        };
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", method);
        if (parameters?.GetValueOrDefault("name") is string name) request.Headers.TryAddWithoutValidation("Mcp-Name", name);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return await (target ?? client).SendAsync(request);
    }
}

public sealed class UpstreamClientTests
{
    [Theory]
    [InlineData("Synced", false, true, null)]
    [InlineData("LocalOnly", false, true, null)]
    [InlineData("Conflict", true, false, "git_sync_conflict")]
    [InlineData("Error", true, false, "git_sync_failed")]
    [InlineData("NotInitialized", false, false, "git_not_initialized")]
    [InlineData("Synced", true, false, "git_sync_pending")]
    public async Task SyncReportsCanonicalOutcomeWithoutFalseSuccess(string state, bool pending, bool success, string? errorCode)
    {
        var libraryId = Guid.NewGuid();
        var syncState = new GitSyncState(state, pending, LocalHead: "local-head", RemoteHead: "remote-head");
        var syncCalls = 0;
        var handler = new StubHandler(request =>
        {
            Assert.Equal("Bearer internal-token", request.Headers.Authorization?.ToString());
            if (request.RequestUri?.AbsolutePath == "/v1/status")
                return Json(HttpStatusCode.OK, new LibraryStatus(libraryId, 1));
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/sync", request.RequestUri?.AbsolutePath);
            Assert.Null(request.Content);
            syncCalls++;
            return Json(HttpStatusCode.OK, syncState);
        });
        var settings = Options.Create(new SlateMcpOptions { EnableWrites = true });
        var canonical = Canonical(handler, "internal-token", settings.Value);
        var intelligence = Intelligence(new StubHandler(_ => throw new HttpRequestException("offline")), "internal-token", settings.Value);
        var tool = new LibraryMutationTools(canonical, intelligence, CreateProtector(),
            new ToolExecutor(new LibraryContext(canonical, settings), NullLogger<ToolExecutor>.Instance), settings, new HttpContextAccessor());

        var result = await tool.SyncLibrary();

        Assert.Equal(1, syncCalls);
        Assert.Equal(success, result.Success);
        Assert.Equal(errorCode, result.Error?.Code);
        Assert.Equal(syncState, result.Data);
        Assert.Equal(libraryId, result.LibraryId);
        Assert.Equal(state == "LocalOnly", result.Warnings.Count > 0);
    }

    [Fact]
    public async Task DisabledWritesPreventGitSyncRequest()
    {
        var libraryId = Guid.NewGuid();
        var handler = new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/v1/status", request.RequestUri?.AbsolutePath);
            return Json(HttpStatusCode.OK, new LibraryStatus(libraryId, 1));
        });
        var settings = Options.Create(new SlateMcpOptions { EnableWrites = false });
        var canonical = Canonical(handler, "internal-token", settings.Value);
        var intelligence = Intelligence(new StubHandler(_ => throw new InvalidOperationException("No event expected")), "internal-token", settings.Value);
        var tool = new LibraryMutationTools(canonical, intelligence, CreateProtector(),
            new ToolExecutor(new LibraryContext(canonical, settings), NullLogger<ToolExecutor>.Instance), settings, new HttpContextAccessor());

        var result = await tool.SyncLibrary();

        Assert.False(result.Success);
        Assert.Equal("invalid_request", result.Error?.Code);
    }

    [Fact]
    public async Task CanonicalClientSendsOnlyServerHeldDeviceToken()
    {
        string? authorization = null;
        string? forwardedProtocol = null;
        var libraryId = Guid.NewGuid();
        var handler = new StubHandler(request =>
        {
            authorization = request.Headers.Authorization?.ToString();
            forwardedProtocol = request.Headers.GetValues("X-Forwarded-Proto").Single();
            return Json(HttpStatusCode.OK, new LibraryStatus(libraryId, 3));
        });
        var client = Canonical(handler, "internal-device-token");
        var status = await client.Status(default);
        Assert.Equal(libraryId, status.LibraryId);
        Assert.Equal("Bearer internal-device-token", authorization);
        Assert.Equal("https", forwardedProtocol);
    }

    [Fact]
    public async Task StaleCanonicalWriteBecomesSpecificToolError()
    {
        var libraryId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var handler = new StubHandler(request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, new LibraryStatus(libraryId, 1))
            : new HttpResponseMessage(HttpStatusCode.PreconditionFailed) { Content = JsonContent.Create(new { error = "stale" }) });
        var settings = Options.Create(new SlateMcpOptions { EnableWrites = true });
        var canonical = Canonical(handler, "internal-token", settings.Value);
        var intelligence = Intelligence(new StubHandler(_ => Json(HttpStatusCode.Accepted, new { })), "internal-token", new SlateMcpOptions());
        var executor = new ToolExecutor(new LibraryContext(canonical, settings), NullLogger<ToolExecutor>.Instance);
        var tool = new LibraryMutationTools(canonical, intelligence, CreateProtector(), executor, settings, new HttpContextAccessor());
        var result = await tool.UpdateNote(noteId, "# Changed", "old-revision");
        Assert.False(result.Success);
        Assert.Equal("stale_revision", result.Error?.Code);
    }

    [Fact]
    public async Task IntelligenceClientForwardsCanonicalIdentityHeaders()
    {
        string? authorization = null; string? server = null;
        var handler = new StubHandler(request =>
        {
            authorization = request.Headers.Authorization?.ToString();
            server = request.Headers.GetValues("X-Slate-Server").Single();
            return Json(HttpStatusCode.OK, new { findings = Array.Empty<object>() });
        });
        var client = Intelligence(handler, "device-secret", new SlateMcpOptions { IntelligenceBaseUrl = "http://intelligence.test", CanonicalBaseUrl = "http://canonical.test", CanonicalPublicUrl = "https://lib.example.test" });
        _ = await client.Post("health", new { filters = new { } }, default);
        Assert.Equal("Bearer device-secret", authorization);
        Assert.Equal("https://lib.example.test", server);
    }

    [Fact]
    public async Task EmptyAcceptedIntelligenceEventDoesNotFailCanonicalWriteNotification()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var client = Intelligence(handler, "device-secret", new SlateMcpOptions
        {
            IntelligenceBaseUrl = "http://intelligence.test",
            CanonicalBaseUrl = "http://canonical.test",
            CanonicalPublicUrl = "https://lib.example.test"
        });

        await client.Notify(new { type = "note.updated", noteId = Guid.NewGuid() }, default);
    }

    [Fact]
    public async Task HybridSearchFallsBackToCanonicalLexicalSearchWhenIntelligenceIsOffline()
    {
        var libraryId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var canonicalHandler = new StubHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/v1/status" => Json(HttpStatusCode.OK, new LibraryStatus(libraryId, 1)),
            "/v1/search" => Json(HttpStatusCode.OK, new SearchPage("postgres", 0, 20, 1,
                [new SearchHit(noteId, "PostgreSQL", "Database/PostgreSQL.md", "Index notes", 1, "rev-1")], 1)),
            _ => Json(HttpStatusCode.NotFound, new { error = "not found" })
        });
        var settingsValue = new SlateMcpOptions
        {
            IntelligenceBaseUrl = "http://intelligence.test",
            CanonicalBaseUrl = "http://canonical.test",
            CanonicalPublicUrl = "https://lib.example.test"
        };
        var settings = Options.Create(settingsValue);
        var canonical = Canonical(canonicalHandler, "internal-token", settingsValue);
        var intelligence = Intelligence(new StubHandler(_ => throw new HttpRequestException("offline")), "internal-token", settingsValue);
        var executor = new ToolExecutor(new LibraryContext(canonical, settings), NullLogger<ToolExecutor>.Instance);
        var tools = new LibraryReadTools(canonical, intelligence, executor, settings);

        var result = await tools.SearchNotes("postgres", "hybrid");

        Assert.True(result.Success);
        Assert.Equal("lexical", result.Data?.EffectiveMode);
        Assert.NotNull(result.Degraded);
        Assert.Contains(result.References!, reference => reference.NoteId == noteId);
    }

    private static CanonicalSlateClient Canonical(HttpMessageHandler handler, string token, SlateMcpOptions? options = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://canonical.test/") };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Secrets:CanonicalDeviceToken"] = token }).Build();
        return new(http, Options.Create(options ?? new SlateMcpOptions()), configuration);
    }

    private static IntelligenceClient Intelligence(HttpMessageHandler handler, string token, SlateMcpOptions options)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://intelligence.test/") };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Secrets:CanonicalDeviceToken"] = token }).Build();
        return new(http, Options.Create(options), configuration);
    }

    private static ProposalProtector CreateProtector()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Secrets:ProposalSigningKey"] = "test-proposal-signing-key-with-enough-entropy" }).Build();
        return new(configuration, Options.Create(new SlateMcpOptions()), new TestEnvironment());
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status) { Content = JsonContent.Create(body) };
}

public sealed class McpFactory : WebApplicationFactory<Program>
{
    public static readonly Guid LibraryId = Guid.Parse("2cf91c8b-5f02-4665-8f8a-e3b1040cafcb");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("AllowedHosts", "localhost");
        builder.UseSetting("Mcp:PublicOrigin", "http://localhost");
        builder.UseSetting("Mcp:CanonicalBaseUrl", "http://canonical.test");
        builder.UseSetting("Mcp:CanonicalPublicUrl", "https://lib.example.test");
        builder.UseSetting("Mcp:IntelligenceBaseUrl", "http://intelligence.test");
        builder.UseSetting("Mcp:AllowedCanonicalHosts:0", "canonical.test");
        builder.UseSetting("Mcp:AllowedIntelligenceHosts:0", "intelligence.test");
        builder.UseSetting("OAuth:Authority", "");
        builder.UseSetting("OAuth:Audience", "http://localhost/mcp");
        builder.UseSetting("OAuth:Resource", "http://localhost/mcp");
        builder.UseSetting("Development:BearerToken", "test-mcp-token");
        builder.UseSetting("Secrets:CanonicalDeviceToken", "not-returned");
        builder.UseSetting("Secrets:ProposalSigningKey", "test-proposal-signing-key-with-enough-entropy");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            services.AddHttpClient<CanonicalSlateClient>().ConfigurePrimaryHttpMessageHandler(() => new StubHandler(request =>
                request.RequestUri?.AbsolutePath == "/v1/status"
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new LibraryStatus(LibraryId, 4)) }
                    : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = JsonContent.Create(new { error = "not found" }) }));
            services.AddHttpClient<IntelligenceClient>().ConfigurePrimaryHttpMessageHandler(() => new StubHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = JsonContent.Create(new { error = "unavailable" }) }));
        });
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "localhost",
            ["Mcp:PublicOrigin"] = "http://localhost",
            ["Mcp:CanonicalBaseUrl"] = "http://canonical.test",
            ["Mcp:CanonicalPublicUrl"] = "https://lib.example.test",
            ["Mcp:IntelligenceBaseUrl"] = "http://intelligence.test",
            ["Mcp:AllowedCanonicalHosts:0"] = "canonical.test",
            ["Mcp:AllowedIntelligenceHosts:0"] = "intelligence.test",
            ["OAuth:Authority"] = "",
            ["OAuth:Audience"] = "http://localhost/mcp",
            ["OAuth:Resource"] = "http://localhost/mcp",
            ["Development:BearerToken"] = "test-mcp-token",
            ["Secrets:CanonicalDeviceToken"] = "not-returned",
            ["Secrets:ProposalSigningKey"] = "test-proposal-signing-key-with-enough-entropy"
        }));
    }
}

/// <summary>Hosts the MCP server against an Orbit Identity-shaped issuer that signs ES256 <c>at+jwt</c> access tokens.</summary>
public sealed class OrbitMcpFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://auth.example.test/";
    public const string Audience = "https://mcp.example.test/mcp";
    public const string ApprovedSubject = "approved-user";
    private static readonly ECDsaSecurityKey SigningKey = new(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = "orbit-test-es256" };
    // Also trusted by the test issuer, so a rejection proves the algorithm rule rather than an unknown key.
    private static readonly SymmetricSecurityKey HmacKey = new(Encoding.UTF8.GetBytes("slate-orbit-test-hmac-key-with-32-bytes")) { KeyId = "orbit-test-hs256" };
    private static readonly RsaSecurityKey RsaKey = new(RSA.Create(2048)) { KeyId = "orbit-test-rs256" };
    public DiscoveryLogProvider DiscoveryLogs { get; } = new();

    public static string Token(string subject, string audience = Audience, string issuer = Issuer, string? grantType = null,
        string? scope = "slate.read slate.analyze slate.write slate.organize", string scopeClaimType = "scope", IReadOnlyCollection<string>? permissions = null,
        string tokenType = "at+jwt", string algorithm = SecurityAlgorithms.EcdsaSha256, bool expires = true)
    {
        var claims = new List<Claim>
        {
            new("sub", subject)
        };
        if (scope is not null) claims.Add(new(scopeClaimType, scope));
        if (permissions is not null) claims.AddRange(permissions.Select(permission => new Claim("permissions", permission)));
        if (grantType is not null) claims.Add(new("gty", grantType));

        SecurityKey key = algorithm switch
        {
            SecurityAlgorithms.EcdsaSha256 => SigningKey,
            SecurityAlgorithms.HmacSha256 => HmacKey,
            SecurityAlgorithms.RsaSha256 => RsaKey,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = expires }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            TokenType = tokenType,
            IssuedAt = DateTime.UtcNow.AddSeconds(-5),
            NotBefore = DateTime.UtcNow.AddSeconds(-5),
            Expires = expires ? DateTime.UtcNow.AddMinutes(5) : null,
            SigningCredentials = new SigningCredentials(key, algorithm)
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging => logging.AddProvider(DiscoveryLogs));
        builder.UseEnvironment("Testing");
        builder.UseSetting("AllowedHosts", "localhost");
        builder.UseSetting("Mcp:PublicOrigin", "https://mcp.karthiksurkanti.in");
        builder.UseSetting("Mcp:AllowedOrigins:0", "https://chatgpt.com");
        builder.UseSetting("Mcp:CanonicalBaseUrl", "http://canonical.test");
        builder.UseSetting("Mcp:CanonicalPublicUrl", "https://lib.example.test");
        builder.UseSetting("Mcp:AllowedCanonicalHosts:0", "canonical.test");
        builder.UseSetting("OAuth:Authority", Issuer);
        builder.UseSetting("OAuth:Audience", Audience);
        builder.UseSetting("OAuth:Resource", Audience);
        builder.UseSetting("OAuth:AuthorizationServers:0", Issuer);
        builder.UseSetting("OAuth:AllowedSubjectIds:0", ApprovedSubject);
        builder.UseSetting("OAuth:AllowedSubjectIds:1", "machine-client@clients");
        builder.UseSetting("OAuth:AllowedSubjectIds:2", "approved-machine-grant");
        builder.UseSetting("Secrets:CanonicalDeviceToken", "not-returned");
        builder.UseSetting("Secrets:ProposalSigningKey", "test-proposal-signing-key-with-enough-entropy");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "localhost",
            ["Mcp:PublicOrigin"] = "https://mcp.karthiksurkanti.in",
            ["Mcp:AllowedOrigins:0"] = "https://chatgpt.com",
            ["Mcp:CanonicalBaseUrl"] = "http://canonical.test",
            ["Mcp:CanonicalPublicUrl"] = "https://lib.example.test",
            ["Mcp:AllowedCanonicalHosts:0"] = "canonical.test",
            ["OAuth:Authority"] = Issuer,
            ["OAuth:Audience"] = Audience,
            ["OAuth:Resource"] = Audience,
            ["OAuth:AuthorizationServers:0"] = Issuer,
            ["OAuth:AllowedSubjectIds:0"] = ApprovedSubject,
            ["OAuth:AllowedSubjectIds:1"] = "machine-client@clients",
            ["OAuth:AllowedSubjectIds:2"] = "approved-machine-grant",
            ["Secrets:CanonicalDeviceToken"] = "not-returned",
            ["Secrets:ProposalSigningKey"] = "test-proposal-signing-key-with-enough-entropy"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            var oidc = new OpenIdConnectConfiguration { Issuer = Issuer };
            oidc.SigningKeys.Add(SigningKey);
            oidc.SigningKeys.Add(HmacKey);
            oidc.SigningKeys.Add(RsaKey);
            services.PostConfigure<JwtBearerOptions>("SlateBearer", options =>
            {
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(oidc);
            });
        });
    }
}

public sealed class DiscoveryLogProvider : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
    public void Dispose() { }

    private sealed class Logger(DiscoveryLogProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => category == "Slate.Lib.Mcp.Discovery";
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) provider.Entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}

internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handler(request));
}

internal sealed class TestEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Testing";
    public string ApplicationName { get; set; } = "Slate.Lib.Mcp.Tests";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

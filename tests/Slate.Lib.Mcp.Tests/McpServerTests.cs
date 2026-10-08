using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        Assert.Contains("slate.delete", document.GetProperty("scopes_supported").EnumerateArray().Select(value => value.GetString()));
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

public sealed class UpstreamClientTests
{
    [Fact]
    public async Task CanonicalClientSendsOnlyServerHeldDeviceToken()
    {
        string? authorization = null;
        var libraryId = Guid.NewGuid();
        var handler = new StubHandler(request =>
        {
            authorization = request.Headers.Authorization?.ToString();
            return Json(HttpStatusCode.OK, new LibraryStatus(libraryId, 3));
        });
        var client = Canonical(handler, "internal-device-token");
        var status = await client.Status(default);
        Assert.Equal(libraryId, status.LibraryId);
        Assert.Equal("Bearer internal-device-token", authorization);
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
        builder.UseSetting("OAuth:Audience", "slate-mcp");
        builder.UseSetting("OAuth:Resource", "http://localhost/mcp");
        builder.UseSetting("Development:BearerToken", "test-mcp-token");
        builder.UseSetting("Secrets:CanonicalDeviceToken", "not-returned");
        builder.UseSetting("Secrets:ProposalSigningKey", "test-proposal-signing-key-with-enough-entropy");
        builder.ConfigureTestServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
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
            ["OAuth:Audience"] = "slate-mcp",
            ["OAuth:Resource"] = "http://localhost/mcp",
            ["Development:BearerToken"] = "test-mcp-token",
            ["Secrets:CanonicalDeviceToken"] = "not-returned",
            ["Secrets:ProposalSigningKey"] = "test-proposal-signing-key-with-enough-entropy"
        }));
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

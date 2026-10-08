using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
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

public sealed class SubjectAccessPolicyTests
{
    private const string ApprovedSubject = "auth0|approved-user";

    [Fact]
    public void ExplicitlyApprovedAuth0UserIsAllowed()
    {
        var principal = Principal(new Claim("sub", ApprovedSubject));

        Assert.True(SubjectAccessPolicy.IsAllowed(principal, [ApprovedSubject]));
    }

    [Fact]
    public void UnlistedAuth0UserIsRejected()
    {
        var principal = Principal(new Claim("sub", "auth0|different-user"));

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
    [InlineData("auth0|approved-user", "client-credentials")]
    [InlineData("auth0|approved-user", "client_credentials")]
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
        settings.AuthorizationServers = ["https://tenant.us.auth0.com"];

        var error = Assert.Throws<InvalidOperationException>(() => SubjectAccessPolicy.ValidateProductionConfiguration(settings));

        Assert.Contains("AuthorizationServers", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteAuth0ProductionConfigurationIsAccepted()
    {
        var settings = ProductionSettings();
        settings.AllowedSubjectIds = [ApprovedSubject];

        SubjectAccessPolicy.ValidateProductionConfiguration(settings);
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

    private static OAuthOptions ProductionSettings() => new()
    {
        Authority = "https://tenant.us.auth0.com/",
        AuthorizationServers = ["https://tenant.us.auth0.com/"],
        Audience = "https://mcp.lib.karthiksurkanti.in/mcp",
        Resource = "https://mcp.lib.karthiksurkanti.in/mcp"
    };
}

public sealed class Auth0JwtAuthenticationTests : IClassFixture<Auth0McpFactory>
{
    private readonly HttpClient client;

    public Auth0JwtAuthenticationTests(Auth0McpFactory factory) => client = factory.CreateClient();

    [Fact]
    public async Task ApprovedUserTokenIsAccepted()
    {
        using var response = await Send(Auth0McpFactory.Token(Auth0McpFactory.ApprovedSubject));

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnapprovedUserTokenIsRejected()
    {
        using var response = await Send(Auth0McpFactory.Token("auth0|unapproved-user"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("machine-client@clients", null)]
    [InlineData("auth0|approved-machine-grant", "client-credentials")]
    public async Task MachineTokenIsRejectedEvenWhenSubjectIsAllowlisted(string subject, string? grantType)
    {
        using var response = await Send(Auth0McpFactory.Token(subject, grantType: grantType));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongAudienceIsRejected()
    {
        using var response = await Send(Auth0McpFactory.Token(Auth0McpFactory.ApprovedSubject, audience: "https://wrong.example/mcp"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongIssuerIsRejected()
    {
        using var response = await Send(Auth0McpFactory.Token(Auth0McpFactory.ApprovedSubject, issuer: "https://wrong.us.auth0.com/"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpResponseMessage> Send(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
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
        request.Headers.Authorization = new("Bearer", token);
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", "tools/list");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return await client.SendAsync(request);
    }
}

public sealed class UpstreamClientTests
{
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

public sealed class Auth0McpFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://tenant.us.auth0.com/";
    public const string Audience = "https://mcp.lib.karthiksurkanti.in/mcp";
    public const string ApprovedSubject = "auth0|approved-user";
    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("slate-auth0-test-signing-key-32-bytes-minimum"));

    public static string Token(string subject, string audience = Audience, string issuer = Issuer, string? grantType = null)
    {
        var claims = new List<Claim>
        {
            new("sub", subject),
            new("scope", "slate.read slate.analyze slate.write slate.organize")
        };
        if (grantType is not null) claims.Add(new("gty", grantType));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = DateTime.UtcNow.AddSeconds(-5),
            NotBefore = DateTime.UtcNow.AddSeconds(-5),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256)
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("AllowedHosts", "localhost");
        builder.UseSetting("Mcp:PublicOrigin", "https://mcp.lib.karthiksurkanti.in");
        builder.UseSetting("Mcp:CanonicalBaseUrl", "http://canonical.test");
        builder.UseSetting("Mcp:CanonicalPublicUrl", "https://lib.example.test");
        builder.UseSetting("Mcp:AllowedCanonicalHosts:0", "canonical.test");
        builder.UseSetting("OAuth:Authority", Issuer);
        builder.UseSetting("OAuth:Audience", Audience);
        builder.UseSetting("OAuth:Resource", Audience);
        builder.UseSetting("OAuth:AuthorizationServers:0", Issuer);
        builder.UseSetting("OAuth:AllowedSubjectIds:0", ApprovedSubject);
        builder.UseSetting("OAuth:AllowedSubjectIds:1", "machine-client@clients");
        builder.UseSetting("OAuth:AllowedSubjectIds:2", "auth0|approved-machine-grant");
        builder.UseSetting("Secrets:CanonicalDeviceToken", "not-returned");
        builder.UseSetting("Secrets:ProposalSigningKey", "test-proposal-signing-key-with-enough-entropy");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "localhost",
            ["Mcp:PublicOrigin"] = "https://mcp.lib.karthiksurkanti.in",
            ["Mcp:CanonicalBaseUrl"] = "http://canonical.test",
            ["Mcp:CanonicalPublicUrl"] = "https://lib.example.test",
            ["Mcp:AllowedCanonicalHosts:0"] = "canonical.test",
            ["OAuth:Authority"] = Issuer,
            ["OAuth:Audience"] = Audience,
            ["OAuth:Resource"] = Audience,
            ["OAuth:AuthorizationServers:0"] = Issuer,
            ["OAuth:AllowedSubjectIds:0"] = ApprovedSubject,
            ["OAuth:AllowedSubjectIds:1"] = "machine-client@clients",
            ["OAuth:AllowedSubjectIds:2"] = "auth0|approved-machine-grant",
            ["Secrets:CanonicalDeviceToken"] = "not-returned",
            ["Secrets:ProposalSigningKey"] = "test-proposal-signing-key-with-enough-entropy"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            var oidc = new OpenIdConnectConfiguration { Issuer = Issuer };
            oidc.SigningKeys.Add(SigningKey);
            services.PostConfigure<JwtBearerOptions>("SlateBearer", options =>
            {
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(oidc);
            });
        });
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

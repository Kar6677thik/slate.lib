extern alias SlateApi;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Slate.Lib.Core;
using Slate.Lib.Mcp.Clients;
using Xunit;
using ApiDeviceTokens = SlateApi::Slate.Lib.Api.DeviceTokens;
using ApiLibraryPaths = SlateApi::Slate.Lib.Api.LibraryPaths;
using ApiLibrarySetup = SlateApi::Slate.Lib.Api.LibrarySetup;
using ApiLibraryStore = SlateApi::Slate.Lib.Api.LibraryStore;
using ApiProgram = SlateApi::Program;

namespace Slate.Lib.Mcp.Tests;

public sealed class McpEndToEndTests
{
    [Fact]
    public async Task StatelessMcpLifecyclePreservesCanonicalIdentityAndRejectsStaleWrites()
    {
        using var library = new DisposableLibrary();
        var canonicalToken = new ApiDeviceTokens(library.DeviceFile).Create("mcp-e2e");
        using var api = new DisposableApiFactory(library);
        _ = api.CreateClient();
        using var canonicalHandler = api.Server.CreateHandler();
        using var mcp = new DisposableMcpFactory(canonicalHandler, canonicalToken, library.LibraryId);
        using var client = mcp.CreateClient();

        var status = await Call(client, "get_library_status", new { });
        Assert.Equal(library.LibraryId, status.GetProperty("libraryId").GetGuid());

        var browse = await Call(client, "browse_library", new { path = "Database", page = 0 });
        Assert.True(browse.GetProperty("success").GetBoolean());
        Assert.Contains("PostgreSQL.md", browse.GetRawText(), StringComparison.Ordinal);

        var search = await Call(client, "search_notes", new { query = "PostgreSQL", mode = "lexical", page = 0, pageSize = 20 });
        Assert.Equal("lexical", search.GetProperty("data").GetProperty("effectiveMode").GetString());

        var initial = await Call(client, "read_note", new { noteId = library.SeedNoteId });
        Assert.Contains("WAL", initial.GetProperty("data").GetProperty("markdown").GetString(), StringComparison.Ordinal);

        var created = await Call(client, "create_note", new { folderPath = "", name = "MCP Smoke.md", title = "MCP Smoke", initialMarkdown = "# MCP Smoke\n\nOriginal.\n" });
        var createdData = created.GetProperty("data");
        var createdId = createdData.GetProperty("id").GetGuid();
        var originalRevision = createdData.GetProperty("revision").GetString()!;
        var createdMarkdown = createdData.GetProperty("markdown").GetString()!;

        var updatedMarkdown = createdMarkdown.Replace("Original.", "Concurrent update preserved.", StringComparison.Ordinal);
        var updated = await Call(client, "update_note", new { noteId = createdId, markdown = updatedMarkdown, revision = originalRevision });
        Assert.True(updated.GetProperty("success").GetBoolean(), updated.GetRawText());
        var updatedRevision = updated.GetProperty("data").GetProperty("revision").GetString();
        Assert.NotEqual(originalRevision, updatedRevision);

        var staleMarkdown = createdMarkdown.Replace("Original.", "Stale overwrite.", StringComparison.Ordinal);
        var stale = await Call(client, "update_note", new { noteId = createdId, markdown = staleMarkdown, revision = originalRevision });
        Assert.False(stale.GetProperty("success").GetBoolean());
        Assert.Equal("stale_revision", stale.GetProperty("error").GetProperty("code").GetString());

        var afterConflict = await Call(client, "read_note", new { noteId = createdId });
        Assert.Contains("Concurrent update preserved", afterConflict.GetProperty("data").GetProperty("markdown").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Stale overwrite", afterConflict.GetProperty("data").GetProperty("markdown").GetString(), StringComparison.Ordinal);

        _ = await Call(client, "create_folder", new { parentPath = "", name = "Moved" });
        var operationId = Guid.NewGuid();
        var preview = await Call(client, "preview_bulk_operation", new { operationId, operation = "move", paths = new[] { "MCP Smoke.md" }, destinationFolderPath = "Moved" });
        var previewData = preview.GetProperty("data");
        var canonicalPreview = previewData.GetProperty("preview");
        var fingerprint = canonicalPreview.GetProperty("fingerprint").GetString()!;
        var proposalToken = previewData.GetProperty("proposalToken").GetString()!;
        var applied = await Call(client, "apply_bulk_operation", new { operationId, fingerprint, proposalToken, repairIncomingLinks = false });
        Assert.True(applied.GetProperty("success").GetBoolean());

        var moved = await Call(client, "read_note", new { noteId = createdId });
        Assert.Equal(createdId, moved.GetProperty("data").GetProperty("id").GetGuid());
        Assert.Equal("Moved/MCP Smoke.md", moved.GetProperty("data").GetProperty("path").GetString());

        var links = await Call(client, "get_note_links", new { noteId = createdId });
        Assert.True(links.GetProperty("success").GetBoolean());
        var learning = await Call(client, "analyze_learning_coverage", new { topic = "PostgreSQL", sourceLimit = 10 });
        Assert.True(learning.GetProperty("success").GetBoolean());

        Assert.Single(Directory.EnumerateFiles(library.Root, "MCP Smoke.md", SearchOption.AllDirectories));
    }

    private static async Task<JsonElement> Call(HttpClient client, string toolName, object arguments)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = Guid.NewGuid().ToString("N"),
                method = "tools/call",
                @params = new
                {
                    name = toolName,
                    arguments,
                    _meta = new Dictionary<string, object?>
                    {
                        ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                        ["io.modelcontextprotocol/clientCapabilities"] = new Dictionary<string, object?>()
                    }
                }
            })
        };
        request.Headers.Authorization = new("Bearer", "test-mcp-token");
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", "tools/call");
        request.Headers.TryAddWithoutValidation("Mcp-Name", toolName);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {payload}");
        var data = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries).Single(line => line.StartsWith("data: ", StringComparison.Ordinal))[6..];
        using var document = JsonDocument.Parse(data);
        return document.RootElement.GetProperty("result").GetProperty("structuredContent").Clone();
    }

    private sealed class DisposableMcpFactory(HttpMessageHandler canonicalHandler, string canonicalToken, Guid libraryId) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AllowedHosts"] = "localhost",
                ["Mcp:PublicOrigin"] = "http://localhost",
                ["Mcp:CanonicalBaseUrl"] = "http://canonical.test",
                ["Mcp:CanonicalPublicUrl"] = "https://lib.example.test",
                ["Mcp:IntelligenceBaseUrl"] = "http://intelligence.test",
                ["Mcp:ExpectedLibraryId"] = libraryId.ToString(),
                ["Mcp:AllowedCanonicalHosts:0"] = "canonical.test",
                ["Mcp:AllowedIntelligenceHosts:0"] = "intelligence.test",
                ["OAuth:Authority"] = "",
                ["OAuth:Audience"] = "http://localhost/mcp",
                ["OAuth:Resource"] = "http://localhost/mcp",
                ["Development:BearerToken"] = "test-mcp-token",
                ["Secrets:CanonicalDeviceToken"] = canonicalToken,
                ["Secrets:ProposalSigningKey"] = "test-proposal-signing-key-with-enough-entropy"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<CanonicalSlateClient>().ConfigurePrimaryHttpMessageHandler(() => canonicalHandler);
                services.AddHttpClient<IntelligenceClient>().ConfigurePrimaryHttpMessageHandler(() =>
                    new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = JsonContent.Create(new { error = "offline" }) }));
            });
        }
    }

    private sealed class DisposableApiFactory(DisposableLibrary library) : WebApplicationFactory<ApiProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Library:RootPath", library.Root);
            builder.UseSetting("Authentication:DeviceFile", library.DeviceFile);
            builder.UseSetting("Data:DerivedPath", library.DerivedRoot);
            builder.UseSetting("Data:AssetsPath", Path.Combine(library.DerivedRoot, "assets"));
            builder.UseSetting("Git:StatePath", Path.Combine(library.DerivedRoot, "state"));
            builder.UseSetting("Git:SyncIntervalSeconds", "3600");
        }
    }

    private sealed class DisposableLibrary : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "slate-mcp-e2e-" + Guid.NewGuid().ToString("N"));
        public string DerivedRoot { get; } = Path.Combine(Path.GetTempPath(), "slate-mcp-derived-" + Guid.NewGuid().ToString("N"));
        public string DeviceFile => Path.Combine(Root, ".devices.json");
        public Guid SeedNoteId { get; } = Guid.NewGuid();
        public Guid LibraryId { get; }

        public DisposableLibrary()
        {
            Directory.CreateDirectory(Path.Combine(Root, "Database"));
            File.WriteAllText(Path.Combine(Root, "Database", "PostgreSQL.md"), $"---\nid: {SeedNoteId}\n---\n# PostgreSQL\n\nWAL records durable changes.\n", new UTF8Encoding(false));
            ApiLibrarySetup.Initialize(new ApiLibraryPaths(Root));
            LibraryId = new ApiLibraryStore(new ApiLibraryPaths(Root)).LibraryId;
        }

        public void Dispose()
        {
            SafeDelete(Root, "slate-mcp-e2e-");
            SafeDelete(DerivedRoot, "slate-mcp-derived-");
        }

        private static void SafeDelete(string path, string prefix)
        {
            if (!Directory.Exists(path)) return;
            var resolved = Path.GetFullPath(path);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe test cleanup path.");
            foreach (var file in Directory.EnumerateFiles(resolved, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(resolved, true);
        }
    }
}

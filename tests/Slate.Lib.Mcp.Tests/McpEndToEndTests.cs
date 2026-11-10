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
        Assert.False(initial.GetProperty("data").TryGetProperty("url", out _), "No url is returned unless Mcp:WebAppUrl is configured.");
        Assert.DoesNotContain("\"url\"", initial.GetRawText(), StringComparison.Ordinal);

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

        var sync = await Call(client, "sync_library", new { });
        Assert.False(sync.GetProperty("success").GetBoolean());
        Assert.Equal("NotInitialized", sync.GetProperty("data").GetProperty("state").GetString());
        Assert.Equal("git_not_initialized", sync.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task NoteResultsCarryTheConfiguredWebAppUrl()
    {
        using var library = new DisposableLibrary();
        var canonicalToken = new ApiDeviceTokens(library.DeviceFile).Create("mcp-urls");
        using var api = new DisposableApiFactory(library);
        _ = api.CreateClient();
        using var canonicalHandler = api.Server.CreateHandler();
        using var mcp = new DisposableMcpFactory(canonicalHandler, canonicalToken, library.LibraryId, "https://lib.example.test");
        using var client = mcp.CreateClient();
        string UrlFor(Guid id) => $"https://lib.example.test/?note={id}";

        var read = await CallResult(client, "read_note", new { noteId = library.SeedNoteId });
        var structured = read.GetProperty("structuredContent");
        Assert.Equal(UrlFor(library.SeedNoteId), structured.GetProperty("data").GetProperty("url").GetString());
        Assert.Equal(UrlFor(library.SeedNoteId), structured.GetProperty("references")[0].GetProperty("url").GetString());
        var text = read.GetProperty("content").EnumerateArray().Single(block => block.GetProperty("type").GetString() == "text").GetProperty("text").GetString()!;
        Assert.Contains(UrlFor(library.SeedNoteId), text, StringComparison.Ordinal);

        var search = await Call(client, "search_notes", new { query = "PostgreSQL", mode = "lexical", page = 0, pageSize = 20 });
        foreach (var hit in search.GetProperty("data").GetProperty("results").EnumerateArray())
            Assert.Equal(UrlFor(hit.GetProperty("id").GetGuid()), hit.GetProperty("url").GetString());
        Assert.NotEmpty(search.GetProperty("data").GetProperty("results").EnumerateArray());

        var browse = await Call(client, "browse_library", new { path = "Database", page = 0 });
        var entries = browse.GetProperty("data").GetProperty("entries").EnumerateArray().ToArray();
        var noteEntry = entries.First(entry => !entry.GetProperty("isDirectory").GetBoolean());
        Assert.Equal(UrlFor(noteEntry.GetProperty("id").GetGuid()), noteEntry.GetProperty("url").GetString());

        var created = await Call(client, "create_note", new { folderPath = "", name = "Canvas Link.md", title = "Canvas Link", initialMarkdown = "# Canvas Link\n\nSee [the canvas](https://canvas.example.test/c/42).\n" });
        var createdId = created.GetProperty("data").GetProperty("id").GetGuid();
        Assert.Equal(UrlFor(createdId), created.GetProperty("data").GetProperty("url").GetString());

        // An https link to another app is ordinary Markdown: it is not a note link, so it gets no Slate url.
        var links = await Call(client, "get_note_links", new { noteId = createdId });
        Assert.Equal(UrlFor(createdId), links.GetProperty("data").GetProperty("url").GetString());
        Assert.Empty(links.GetProperty("data").GetProperty("outgoing").EnumerateArray());
    }

    [Fact]
    public async Task AgentFindsReaderQuestionsAndAnswersWithACommentOrALinkedPage()
    {
        using var library = new DisposableLibrary();
        var canonicalToken = new ApiDeviceTokens(library.DeviceFile).Create("mcp-comments");
        using var api = new DisposableApiFactory(library);
        using var owner = api.CreateClient();
        owner.DefaultRequestHeaders.Authorization = new("Bearer", canonicalToken);
        using var canonicalHandler = api.Server.CreateHandler();
        using var mcp = new DisposableMcpFactory(canonicalHandler, canonicalToken, library.LibraryId, "https://lib.example.test");
        using var client = mcp.CreateClient();

        // The owner, reading in the web app, highlights "WAL" and asks; later leaves a page-level question too.
        var quick = await (await owner.PostAsJsonAsync($"/v1/notes/{library.SeedNoteId}/comments",
            new CreateCommentThreadRequest(new(CommentAnchor.Text, "WAL", "PostgreSQL ", " records"), "What does WAL stand for?"))).Content.ReadFromJsonAsync<CommentThreadView>();
        var deep = await (await owner.PostAsJsonAsync($"/v1/notes/{library.SeedNoteId}/comments",
            new CreateCommentThreadRequest(new(CommentAnchor.Page), "How does crash recovery actually replay the log?"))).Content.ReadFromJsonAsync<CommentThreadView>();

        var questions = await Call(client, "list_comment_questions", new { });
        Assert.True(questions.GetProperty("success").GetBoolean(), questions.GetRawText());
        var items = questions.GetProperty("data").GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        var passage = items.Single(item => item.GetProperty("thread").GetProperty("id").GetGuid() == quick!.Id);
        Assert.Equal($"https://lib.example.test/?note={library.SeedNoteId}", passage.GetProperty("url").GetString());
        Assert.Equal(6, passage.GetProperty("thread").GetProperty("line").GetInt32());
        Assert.Equal("WAL records durable changes.", passage.GetProperty("thread").GetProperty("context").GetString());

        var reply = await Call(client, "reply_to_comment", new { noteId = library.SeedNoteId, threadId = quick!.Id, markdown = "Write-ahead log.", agentName = "Claude" });
        Assert.True(reply.GetProperty("success").GetBoolean(), reply.GetRawText());
        var agentComment = reply.GetProperty("data").GetProperty("comments")[1];
        Assert.Equal("agent", agentComment.GetProperty("author").GetProperty("kind").GetString());
        Assert.Equal("Claude", agentComment.GetProperty("author").GetProperty("name").GetString());

        var page = await Call(client, "answer_comment_with_note", new { noteId = library.SeedNoteId, threadId = deep!.Id, title = "WAL crash recovery", markdown = "Recovery replays WAL records from the last checkpoint.", replyMarkdown = "This deserved a full page.", agentName = "ChatGPT" });
        Assert.True(page.GetProperty("success").GetBoolean(), page.GetRawText());
        var note = page.GetProperty("data").GetProperty("note");
        var noteId = note.GetProperty("id").GetGuid();
        Assert.Equal("Database/wal-crash-recovery.md", note.GetProperty("path").GetString());
        var markdown = note.GetProperty("markdown").GetString()!;
        Assert.Contains("# WAL crash recovery", markdown, StringComparison.Ordinal);
        Assert.Contains($"[[id:{library.SeedNoteId:D}|PostgreSQL]]", markdown, StringComparison.Ordinal);
        var linked = page.GetProperty("data").GetProperty("thread").GetProperty("comments")[1].GetProperty("linkedNote");
        Assert.Equal(noteId, linked.GetProperty("noteId").GetGuid());
        Assert.Equal($"https://lib.example.test/?note={noteId}", linked.GetProperty("url").GetString());

        var remaining = await Call(client, "list_comment_questions", new { });
        Assert.Empty(remaining.GetProperty("data").GetProperty("items").EnumerateArray());
        var all = await Call(client, "get_note_comments", new { noteId = library.SeedNoteId });
        Assert.All(all.GetProperty("data").GetProperty("threads").EnumerateArray(), thread => Assert.Equal("open", thread.GetProperty("status").GetString()));

        var missing = await Call(client, "reply_to_comment", new { noteId = library.SeedNoteId, threadId = Guid.NewGuid(), markdown = "x" });
        Assert.Equal("not_found", missing.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task OutputSchemasAllowTheAddedUrlField()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var mcp = new DisposableMcpFactory(handler, "unused", Guid.NewGuid(), "https://lib.example.test");
        using var client = mcp.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = "list",
                method = "tools/list",
                @params = new { _meta = new Dictionary<string, object?> { ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28", ["io.modelcontextprotocol/clientCapabilities"] = new Dictionary<string, object?>() } }
            })
        };
        request.Headers.Authorization = new("Bearer", "test-mcp-token");
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", "tools/list");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, payload);
        Assert.Contains("read_note", payload, StringComparison.Ordinal);
        // A closed schema would make every result that gained a url invalid for clients that validate output.
        Assert.DoesNotContain("\"additionalProperties\":false", payload.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://lib.example.test")]
    [InlineData("https://lib.example.test/notes")]
    public void StartupRejectsAWebAppUrlThatIsNotAnHttpsOrigin(string webAppUrl)
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var mcp = new DisposableMcpFactory(handler, "unused", Guid.NewGuid(), webAppUrl);
        var error = Assert.ThrowsAny<Exception>(() => mcp.CreateClient());
        Assert.Contains("Mcp:WebAppUrl", error.ToString(), StringComparison.Ordinal);
    }

    private static async Task<JsonElement> Call(HttpClient client, string toolName, object arguments) =>
        (await CallResult(client, toolName, arguments)).GetProperty("structuredContent").Clone();

    private static async Task<JsonElement> CallResult(HttpClient client, string toolName, object arguments)
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
        return document.RootElement.GetProperty("result").Clone();
    }

    private sealed class DisposableMcpFactory(HttpMessageHandler canonicalHandler, string canonicalToken, Guid libraryId, string? webAppUrl = null) : WebApplicationFactory<Program>
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
                ["Secrets:ProposalSigningKey"] = "test-proposal-signing-key-with-enough-entropy",
                ["Mcp:WebAppUrl"] = webAppUrl
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

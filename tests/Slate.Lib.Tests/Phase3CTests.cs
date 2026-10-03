using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Slate.Lib.Api;
using Slate.Lib.Core;
using Xunit;

namespace Slate.Lib.Tests;

public sealed class AssetTests
{
    [Fact]
    public async Task UploadSniffsBytesPersistsMetadataAndIsIdempotent()
    {
        using var fixture = new TestLibrary();
        var store = Assets(fixture);
        var id = Guid.NewGuid(); var bytes = Png();
        var first = await store.UploadAsync(id, "../unsafe.svg", "image/svg+xml", new MemoryStream(bytes), bytes.Length, Hash(bytes), default);
        Assert.Equal("image/png", first.ContentType); Assert.Equal(".png", first.Extension); Assert.True(first.InlineImage);
        Assert.Equal("unsafe.svg", first.OriginalFilename);
        var retry = await store.UploadAsync(id, "different-name.png", "image/png", new MemoryStream(bytes), bytes.Length, Hash(bytes), default);
        Assert.Equal(first, retry);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(store.Open(id).Path));
        var restarted = Assets(fixture); Assert.Equal(first, restarted.ReadMetadata(id));
        await Assert.ThrowsAsync<LibraryConflictException>(() => restarted.UploadAsync(id, "x.png", "image/png", new MemoryStream([1, 2, 3]), 3, null, default));
    }

    [Fact]
    public async Task EnforcesLimitAndTreatsUnsafeOrUnknownTypesAsDownloads()
    {
        using var fixture = new TestLibrary();
        var store = Assets(fixture, 1024);
        await Assert.ThrowsAsync<AssetTooLargeException>(() => store.UploadAsync(Guid.NewGuid(), "huge.bin", null, new MemoryStream(new byte[1025]), 1025, null, default));
        var record = await store.UploadAsync(Guid.NewGuid(), "page.html", "text/html", new MemoryStream("<script>x</script>"u8.ToArray()), null, null, default);
        Assert.Equal("application/octet-stream", record.ContentType); Assert.Equal(".bin", record.Extension); Assert.False(record.InlineImage);
    }

    [Fact]
    public async Task CaptureReferencesUploadedAssetsOutsideMarkdownTree()
    {
        using var fixture = new TestLibrary();
        var assets = Assets(fixture); var id = Guid.NewGuid(); var bytes = Png();
        await assets.UploadAsync(id, "photo.png", "image/png", new MemoryStream(bytes), bytes.Length, null, default);
        var paths = new LibraryPaths(fixture.Root);
        var git = new GitSyncService(paths, Options.Create(new GitOptions { StatePath = Path.Combine(fixture.DerivedRoot, "git-state") }), NullLogger<GitSyncService>.Instance);
        await git.InitializeAsync();
        var store = new LibraryStore(paths, null, git, new LinkIndex(), assets);
        var capture = store.Capture(new(Guid.NewGuid(), "", "visual note", "share", AssetIds: [id]));
        await git.FlushAsync(store);
        Assert.Contains($"../.assets/{id:D}.png", capture.Markdown);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, ".assets")));
        var tracked = RunGit(fixture.Root, "ls-files");
        Assert.Contains(capture.Path, tracked.Replace('\\', '/'));
        Assert.DoesNotContain(id.ToString("D"), tracked, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PendingAssetCaptureSurvivesFailureAndDeletesBytesOnlyAfterSuccess()
    {
        using var fixture = new TestLibrary();
        var appData = Path.Combine(fixture.DerivedRoot, "app-data");
        var cache = Path.Combine(fixture.DerivedRoot, "cache");
        var libraryId = Guid.NewGuid();
        var state = new ClientStateStore(appData, cache, libraryId, 1024 * 1024);
        var pending = await new PendingAssetStore(appData, libraryId).StageAsync(
            new MemoryStream(Png()), "offline.png", "image/png");
        var capture = new CaptureNoteRequest(Guid.NewGuid(), "offline attachment");
        await state.SaveDraftAsync(new(capture.CaptureId, DraftKind.QuickThought, capture.Content, DateTimeOffset.UtcNow,
            PendingCapture: capture, PendingAssets: [pending]));
        var processor = new PendingCaptureProcessor(state);

        Assert.Equal(0, await processor.RetryAsync((_, _) => throw new HttpRequestException("offline"),
            (_, _) => throw new InvalidOperationException("capture must not run")));
        Assert.True(File.Exists(pending.LocalPath));
        Assert.NotNull(await state.ReadDraftAsync(capture.CaptureId));

        CaptureNoteRequest? submitted = null;
        Assert.Equal(1, await processor.RetryAsync(
            (asset, _) => Task.FromResult(new AssetMetadata(asset.Id, asset.OriginalFilename, asset.ContentType, asset.ByteSize, DateTimeOffset.UtcNow, asset.Sha256, ".png", true)),
            (request, _) =>
            {
                submitted = request;
                return Task.FromResult(new LibraryNote(request.CaptureId, "inbox/offline.md", "Offline", request.Content, "revision"));
            }));
        Assert.Equal([pending.Id], submitted!.AssetIds);
        Assert.False(File.Exists(pending.LocalPath));
        Assert.Null(await state.ReadDraftAsync(capture.CaptureId));
        Assert.Equal(0, await processor.RetryAsync((_, _) => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException()));
    }

    private static AssetStore Assets(TestLibrary fixture, long maximum = 25L * 1024 * 1024) =>
        new(Options.Create(new AssetOptions { RootPath = Path.Combine(fixture.DerivedRoot, "assets"), MaximumBytes = maximum }));
    private static byte[] Png() => [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1, 2, 3];
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
    private static string RunGit(string directory, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd(); var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output;
    }
}

public sealed class LinkTests
{
    [Fact]
    public void ParserSupportsHeadingLabelsAndIgnoresCode()
    {
        var links = WikiLinks.Extract("[[PostgreSQL MVCC#Vacuum|MVCC]]\n`[[inline]]`\n```md\n[[fenced]]\n```\n");
        var link = Assert.Single(links); Assert.Equal("PostgreSQL MVCC", link.Target); Assert.Equal("Vacuum", link.Heading); Assert.Equal("MVCC", link.Label);
    }

    [Fact]
    public void ResolvesIdsPathsTitlesAliasesHeadingsAndBacklinksIncrementally()
    {
        var targetId = Guid.NewGuid(); var sourceId = Guid.NewGuid();
        var target = Note(targetId, "Knowledge/Postgres.md", "---\nid: " + targetId + "\naliases: [Postgres]\n---\n# PostgreSQL MVCC\n## Vacuum\n## Vacuum\n", "a");
        var source = Note(sourceId, "Source.md", $"---\nid: {sourceId}\n---\n# Source\n[[Postgres#Vacuum|alias]] [[id:{targetId}]] [[Knowledge/Postgres.md]] [[Missing]]", "b");
        var index = new LinkIndex(); index.Reconcile([target, source]);
        var outgoing = index.Read(sourceId).Outgoing;
        Assert.Equal(["resolved", "resolved", "resolved", "broken"], outgoing.Select(x => x.State));
        Assert.Equal(sourceId, Assert.Single(index.Read(targetId).Backlinks).SourceId);
        index.Upsert(source with { Markdown = $"---\nid: {sourceId}\n---\n# Source\nNo links now.", Revision = "c" });
        Assert.Empty(index.Read(targetId).Backlinks);
        Assert.True(index.ParsedNotes >= 3);
    }

    [Fact]
    public void DuplicateTitlesRemainAmbiguousAndMissingHeadingsAreDistinct()
    {
        var one = Guid.NewGuid(); var two = Guid.NewGuid(); var source = Guid.NewGuid();
        var index = new LinkIndex(); index.Reconcile([
            Note(one, "One.md", $"---\nid: {one}\n---\n# Same", "1"),
            Note(two, "Two.md", $"---\nid: {two}\n---\n# Same", "2"),
            Note(source, "Source.md", $"---\nid: {source}\n---\n# Source\n[[Same]] [[id:{one}#Absent]]", "3")]);
        Assert.Equal("ambiguous", index.Read(source).Outgoing[0].State);
        Assert.Equal("missing-heading", index.Read(source).Outgoing[1].State);
    }

    [Fact]
    public void StoreMaintainsLinksAcrossEditRenameMoveCopyDeleteAndRefresh()
    {
        using var fixture = new TestLibrary(); var links = new LinkIndex();
        var store = new LibraryStore(new LibraryPaths(fixture.Root), null, null, links, null);
        var source = store.CreateNote(new("", "Links", "Links", InitialMarkdown: $"# Links\n[[id:{fixture.NoteId}]]"));
        Assert.Equal(source.Id, Assert.Single(store.Links(fixture.NoteId).Backlinks).SourceId);
        store.Rename(new("Science/Databases/Note.md", "Renamed"));
        Assert.Contains("Note", NoteDocument.Parse(store.Read(fixture.NoteId).Markdown, "x.md").Aliases);
        store.CreateFolder(new("", "Archive")); store.Move(new("Science/Databases/Renamed.md", "Archive"));
        Assert.Equal(source.Id, Assert.Single(store.Links(fixture.NoteId).Backlinks).SourceId);
        var copy = store.Copy(new(source.Path, "")); Assert.Equal(2, store.Links(fixture.NoteId).Backlinks.Count);
        store.Delete(new(copy.Path, false)); Assert.Single(store.Links(fixture.NoteId).Backlinks);
        File.WriteAllText(Path.Combine(fixture.Root, source.Path), $"---\nid: {source.Id}\n---\n# Links\nExternal edit"); store.Refresh();
        Assert.Empty(store.Links(fixture.NoteId).Backlinks);
    }

    [Fact]
    public void HeadingSlugsAreStableUnicodeAwareAndDeduplicated()
    {
        var slugger = new HeadingSlugger();
        Assert.Equal("hello-world", slugger.Add("Hello, World!"));
        Assert.Equal("hello-world-1", slugger.Add("Hello World"));
        Assert.Equal("café", slugger.Add("Cafe\u0301"));
    }

    private static LibraryNote Note(Guid id, string path, string markdown, string revision) =>
        new(id, path, NoteDocument.Parse(markdown, path).Title, markdown, revision);
}

public sealed class RichRendererTests
{
    [Theory]
    [InlineData("# Plain note\nA short paragraph.", false, false, false)]
    [InlineData("```csharp\nvar x = 1;\n```", true, false, false)]
    [InlineData("Inline $x^2$", false, true, false)]
    [InlineData("```mermaid\ngraph TD\nA-->B\n```", false, false, true)]
    public void LoadsOnlyRendererEnginesNeededByTheNote(string body, bool code, bool math, bool diagram)
    {
        var id = Guid.NewGuid();
        var html = new MarkdownReader().Render(new(id, "Test.md", "Test", $"---\nid: {id}\n---\n{body}", "r"));
        Assert.Equal(code, html.Contains("src=\"highlight.min.js\"", StringComparison.Ordinal));
        Assert.Equal(math, html.Contains("src=\"katex/katex.min.js\"", StringComparison.Ordinal));
        Assert.Equal(diagram, html.Contains("src=\"mermaid.min.js\"", StringComparison.Ordinal));
        Assert.Contains("src=\"slate-render.js\"", html);
    }

    [Fact]
    public void RendersRichSemanticsWithStableSafetyBoundary()
    {
        var id = Guid.NewGuid(); var target = Guid.NewGuid();
        var markdown = $"---\nid: {id}\n---\n# Repeat\n## Repeat\n## Repeat\n`[[Ignored]] $not-math$`\n[[Target#Section]]\n\n> [!NOTE]\n> Read this.\n\n```mermaid\ngraph TD\nA-->B\n```\n\n```csharp\nvar x = 1;\n```\n\nInline $x^2$ and:\n\n$$\\int_0^1 x dx$$\n\nFootnote[^1].\n\n[^1]: Detail.\n\n<script>alert(1)</script>";
        var link = new NoteLink(0, 0, "", "Target", null, "Section", "resolved", target, "Target.md", "Target");
        var html = new MarkdownReader().Render(new(id, "Notes/Test.md", "Repeat", markdown, "r"), new(id, [link], []));
        Assert.Contains("id=\"repeat\"", html); Assert.Contains("id=\"repeat-1\"", html); Assert.Contains("id=\"repeat-2\"", html);
        Assert.Contains($"slate-note://{target:D}#section", html); Assert.Contains("class=\"mermaid\"", html);
        Assert.Contains("[[Ignored]] $not-math$", html);
        Assert.Contains("markdown-alert-note", html); Assert.Contains("SLATE_MATH_", html); Assert.Contains("footnotes", html);
        Assert.Contains("highlight.min.js", html); Assert.Contains("securityLevel:'strict'", FileBootstrap()); Assert.DoesNotContain("<script>alert(1)</script>", html);
    }

    [Fact]
    public async Task MaterializesPinnedOfflineRendererBundles()
    {
        using var fixture = new TestLibrary();
        var path = await MarkdownReader.MaterializeAssetsAsync(fixture.DerivedRoot);
        Assert.True(new FileInfo(Path.Combine(path, "mermaid.min.js")).Length > 1_000_000);
        Assert.True(File.Exists(Path.Combine(path, "katex", "katex.min.js")));
        Assert.True(File.Exists(Path.Combine(path, "highlight.min.js")));
        Assert.True(File.Exists(Path.Combine(path, "slate-render.js")));
    }

    [Fact]
    public async Task DesktopAppearanceIsOptInAndKeepsReaderSecurityPolicy()
    {
        using var fixture = new TestLibrary();
        var id = Guid.NewGuid();
        var note = new LibraryNote(id, "Test.md", "Test", $"---\nid: {id}\n---\n# Test\n\n```csharp\nvar x = 1;\n```", "r");
        var reader = new MarkdownReader();
        var desktopPath = await reader.WriteDocumentAsync(fixture.DerivedRoot, note, desktopAppearance: true);
        var desktop = await File.ReadAllTextAsync(desktopPath);
        Assert.Contains("Segoe UI Variable", desktop);
        Assert.Contains("slate-desktop.js", desktop);
        Assert.Contains("script-src 'self'", desktop);
        Assert.Contains("connect-src 'none'", desktop);
        var defaultPath = await reader.WriteDocumentAsync(fixture.DerivedRoot, note);
        var mobile = await File.ReadAllTextAsync(defaultPath);
        Assert.DoesNotContain("Segoe UI Variable", mobile);
        Assert.DoesNotContain("slate-desktop.js", mobile);
        Assert.Contains("script-src 'self'", mobile);
    }

    private static string FileBootstrap()
    {
        var field = typeof(MarkdownReader).GetField("BootstrapJavaScript", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (string)field.GetRawConstantValue()!;
    }
}

public sealed class Phase3CApiTests
{
    private sealed class Factory(TestLibrary fixture) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Library:RootPath", fixture.Root); builder.UseSetting("Authentication:DeviceFile", fixture.DeviceFile);
            builder.UseSetting("Data:DerivedPath", fixture.DerivedRoot); builder.UseSetting("Data:AssetsPath", Path.Combine(fixture.DerivedRoot, "assets"));
            builder.UseSetting("Data:MaximumAssetBytes", "1024");
            builder.UseSetting("Git:StatePath", Path.Combine(fixture.DerivedRoot, "state")); builder.UseSetting("Git:SyncIntervalSeconds", "3600");
        }
    }

    [Fact]
    public async Task AssetAndLinkEndpointsRequireAuthAndRoundTripBytes()
    {
        using var fixture = new TestLibrary(); var token = new DeviceTokens(fixture.DeviceFile).Create("test");
        using var factory = new Factory(fixture); using var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync($"/v1/notes/{fixture.NoteId}/links")).StatusCode);
        var api = new LibraryApiClient(http); api.Connect(http.BaseAddress!.ToString(), token);
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2d, 1, 2 }; var id = Guid.NewGuid();
        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        var uploaded = await api.UploadAssetAsync(id, "paper.pdf", "application/pdf", new MemoryStream(bytes), bytes.Length, sha, default);
        Assert.Equal("application/pdf", uploaded.ContentType); Assert.Equal(bytes, await api.ReadAssetBytesAsync(id, default));
        Assert.Equal(fixture.NoteId, (await api.LinksAsync(fixture.NoteId, default)).NoteId);
        using var conflict = new MemoryStream([9]);
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => api.UploadAssetAsync(id, "other.pdf", "application/pdf", conflict, 1, "", default));
        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        await using var tooLarge = new MemoryStream(new byte[1025]);
        var rejected = await Assert.ThrowsAsync<HttpRequestException>(() =>
            api.UploadAssetAsync(Guid.NewGuid(), "large.bin", "application/octet-stream", tooLarge, tooLarge.Length, "", default));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejected.StatusCode);
    }
}

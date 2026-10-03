using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Slate.Lib.Api;
using Slate.Lib.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Headers;
using Xunit;

namespace Slate.Lib.Tests;

public sealed class ProductivityTests
{
    private sealed class Factory(TestLibrary fixture) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Library:RootPath", fixture.Root);
            builder.UseSetting("Authentication:DeviceFile", fixture.DeviceFile);
            builder.UseSetting("Data:DerivedPath", fixture.DerivedRoot);
            builder.UseSetting("Data:AssetsPath", Path.Combine(fixture.DerivedRoot, "assets"));
            builder.UseSetting("Git:StatePath", Path.Combine(fixture.DerivedRoot, "state"));
            builder.UseSetting("Git:SyncIntervalSeconds", "3600");
        }
    }
    [Fact]
    public async Task BulkHttpBoundaryAuthenticatesRevalidatesAndReportsCompletion()
    {
        using var fixture = new TestLibrary(); using var factory = new Factory(fixture); using var http = factory.CreateClient();
        var request = new BulkOperationRequest(Guid.NewGuid(), "move", ["Science/Databases/Note.md"], "Empty");
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/v1/library/bulk/preview", request)).StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new DeviceTokens(fixture.DeviceFile).Create("test"));
        var response = await http.PostAsJsonAsync("/v1/library/bulk/preview", request); response.EnsureSuccessStatusCode();
        var preview = (await response.Content.ReadFromJsonAsync<BulkPreview>())!;
        var before = await http.GetFromJsonAsync<BulkOperationResult>($"/v1/library/bulk/{preview.OperationId}"); Assert.Equal("prepared", before!.State);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await http.PostAsJsonAsync("/v1/library/bulk/apply", new BulkApplyRequest(preview.OperationId, "changed"))).StatusCode);
        var applied = await http.PostAsJsonAsync("/v1/library/bulk/apply", new BulkApplyRequest(preview.OperationId, preview.Fingerprint)); applied.EnsureSuccessStatusCode();
        var after = await http.GetFromJsonAsync<BulkOperationResult>($"/v1/library/bulk/{preview.OperationId}"); Assert.Equal("completed", after!.State);
        Assert.Equal("Empty/Note.md", (await http.GetFromJsonAsync<LibraryNote>($"/v1/notes/{fixture.NoteId}"))!.Path);
        var retry = await http.PostAsJsonAsync("/v1/library/bulk/apply", new BulkApplyRequest(preview.OperationId, preview.Fingerprint)); retry.EnsureSuccessStatusCode();
    }
    [Fact]
    public void SelectionSupportsToggleRangeAndSelectAll()
    {
        var selection = new ExplorerSelection(); string[] visible = ["a", "b", "c", "d"];
        selection.Select("b", visible); selection.Select("d", visible, range: true);
        Assert.Equal(["b", "c", "d"], selection.Paths.Order());
        selection.Select("c", visible, toggle: true); Assert.Equal(2, selection.Count);
        selection.Clear(); selection.SelectAll(visible); Assert.Equal(4, selection.Count);
        selection.Retain(["a"]); Assert.Equal("a", Assert.Single(selection.Paths));
    }
    [Theory]
    [InlineData("bold", "**text**")]
    [InlineData("italic", "*text*")]
    [InlineData("code", "`text`")]
    [InlineData("link", "[text](https://)")]
    [InlineData("wiki", "[[text]]")]
    public void MarkdownCommandsKeepSelection(string command, string expected)
    {
        var edit = MarkdownEditing.Transform("text", 0, 4, command);
        Assert.Equal(expected, edit.Text); Assert.Equal("text", edit.Text.Substring(edit.Cursor, edit.Length));
    }
    [Fact]
    public void MarkdownLineCommandsOperateOnWholeLines()
    {
        var edit = MarkdownEditing.Transform("first\nsecond", 2, 7, "task");
        Assert.Equal("- [ ] first\n- [ ] second", edit.Text);
        Assert.Equal("bold text", MarkdownEditing.Transform("", 0, 0, "bold").Text[2..^2]);
    }
    [Fact]
    public async Task CommandRegistryFiltersContextAndFuzzySearch()
    {
        var registry = new CommandRegistry(); var called = false;
        registry.Register(new("daily", "Daily Note", () => { called = true; return Task.CompletedTask; }));
        registry.Register(new("move", "Move", () => Task.CompletedTask, () => false));
        Assert.Equal("daily", Assert.Single(registry.Search("dln")).Id);
        Assert.False(await registry.ExecuteAsync("move")); Assert.True(await registry.ExecuteAsync("daily")); Assert.True(called);
    }
    [Fact]
    public void BulkMovePreservesIdsAndRebasesOutgoingLinks()
    {
        using var fixture = new TestLibrary(); var store = new LibraryStore(new(fixture.Root));
        var one = store.CreateNote(new("Empty", "a", InitialMarkdown: "# A\n\n[other](../Science/Databases/Note.md)"));
        var two = store.CreateNote(new("Empty", "b"));
        var preview = store.PreviewBulk(new(Guid.NewGuid(), "move", [one.Path, two.Path], "Science"));
        Assert.Equal(2, preview.NoteCount); Assert.Equal(one.Path, store.Read(one.Id).Path);
        var result = store.ApplyBulk(new(preview.OperationId, preview.Fingerprint));
        Assert.Equal("completed", result.State); Assert.Equal("Science/a.md", store.Read(one.Id).Path);
        Assert.Contains("(Databases/Note.md)", store.Read(one.Id).Markdown);
        var retry = store.ApplyBulk(new(preview.OperationId, preview.Fingerprint));
        Assert.Equal(result.OperationId, retry.OperationId); Assert.Equal(result.Items, retry.Items);
    }
    [Fact]
    public void CopySubtreeGivesNewIdsAndRedirectsInternalLinksButNotCode()
    {
        using var fixture = new TestLibrary(); var store = new LibraryStore(new(fixture.Root));
        var target = store.CreateNote(new("Empty", "b", "B"));
        var source = store.CreateNote(new("Empty", "a", InitialMarkdown: $"# A\n\n[[id:{target.Id:D}|B]]\n[b](b.md#section)\n\n```md\n[[id:{target.Id:D}]]\n[b](b.md)\n```"));
        var preview = store.PreviewBulk(new(Guid.NewGuid(), "duplicate", ["Empty", target.Path]));
        Assert.Single(preview.Items); store.ApplyBulk(new(preview.OperationId, preview.Fingerprint));
        var copyB = store.ReadPath("Empty copy/b.md"); var copyA = store.ReadPath("Empty copy/a.md");
        Assert.NotEqual(target.Id, copyB.Id); Assert.NotEqual(source.Id, copyA.Id);
        Assert.Contains($"[[id:{copyB.Id:D}|B]]", copyA.Markdown);
        Assert.Contains($"```md\n[[id:{target.Id:D}]]", copyA.Markdown);
        Assert.Contains("(b.md#section)", copyA.Markdown);
    }
    [Fact]
    public void CollisionAndDescendantDropAreRejectedBeforeAnyMutation()
    {
        using var fixture = new TestLibrary(); var store = new LibraryStore(new(fixture.Root));
        var a = store.CreateNote(new("Empty", "Note"));
        Assert.Throws<LibraryConflictException>(() => store.PreviewBulk(new(Guid.NewGuid(), "move", [a.Path, "Science/Databases/Note.md"], "Science/Databases")));
        Assert.Equal(a.Path, store.Read(a.Id).Path);
        Assert.Throws<ArgumentException>(() => store.PreviewBulk(new(Guid.NewGuid(), "move", ["Science"], "Science/Databases")));
    }
    [Fact]
    public void ApplyRejectsChangedSourceAndNewEmptyDirectory()
    {
        using var fixture = new TestLibrary(); var store = new LibraryStore(new(fixture.Root));
        var note = store.CreateNote(new("Empty", "test"));
        var preview = store.PreviewBulk(new(Guid.NewGuid(), "move", ["Empty"], "Science"));
        Directory.CreateDirectory(Path.Combine(fixture.Root, "Empty", "new folder"));
        Assert.Throws<LibraryPreconditionException>(() => store.ApplyBulk(new(preview.OperationId, preview.Fingerprint)));
        Assert.Equal(note.Path, store.Read(note.Id).Path);
        var second = store.PreviewBulk(new(Guid.NewGuid(), "delete", [note.Path]));
        store.Update(note.Id, new(note.Markdown + "changed", note.Revision));
        Assert.Throws<LibraryPreconditionException>(() => store.ApplyBulk(new(second.OperationId, second.Fingerprint)));
    }
    [Fact]
    public void InterruptedApplyRestoresWholeSelectionBeforeServing()
    {
        using var fixture = new TestLibrary(); var store = new LibraryStore(new(fixture.Root));
        var note = store.CreateNote(new("Empty", "test"));
        var preview = store.PreviewBulk(new(Guid.NewGuid(), "move", [note.Path], "Science"));
        var directory = Path.Combine(fixture.Root, ".slate", "operations", preview.OperationId.ToString("D"));
        var journal = Path.Combine(directory, "plan.json"); var json = JsonNode.Parse(File.ReadAllText(journal))!;
        json["state"] = "applying"; File.WriteAllText(journal, json.ToJsonString());
        Directory.CreateDirectory(Path.Combine(directory, "held"));
        File.Move(Path.Combine(fixture.Root, note.Path), Path.Combine(directory, "held", "0"));
        File.Move(Path.Combine(directory, "payload", "0"), Path.Combine(fixture.Root, "Science", "test.md"));
        var reopened = new LibraryStore(new(fixture.Root)); Assert.Equal(note.Path, reopened.Read(note.Id).Path);
        Assert.False(File.Exists(Path.Combine(fixture.Root, "Science", "test.md")));
    }
    [Fact]
    public void BulkDeleteRetainsOriginalSourceForRecovery()
    {
        using var fixture = new TestLibrary(); var store = new LibraryStore(new(fixture.Root));
        var note = store.CreateNote(new("Empty", "test"));
        var preview = store.PreviewBulk(new(Guid.NewGuid(), "delete", [note.Path]));
        store.ApplyBulk(new(preview.OperationId, preview.Fingerprint));
        Assert.Throws<FileNotFoundException>(() => store.Read(note.Id));
        Assert.Equal(note.Markdown, File.ReadAllText(Path.Combine(fixture.Root, ".slate", "operations", preview.OperationId.ToString("D"), "held", "0")));
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Slate.Lib.Api;
using Slate.Lib.Core;
using Xunit;

namespace Slate.Lib.Tests;

public sealed class SmartViewTests
{
    private static LibraryNote Add(LibraryStore store, string name, string metadata, string body = "Content")
    {
        var id = Guid.NewGuid();
        return store.CreateNote(new("Empty", name, Id: id, InitialMarkdown: $"---\nid: {id}\n{metadata}\n---\n\n# {name}\n\n{body}"));
    }

    [Fact]
    public void MetadataAndParsedCodeDriveViewsWithoutMatchingQuotedSyntax()
    {
        using var fixture = new TestLibrary(); using var index = new SearchIndex(Path.Combine(fixture.DerivedRoot, "smart"));
        var store = new LibraryStore(new(fixture.Root), index, null, new LinkIndex(), null);
        var question = Add(store, "Question", "type: question"); var answered = Add(store, "Answered", "type: question\nstatus: answered");
        var learning = Add(store, "Learning", "status: learning"); var review = Add(store, "Review", "status: needs-review");
        var diagram = Add(store, "Diagram", "", "```mermaid\ngraph LR\nA-->B\n```");
        var code = Add(store, "Code", "", "```csharp\nConsole.WriteLine(1);\n```");
        Add(store, "Quoted", "", "The word mermaid and ` ```csharp ` are not fences.");
        Assert.Equal(question.Id, Assert.Single(store.SmartView("unanswered", 0, 20).Results).Id);
        Assert.Equal(learning.Id, Assert.Single(store.SmartView("learning", 0, 20).Results).Id);
        Assert.Equal(review.Id, Assert.Single(store.SmartView("review", 0, 20).Results).Id);
        Assert.Equal(diagram.Id, Assert.Single(store.SmartView("diagrams", 0, 20).Results).Id);
        Assert.Equal(new[] { code.Id, diagram.Id }.Order(), store.SmartView("code", 0, 20).Results.Select(x => x.Id).Order());
        store.AnswerQuestion(question.Id, new("Done", question.Revision, DateTimeOffset.UtcNow));
        Assert.Empty(store.SmartView("unanswered", 0, 20).Results);
    }

    [Fact]
    public void OrphansUpdateWhenIncomingLinksAppearOrDisappearAndIgnoreSelfLinksAndTemplates()
    {
        using var fixture = new TestLibrary(); using var index = new SearchIndex(Path.Combine(fixture.DerivedRoot, "smart"));
        var links = new LinkIndex(); var store = new LibraryStore(new(fixture.Root), index, null, links, null);
        var target = Add(store, "Target", ""); var source = Add(store, "Source", "", $"[[id:{target.Id}]]");
        Assert.DoesNotContain(store.SmartView("orphans", 0, 50).Results, x => x.Id == target.Id);
        store.Update(source.Id, new($"---\nid: {source.Id}\n---\n[[id:{source.Id}]]", source.Revision));
        var orphans = store.SmartView("orphans", 0, 50).Results;
        Assert.Contains(orphans, x => x.Id == target.Id); Assert.Contains(orphans, x => x.Id == source.Id);
        store.CreateFolder(new("", "templates")); var template = store.CreateNote(new("templates", "Example"));
        Assert.DoesNotContain(store.SmartView("orphans", 0, 50).Results, x => x.Id == template.Id);
    }

    [Fact]
    public void RecentViewUsesKnownDatesAndPagesWithoutDuplicates()
    {
        using var fixture = new TestLibrary(); using var index = new SearchIndex(Path.Combine(fixture.DerivedRoot, "smart"));
        var store = new LibraryStore(new(fixture.Root), index, null);
        var old = Add(store, "Old", "created: 2020-01-01\nupdated: 2026-01-01T00:00:00Z");
        var newer = Add(store, "New", "created: 2025-01-01\nupdated: 2026-02-01T00:00:00+00:00");
        var latest = Add(store, "Latest", "created: 2026-03-01"); var unknown = Add(store, "Unknown", "created: sometime");
        var page = store.SmartView("modified", 0, 2); Assert.Equal(new[] { latest.Id, newer.Id }, page.Results.Select(x => x.Id));
        var next = store.SmartView("modified", 1, 2); Assert.Equal(old.Id, next.Results[0].Id);
        Assert.Empty(page.Results.Select(x => x.Id).Intersect(next.Results.Select(x => x.Id)));
        Assert.Null(NoteDocument.Parse(unknown.Markdown, unknown.Path).Created);
        Assert.Throws<SearchQueryException>(() => store.SmartView("modified", int.MaxValue, 20));
    }

    [Fact]
    public void ReopeningDerivedIndexPreservesViewsAndDoesNotReindexUnchangedNotes()
    {
        using var fixture = new TestLibrary(); var path = Path.Combine(fixture.DerivedRoot, "smart"); Guid id;
        using (var index = new SearchIndex(path)) { var store = new LibraryStore(new(fixture.Root), index, null); id = Add(store, "Question", "type: question\nstatus: open").Id; }
        using var reopened = new SearchIndex(path); var recovered = new LibraryStore(new(fixture.Root), reopened, null);
        Assert.Equal(id, Assert.Single(recovered.SmartView("unanswered", 0, 20).Results).Id);
        Assert.Equal(0, reopened.Reconcile(recovered.AllNotes()));
    }

    [Fact]
    public async Task SavedSearchRenameRetainsIdentityAndQueryAcrossRestart()
    {
        using var fixture = new TestLibrary(); var libraryId = Guid.NewGuid(); var id = Guid.NewGuid(); var store = new WorkspacePreferenceStore(fixture.DerivedRoot, libraryId);
        await store.SaveSearchAsync(id, "Postgres", "is:unanswered postgres");
        await store.SaveSearchAsync(id, "Database questions", "is:unanswered postgres");
        var reopened = new WorkspacePreferenceStore(fixture.DerivedRoot, libraryId); var saved = Assert.Single((await reopened.ReadAsync()).Searches);
        Assert.Equal(id, saved.Id); Assert.Equal("Database questions", saved.Name); Assert.Equal("is:unanswered postgres", saved.Query);
        await reopened.DeleteSearchAsync(id); Assert.Empty((await store.ReadAsync()).Searches);
    }

    private sealed class Factory(TestLibrary fixture) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Library:RootPath", fixture.Root); builder.UseSetting("Authentication:DeviceFile", fixture.DeviceFile);
            builder.UseSetting("Data:DerivedPath", fixture.DerivedRoot); builder.UseSetting("Data:AssetsPath", Path.Combine(fixture.DerivedRoot, "assets"));
            builder.UseSetting("Git:StatePath", Path.Combine(fixture.DerivedRoot, "state")); builder.UseSetting("Git:SyncIntervalSeconds", "3600");
        }
    }
    [Fact]
    public async Task SmartViewsRequireAuthenticationAndUseNormalPagingContracts()
    {
        using var fixture = new TestLibrary(); using var factory = new Factory(fixture); using var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/v1/views/modified")).StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new DeviceTokens(fixture.DeviceFile).Create("test"));
        var response = await http.GetAsync("/v1/views/modified?page=0&pageSize=1"); Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<SearchPage>(); Assert.Single(result!.Results); Assert.True(result.Total > 0);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("/v1/views/unknown")).StatusCode);
    }
}

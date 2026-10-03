using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Slate.Lib.Api;
using Slate.Lib.Core;
using Xunit;

namespace Slate.Lib.Tests;

public sealed class KnowledgeWorkflowTests
{
    [Fact]
    public void AnswerPreservesUnknownMetadataCommentsAndQuestionBody()
    {
        var id = Guid.NewGuid(); const string created = "2025-01-02T03:04:05Z";
        var source = $"---\nid: {id}\ntype: question\nstatus: open # retained\ncreated: {created}\ncustom:\n  nested: [one, two]\n---\n\n# Question\n\nOriginal context.\n";
        var answered = KnowledgeWorkflows.Answer(source, "An answer.\n\nWith more context.", DateTimeOffset.Parse("2026-10-02T10:00:00Z"));
        Assert.Contains("status: \"answered\" # retained", answered); Assert.Contains("custom:\n  nested: [one, two]", answered);
        Assert.Contains("created: " + created, answered); Assert.Contains("Original context.\n\n## Answer", answered);
        Assert.Equal(id, NoteDocument.Parse(answered, "question.md").Id);
    }
    [Fact]
    public void AnswerRejectsStaleRevisionAndPublishesSearchChange()
    {
        using var fixture = new TestLibrary(); using var search = new SearchIndex(Path.Combine(fixture.DerivedRoot, "search"));
        var store = new LibraryStore(new(fixture.Root), search, null); var id = Guid.NewGuid();
        var question = store.CreateNote(new("Empty", "question", Id: id, InitialMarkdown: KnowledgeWorkflows.Question(id, "Postgres question", "Why?", DateTimeOffset.UtcNow)));
        Assert.Contains(store.Search("is:unanswered", 0, 20).Results, x => x.Id == id);
        var answer = store.AnswerQuestion(id, new("Because…", question.Revision, DateTimeOffset.UtcNow));
        Assert.DoesNotContain(store.Search("is:unanswered", 0, 20).Results, x => x.Id == id);
        Assert.Throws<LibraryPreconditionException>(() => store.AnswerQuestion(id, new("Other answer", question.Revision, DateTimeOffset.UtcNow)));
        Assert.Equal("answered", NoteDocument.Parse(answer.Markdown, answer.Path).Status);
    }
    [Fact]
    public async Task ConcurrentDailyCreationUsesOneCanonicalLocalDatePath()
    {
        using var fixture = new TestLibrary(); var store = new LibraryStore(new(fixture.Root)); var date = new DateOnly(2026, 10, 2);
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => store.Daily(new(date)))));
        Assert.Single(results.Select(x => x.Id).Distinct()); Assert.All(results, note => Assert.Equal("daily/2026-10-02.md", note.Path));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(fixture.Root, "daily"), "*.md"));
    }
    [Fact]
    public void TemplateAllocatesNewIdentityKeepsUnknownMetadataAndExpandsOnlyBodyPlaceholders()
    {
        var oldId = Guid.NewGuid(); var newId = Guid.NewGuid();
        var source = $"---\nid: {oldId}\ncustom: '{{date}}'\nstatus: learning\n---\n\n# {{{{title}}}}\n\n{{{{date}}}} {{{{unknown}}}}\n";
        var applied = KnowledgeWorkflows.Template(source, newId, "New title", new DateOnly(2026, 10, 2), DateTimeOffset.UtcNow);
        Assert.Equal(newId, NoteDocument.Parse(applied, "copy.md").Id); Assert.Contains("# New title", applied); Assert.Contains("2026-10-02 {{unknown}}", applied);
        Assert.Contains("custom: '{date}'", applied); Assert.Contains("status: learning", applied); Assert.Contains(oldId.ToString(), source);
    }
    [Fact]
    public void LinkCaptureIsPortableAndRejectsActiveSchemes()
    {
        var source = KnowledgeWorkflows.Link(Guid.NewGuid(), "https://example.com/path?q=1", "Reference", "My observation", DateTimeOffset.UtcNow);
        Assert.Contains("<https://example.com/path?q=1>", source); Assert.Contains("My observation", source); Assert.Equal("link", NoteDocument.Parse(source, "link.md").Type);
        Assert.Throws<ArgumentException>(() => KnowledgeWorkflows.Link(Guid.NewGuid(), "javascript:alert(1)", null, null, DateTimeOffset.UtcNow));
    }
    [Fact]
    public void CaptureAppendRebasesAttachmentsAndRetainsBothSourcesUntilReviewedApply()
    {
        using var fixture = new TestLibrary(); var store = new LibraryStore(new(fixture.Root));
        var asset = Guid.NewGuid(); var capture = store.CreateNote(new("Empty", "capture", InitialMarkdown: $"A thought\n\n![image](../.assets/{asset}.png)\n")); var target = store.Read(fixture.NoteId);
        var preview = store.PreviewCaptureAppend(target.Id, capture.Id); Assert.Equal(target.Markdown, store.Read(target.Id).Markdown);
        Assert.Contains($"../../.assets/{asset}.png", preview.ProposedMarkdown);
        store.Update(target.Id, new(target.Markdown + "External change", target.Revision));
        Assert.Throws<LibraryPreconditionException>(() => store.AppendCapture(target.Id, new(capture.Id, preview.DestinationRevision, preview.CaptureRevision)));
        preview = store.PreviewCaptureAppend(target.Id, capture.Id);
        var appended = store.AppendCapture(target.Id, new(capture.Id, preview.DestinationRevision, preview.CaptureRevision));
        Assert.Contains("External change", appended.Markdown); Assert.Contains("A thought", appended.Markdown); Assert.Equal(capture, store.Read(capture.Id));
    }
    [Fact]
    public async Task MultipleSharedFilesSurviveRestartAndPromotionDoesNotOverwriteAnEditedDraft()
    {
        using var fixture = new TestLibrary(); var inbox = new ShareInboxStore(fixture.DerivedRoot);
        var share = await inbox.BeginAsync(new("Shared text", false));
        share = await inbox.AddFileAsync(share, new MemoryStream([1, 2, 3]), "first.txt", "text/plain");
        share = await inbox.AddFileAsync(share, new MemoryStream([4, 5]), "second.pdf", "application/pdf");
        // Process dies before marking the batch complete: the copied bytes remain discoverable.
        var restarted = new ShareInboxStore(fixture.DerivedRoot); var recovered = Assert.Single(await restarted.ReadAsync());
        Assert.True(recovered.Complete); Assert.NotNull(recovered.Error); Assert.Equal(2, recovered.Assets!.Count);
        var state = new ClientStateStore(fixture.DerivedRoot, fixture.DerivedRoot, Guid.NewGuid(), 1024 * 1024);
        var edited = new DraftRecord(share.Id, DraftKind.Share, "already edited", DateTimeOffset.UtcNow); await state.SaveDraftAsync(edited);
        await restarted.PromoteAsync(recovered, state); Assert.Equal(edited, await state.ReadDraftAsync(share.Id)); Assert.Empty(await restarted.ReadAsync());
        Assert.All(recovered.Assets, asset => Assert.True(File.Exists(asset.LocalPath)));
    }
    [Fact]
    public async Task ReceivingShareDoesNotAuthorizeAutomaticSubmission()
    {
        using var fixture = new TestLibrary(); var inbox = new ShareInboxStore(fixture.DerivedRoot); var share = await inbox.BeginAsync(new("Shared", false)); await inbox.SaveAsync(share with { Complete = true });
        var state = new ClientStateStore(fixture.DerivedRoot, fixture.DerivedRoot, Guid.NewGuid(), 1024 * 1024); await inbox.PromoteAsync(share, state);
        var called = false;
        var submitted = await new PendingCaptureProcessor(state).RetryAsync((request, token) => { called = true; return Task.FromResult(new LibraryNote(request.CaptureId, "inbox/x.md", "x", "x", "x")); });
        Assert.False(called); Assert.Equal(0, submitted); Assert.Single(await state.ReadDraftsAsync());
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
    public async Task DailyAndAnswerWorkThroughAuthenticatedHttp()
    {
        using var fixture = new TestLibrary(); using var factory = new Factory(fixture); using var http = factory.CreateClient();
        var date = new DailyNoteRequest(new(2026, 10, 2)); Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/v1/workflows/daily", date)).StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new DeviceTokens(fixture.DeviceFile).Create("test"));
        var response = await http.PostAsJsonAsync("/v1/workflows/daily", date); response.EnsureSuccessStatusCode();
        var note = (await response.Content.ReadFromJsonAsync<LibraryNote>())!; Assert.Equal("daily/2026-10-02.md", note.Path);
        var id = Guid.NewGuid(); var create = await http.PostAsJsonAsync("/v1/notes", new CreateNoteRequest("Empty", "question", Id: id, InitialMarkdown: KnowledgeWorkflows.Question(id, "Question", "Context", DateTimeOffset.UtcNow))); create.EnsureSuccessStatusCode();
        var question = (await create.Content.ReadFromJsonAsync<LibraryNote>())!;
        var answered = await http.PostAsJsonAsync($"/v1/notes/{id}/answer", new AnswerQuestionRequest("Answer", question.Revision, DateTimeOffset.UtcNow)); answered.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await http.PostAsJsonAsync($"/v1/notes/{id}/answer", new AnswerQuestionRequest("Stale", question.Revision, DateTimeOffset.UtcNow))).StatusCode);
    }
}

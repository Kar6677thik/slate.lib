using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Slate.Lib.Api;
using Slate.Lib.Core;
using Xunit;

namespace Slate.Lib.Tests;

public sealed class NotificationStoreTests
{
    private static readonly Guid Web = Guid.NewGuid(), Mcp = Guid.NewGuid();

    private static LibraryNote Note(string title) => new(Guid.NewGuid(), "inbox/" + title + ".md", title, "# " + title, "\"r\"");

    [Fact]
    public void BundlesPageCreationsAndKeepsEachCommentSeparate()
    {
        using var fixture = new TestLibrary();
        var now = DateTimeOffset.Parse("2026-11-10T10:00:00Z");
        var store = new NotificationStore(Path.Combine(fixture.DerivedRoot, "notifications.json"), () => now);
        var agent = new NotificationActor(NotificationActor.Agent, "Claude", Mcp);

        var first = Note("one");
        store.NotesAdded(agent, "created", [first]);
        now = now.AddMinutes(3);
        store.NotesAdded(agent, "created", Enumerable.Range(0, 120).Select(i => Note("bulk-" + i)));
        store.NotesAdded(agent, "created", [first]); // An idempotent repeat is never announced twice.

        var page = store.List(Web, includeOwn: false, kind: null, 0);
        var bundle = Assert.Single(page.Items).Notification;
        Assert.Equal(121, bundle.Notes!.Count);
        Assert.Equal(NotificationStore.MaxNotesPerNotification, bundle.Notes.Notes.Count);
        Assert.Equal(1, page.Unread);

        // Outside the bundling window a new entry starts; a keyed bulk operation is always one entry and idempotent.
        now = now.AddMinutes(30);
        store.NotesAdded(agent, "created", [Note("later")]);
        store.NotesAdded(agent, "copied", [Note("a"), Note("b")], "bulk:1");
        store.NotesAdded(agent, "copied", [Note("c")], "bulk:1");
        Assert.Equal(3, store.List(Web, false, "notes", 0).Total);

        var thread = new CommentThreadView(Guid.NewGuid(), new(CommentAnchor.Text, "WAL"), "open", now, now, null,
            [new(Guid.NewGuid(), new(CommentAuthor.User, "You"), "What is it?", now), new(Guid.NewGuid(), new(CommentAuthor.Agent, "Claude"), "Write-ahead log.", now)],
            "anchored", 3, "WAL", false);
        var view = new NoteCommentsView(first.Id, "PostgreSQL", "db/PostgreSQL.md", "\"r\"", [thread]);
        store.CommentAdded(agent, view, thread, thread.Comments[1]);
        store.CommentAdded(agent, view, thread, thread.Comments[1]);
        var comment = Assert.Single(store.List(Web, false, "comment", 0).Items).Notification;
        Assert.Equal("Claude", comment.Actor.Name);
        Assert.True(comment.Comment!.IsReply);
        Assert.Equal("WAL", comment.Comment.Quote);
        Assert.Equal(first.Id, comment.Comment.NoteId);
    }

    [Fact]
    public void HidesTheViewersOwnActivityAndTracksReadState()
    {
        using var fixture = new TestLibrary();
        var store = new NotificationStore(Path.Combine(fixture.DerivedRoot, "notifications.json"));
        store.NotesAdded(new(NotificationActor.Device, "Web", Web), "created", [Note("mine")]);
        store.NotesAdded(new(NotificationActor.Device, "Android", Mcp), "captured", [Note("phone")]);

        var page = store.List(Web, includeOwn: false, kind: null, 0);
        Assert.Equal("Android", Assert.Single(page.Items).Notification.Actor.Name);
        Assert.Equal(2, store.List(Web, includeOwn: true, kind: null, 0).Total);
        Assert.Equal(1, store.Summary(Web).Unread);

        Assert.Equal(0, store.MarkRead(Web, new(All: true)).Unread);
        Assert.False(store.List(Web, false, null, 0).Items[0].Unread);
        store.NotesAdded(new(NotificationActor.Device, "Android", Mcp), "captured", [Note("next")]);
        Assert.Equal(2, store.List(Web, false, null, 0).Total); // A read bundle is never reopened.
        Assert.Throws<ArgumentException>(() => store.MarkRead(Web, new()));
    }

    [Fact]
    public void ADamagedFeedIsTreatedAsEmpty()
    {
        using var fixture = new TestLibrary();
        var file = Path.Combine(fixture.DerivedRoot, "notifications.json");
        Directory.CreateDirectory(fixture.DerivedRoot);
        File.WriteAllText(file, "{ not json");
        var store = new NotificationStore(file);
        Assert.Empty(store.List(null, true, null, 0).Items);
        store.NotesAdded(new(NotificationActor.Device, "Android", Mcp), "captured", [Note("x")]);
        Assert.Single(store.List(null, true, null, 0).Items);
    }
}

public sealed class CommentVisibilityTests
{
    [Fact]
    public void ResolvingKeepsAThreadAndOnlyHidingHidesIt()
    {
        using var fixture = new TestLibrary();
        var paths = new LibraryPaths(fixture.Root);
        var library = new LibraryStore(paths);
        var comments = new NoteCommentStore(paths, library);
        var thread = comments.Create(fixture.NoteId, new(new(CommentAnchor.Page), "Keep me"));

        var resolved = comments.SetStatus(fixture.NoteId, thread.Id, new(CommentThread.Resolved));
        Assert.False(resolved.Hidden);
        Assert.Single(comments.Read(fixture.NoteId).Threads);

        var hidden = comments.SetHidden(fixture.NoteId, thread.Id, new(true));
        Assert.True(hidden.Hidden);
        Assert.Equal(CommentThread.Resolved, hidden.Status);
        Assert.True(comments.Read(fixture.NoteId).Threads.Single().Hidden);

        var reopened = comments.Reply(fixture.NoteId, thread.Id, new("Actually, one more question"));
        Assert.False(reopened.Hidden);
        Assert.Equal(CommentThread.Open, reopened.Status);
        Assert.True(reopened.AwaitingReply);
        Assert.False(comments.SetHidden(fixture.NoteId, thread.Id, new(true)).AwaitingReply);
    }
}

public sealed class NotificationApiTests
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
    public async Task OtherDevicesActivityReachesTheFeedAndBulkCopiesAreOneEntry()
    {
        using var fixture = new TestLibrary();
        var tokens = new DeviceTokens(fixture.DeviceFile);
        var webToken = tokens.Create("web");
        var mcpToken = tokens.Create("mcp");
        using var factory = new Factory(fixture);
        using var web = factory.CreateClient();
        web.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", webToken);
        using var mcp = factory.CreateClient();
        mcp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mcpToken);

        // The owner asks from the web: that is their own activity and is not announced back to them.
        var question = await (await web.PostAsJsonAsync($"/v1/notes/{fixture.NoteId}/comments", new CreateCommentThreadRequest(new(CommentAnchor.Page), "Why?"))).Content.ReadFromJsonAsync<CommentThreadView>();
        Assert.Equal(0, (await web.GetFromJsonAsync<NotificationSummary>("/v1/notifications/summary"))!.Unread);

        // The agent answers with a page created under its own name, then replies with the page linked.
        using var create = new HttpRequestMessage(HttpMethod.Post, "/v1/notes") { Content = JsonContent.Create(new CreateNoteRequest("Science", "Answer.md", "Answer", InitialMarkdown: "# Answer\n")) };
        create.Headers.Add("X-Slate-Actor", "Claude");
        var answer = await (await mcp.SendAsync(create)).Content.ReadFromJsonAsync<LibraryNote>();
        (await mcp.PostAsJsonAsync($"/v1/notes/{fixture.NoteId}/comments/{question!.Id}/replies", new ReplyToCommentRequest("See the page.", new(CommentAuthor.Agent, "Claude"), answer!.Id))).EnsureSuccessStatusCode();

        var feed = await web.GetFromJsonAsync<NotificationPage>("/v1/notifications");
        Assert.Equal(2, feed!.Unread);
        var reply = feed.Items.Single(item => item.Notification.Kind == "comment").Notification;
        Assert.Equal(answer.Id, reply.Comment!.LinkedNote!.NoteId);
        Assert.Equal(question.Id, reply.Comment.ThreadId);
        var pages = feed.Items.Single(item => item.Notification.Kind == "notes").Notification;
        Assert.Equal("Claude", pages.Actor.Name);
        Assert.Equal(answer.Id, Assert.Single(pages.Notes!.Notes).NoteId);
        Assert.Single((await web.GetFromJsonAsync<NotificationPage>("/v1/notifications?includeOwn=true&kind=comment"))!.Items, item => item.FromThisDevice);

        // A bulk duplicate of a folder from another device is one notification for all of its notes.
        fixture.Write("Batch/A.md", $"---\nid: {Guid.NewGuid()}\n---\n# A\n");
        fixture.Write("Batch/B.md", $"---\nid: {Guid.NewGuid()}\n---\n# B\n");
        (await mcp.PostAsync("/v1/library/refresh", null)).EnsureSuccessStatusCode();
        var operationId = Guid.NewGuid();
        var preview = await (await mcp.PostAsJsonAsync("/v1/library/bulk/preview", new BulkOperationRequest(operationId, "duplicate", ["Batch"]))).Content.ReadFromJsonAsync<BulkPreview>();
        (await mcp.PostAsJsonAsync("/v1/library/bulk/apply", new BulkApplyRequest(operationId, preview!.Fingerprint))).EnsureSuccessStatusCode();
        (await mcp.PostAsJsonAsync("/v1/library/bulk/apply", new BulkApplyRequest(operationId, preview.Fingerprint))).EnsureSuccessStatusCode();
        var bulk = (await web.GetFromJsonAsync<NotificationPage>("/v1/notifications?kind=notes"))!.Items.Where(item => item.Notification.Notes!.Source == "duplicated").ToArray();
        Assert.Equal(2, Assert.Single(bulk).Notification.Notes!.Count);

        var read = await web.PostAsJsonAsync("/v1/notifications/read", new MarkNotificationsReadRequest(All: true));
        Assert.Equal(0, (await read.Content.ReadFromJsonAsync<NotificationSummary>())!.Unread);
        Assert.Equal(HttpStatusCode.BadRequest, (await web.GetAsync("/v1/notifications?kind=other")).StatusCode);

        var hide = await web.PostAsJsonAsync($"/v1/notes/{fixture.NoteId}/comments/{question.Id}/visibility", new CommentThreadVisibilityRequest(true));
        Assert.True((await hide.Content.ReadFromJsonAsync<CommentThreadView>())!.Hidden);
    }
}

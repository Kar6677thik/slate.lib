using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Slate.Lib.Api;
using Slate.Lib.Core;
using Xunit;

namespace Slate.Lib.Tests;

public sealed class CommentAnchorTests
{
    private const string Source = "---\nid: 00000000-0000-0000-0000-000000000001\n---\n# MVCC\n\nPostgreSQL keeps **old row versions** until VACUUM runs.\nEvery row versions table needs care.\n";

    [Fact]
    public void LocatesAPlainQuoteAndReportsItsLine()
    {
        var location = CommentAnchors.Locate(Source, new(CommentAnchor.Text, "until VACUUM runs"));
        Assert.NotNull(location);
        Assert.Equal(6, location.Line);
        Assert.Equal("PostgreSQL keeps **old row versions** until VACUUM runs.", location.Context);
        Assert.Equal("until VACUUM runs", Source[location.Start..location.End]);
    }

    [Fact]
    public void LocatesAQuoteThatSpansMarkdownFormatting()
    {
        var location = CommentAnchors.Locate(Source, new(CommentAnchor.Text, "keeps old row versions until"));
        Assert.NotNull(location);
        Assert.Equal(6, location.Line);
        Assert.Equal("keeps **old row versions** until", Source[location.Start..location.End]);
    }

    [Fact]
    public void UsesPrefixAndSuffixToChooseBetweenRepeatedPassages()
    {
        var location = CommentAnchors.Locate(Source, new(CommentAnchor.Text, "row versions", "Every ", " table"));
        Assert.NotNull(location);
        Assert.Equal(7, location.Line);
    }

    [Fact]
    public void ReportsNoLocationWhenThePassageWasEditedAway()
    {
        Assert.Null(CommentAnchors.Locate(Source, new(CommentAnchor.Text, "autovacuum thresholds")));
        Assert.Null(CommentAnchors.Locate(Source, new(CommentAnchor.Page)));
    }
}

public sealed class CommentStoreTests
{
    [Fact]
    public void ThreadsFollowTheNoteAndKeepMarkdownUntouched()
    {
        using var fixture = new TestLibrary();
        var paths = new LibraryPaths(fixture.Root);
        var library = new LibraryStore(paths);
        var comments = new NoteCommentStore(paths, library);
        var before = library.Read(fixture.NoteId).Markdown;

        var question = comments.Create(fixture.NoteId, new(new(CommentAnchor.Text, "From disk", "real note\n\n", "."), "What does *from disk* mean here?"));
        Assert.Equal("anchored", question.AnchorState);
        Assert.Equal(6, question.Line);
        Assert.True(question.AwaitingReply);
        Assert.Equal(CommentAuthor.User, Assert.Single(question.Comments).Author.Kind);

        var page = comments.Create(fixture.NoteId, new(new(CommentAnchor.Page), "Great note for future me."));
        Assert.Equal("page", page.AnchorState);

        Assert.Equal(before, library.Read(fixture.NoteId).Markdown);
        Assert.True(File.Exists(Path.Combine(fixture.Root, ".slate", "comments", fixture.NoteId.ToString("D") + ".json")));

        library.Move(new("Science/Databases/Note.md", "Empty"));
        var moved = comments.Read(fixture.NoteId);
        Assert.Equal("Empty/Note.md", moved.Path);
        Assert.Equal(2, moved.Threads.Count);
    }

    [Fact]
    public void AgentRepliesAnswerWithALinkedNoteAndOwnerControlsResolution()
    {
        using var fixture = new TestLibrary();
        var paths = new LibraryPaths(fixture.Root);
        var library = new LibraryStore(paths);
        var comments = new NoteCommentStore(paths, library);
        var thread = comments.Create(fixture.NoteId, new(new(CommentAnchor.Text, "real note"), "Explain this?"));
        var answer = library.CreateNote(new("Science", "Explainer.md", "Explainer", InitialMarkdown: "# Explainer\n\nA full answer.\n"));

        var replied = comments.Reply(fixture.NoteId, thread.Id, new("I wrote a full page for this.", new(CommentAuthor.Agent, "Claude"), answer.Id));
        Assert.False(replied.AwaitingReply);
        Assert.Equal(CommentThread.Open, replied.Status);
        var reply = replied.Comments[^1];
        Assert.Equal("Claude", reply.Author.Name);
        Assert.Equal(answer.Id, reply.LinkedNote!.NoteId);

        library.Rename(new("Science/Explainer.md", "Answer.md"));
        Assert.Equal("Science/Answer.md", comments.Read(fixture.NoteId).Threads[0].Comments[^1].LinkedNote!.Path);

        var inbox = comments.Inbox("open", awaitingReply: true, 0);
        Assert.Empty(inbox.Items);
        comments.Reply(fixture.NoteId, thread.Id, new("Thanks — and what about X?"));
        Assert.Single(comments.Inbox("open", awaitingReply: true, 0).Items);

        var resolved = comments.SetStatus(fixture.NoteId, thread.Id, new(CommentThread.Resolved));
        Assert.NotNull(resolved.ResolvedAt);
        Assert.Empty(comments.Inbox("open", awaitingReply: false, 0).Items);
        Assert.Single(comments.Inbox("resolved", awaitingReply: false, 0).Items);

        comments.DeleteComment(fixture.NoteId, thread.Id, resolved.Comments[^1].Id);
        Assert.Equal(2, comments.Read(fixture.NoteId).Threads[0].Comments.Count);
        comments.DeleteComment(fixture.NoteId, thread.Id, resolved.Comments[0].Id);
        Assert.Empty(comments.Read(fixture.NoteId).Threads);
    }

    [Fact]
    public void ReportsDetachedThreadsAndRejectsInvalidInput()
    {
        using var fixture = new TestLibrary();
        var paths = new LibraryPaths(fixture.Root);
        var library = new LibraryStore(paths);
        var comments = new NoteCommentStore(paths, library);
        var thread = comments.Create(fixture.NoteId, new(new(CommentAnchor.Text, "From disk"), "Why?"));
        var note = library.Read(fixture.NoteId);
        library.Update(note.Id, new(note.Markdown.Replace("From disk.", "Rewritten entirely."), note.Revision));
        var view = comments.Read(fixture.NoteId).Threads.Single(item => item.Id == thread.Id);
        Assert.Equal("detached", view.AnchorState);
        Assert.Equal("From disk", view.Anchor.Exact);

        Assert.Throws<ArgumentException>(() => comments.Create(fixture.NoteId, new(new(CommentAnchor.Text, ""), "Empty anchor")));
        Assert.Throws<ArgumentException>(() => comments.Create(fixture.NoteId, new(new(CommentAnchor.Page), "  ")));
        Assert.Throws<ArgumentException>(() => comments.Create(fixture.NoteId, new(new("paragraph"), "Unknown anchor")));
        Assert.Throws<ArgumentException>(() => comments.Reply(fixture.NoteId, thread.Id, new("x", new("robot", "R2"))));
        Assert.Throws<FileNotFoundException>(() => comments.Reply(fixture.NoteId, Guid.NewGuid(), new("No such thread")));
        Assert.Throws<FileNotFoundException>(() => comments.Create(Guid.NewGuid(), new(new(CommentAnchor.Page), "No such note")));
        Assert.Throws<FileNotFoundException>(() => comments.Reply(fixture.NoteId, thread.Id, new("", LinkedNoteId: Guid.NewGuid())));
    }

    [Fact]
    public void RejectsMalformedCommentFiles()
    {
        var id = Guid.NewGuid();
        Assert.Throws<InvalidDataException>(() => NoteCommentStore.Parse("not json", id));
        Assert.Throws<InvalidDataException>(() => NoteCommentStore.Parse($$"""{"schemaVersion":1,"noteId":"{{Guid.NewGuid()}}","threads":[]}""", id));
        Assert.Throws<InvalidDataException>(() => NoteCommentStore.Parse($$"""{"schemaVersion":2,"noteId":"{{id}}","threads":[]}""", id));
        Assert.Empty(NoteCommentStore.Parse($$"""{"schemaVersion":1,"noteId":"{{id}}","threads":[]}""", id).Threads);
        Assert.True(NoteCommentStore.IsCommentPath($".slate/comments/{id:D}.json"));
        Assert.False(NoteCommentStore.IsCommentPath($".slate/comments/{id:D}.json.bak"));
        Assert.False(NoteCommentStore.IsCommentPath($".slate/comments/nested/{id:D}.json"));
        Assert.False(NoteCommentStore.IsCommentPath($".slate/comments/{id.ToString("D").ToUpperInvariant()}.json"));
    }
}

public sealed class CommentGitTests
{
    [Fact]
    public async Task CommentsAreCommittedPushedAndImportedFromAnotherClone()
    {
        using var fixture = new TestLibrary();
        var paths = new LibraryPaths(fixture.Root);
        var git = new GitSyncService(paths, Options.Create(new GitOptions { StatePath = Path.Combine(fixture.DerivedRoot, "state"), SyncIntervalSeconds = 3600 }), NullLogger<GitSyncService>.Instance);
        await git.InitializeAsync();
        var library = new LibraryStore(paths, null, git);
        var comments = new NoteCommentStore(paths, library, git);
        var thread = comments.Create(fixture.NoteId, new(new(CommentAnchor.Text, "real note"), "Synced question"));
        await git.FlushAsync(library);
        Assert.Contains($".slate/comments/{fixture.NoteId:D}.json", Git(fixture.Root, "ls-files"));

        var bare = Path.Combine(fixture.DerivedRoot, "remote.git");
        var clone = Path.Combine(fixture.DerivedRoot, "external");
        Git(fixture.DerivedRoot, "init", "--bare", "-b", "main", bare);
        Git(fixture.Root, "remote", "add", "origin", bare);
        Assert.Equal("Synced", (await git.SynchronizeAsync(library)).Status.State);

        Git(fixture.DerivedRoot, "clone", "--branch", "main", bare, clone);
        Git(clone, "config", "user.name", "Other Device");
        Git(clone, "config", "user.email", "other@example.invalid");
        var cloneComments = new NoteCommentStore(new LibraryPaths(clone), new LibraryStore(new LibraryPaths(clone)));
        cloneComments.Reply(fixture.NoteId, thread.Id, new("Answered on another device", new(CommentAuthor.Agent, "ChatGPT")));
        Git(clone, "add", "-A");
        Git(clone, "commit", "-m", "Answer comment");
        Git(clone, "push", "origin", "main");

        Assert.Equal("Synced", (await git.SynchronizeAsync(library)).Status.State);
        var imported = comments.Read(fixture.NoteId).Threads.Single();
        Assert.Equal("ChatGPT", imported.Comments[^1].Author.Name);

        File.WriteAllText(Path.Combine(clone, ".slate", "comments", fixture.NoteId.ToString("D") + ".json"), "{ broken", new UTF8Encoding(false));
        Git(clone, "commit", "-am", "Corrupt comments");
        Git(clone, "push", "origin", "main");
        Assert.Equal("Error", (await git.SynchronizeAsync(library)).Status.State);
        Assert.Equal("ChatGPT", comments.Read(fixture.NoteId).Threads.Single().Comments[^1].Author.Name);
    }

    private static string Git(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd(); var error = process.StandardError.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"git {arguments[0]} failed: {error}");
        return output;
    }
}

public sealed class CommentApiTests
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
    public async Task EndpointsCreateReplyResolveListAndDelete()
    {
        using var fixture = new TestLibrary();
        var token = new DeviceTokens(fixture.DeviceFile).Create("test");
        using var factory = new Factory(fixture);
        using var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync($"/v1/notes/{fixture.NoteId}/comments")).StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var created = await http.PostAsJsonAsync($"/v1/notes/{fixture.NoteId}/comments", new CreateCommentThreadRequest(new(CommentAnchor.Text, "From disk"), "What is this?"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var thread = (await created.Content.ReadFromJsonAsync<CommentThreadView>())!;

        var inbox = await http.GetFromJsonAsync<CommentInboxPage>("/v1/comments?awaitingReply=true");
        Assert.Equal(fixture.NoteId, Assert.Single(inbox!.Items).NoteId);

        var reply = await http.PostAsJsonAsync($"/v1/notes/{fixture.NoteId}/comments/{thread.Id}/replies", new ReplyToCommentRequest("It means the file on disk.", new(CommentAuthor.Agent, "Claude")));
        reply.EnsureSuccessStatusCode();
        Assert.Empty((await http.GetFromJsonAsync<CommentInboxPage>("/v1/comments?awaitingReply=true"))!.Items);

        var status = await http.PostAsJsonAsync($"/v1/notes/{fixture.NoteId}/comments/{thread.Id}/status", new CommentThreadStatusRequest("resolved"));
        Assert.Equal("resolved", (await status.Content.ReadFromJsonAsync<CommentThreadView>())!.Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync($"/v1/notes/{fixture.NoteId}/comments/{thread.Id}/status", new CommentThreadStatusRequest("maybe"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsJsonAsync($"/v1/notes/{fixture.NoteId}/comments/{Guid.NewGuid()}/replies", new ReplyToCommentRequest("x"))).StatusCode);

        var view = await http.GetFromJsonAsync<NoteCommentsView>($"/v1/notes/{fixture.NoteId}/comments");
        Assert.Equal(2, Assert.Single(view!.Threads).Comments.Count);
        var delete = await http.PostAsync($"/v1/notes/{fixture.NoteId}/comments/{thread.Id}/comments/{thread.Comments[0].Id}/delete", null);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty((await http.GetFromJsonAsync<NoteCommentsView>($"/v1/notes/{fixture.NoteId}/comments"))!.Threads);
    }
}

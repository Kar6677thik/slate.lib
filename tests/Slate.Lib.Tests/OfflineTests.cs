using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Slate.Lib.Api;
using Slate.Lib.Core;
using Xunit;

namespace Slate.Lib.Tests;

public sealed class OfflineTests
{
    private sealed class Factory(TestLibrary f) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Library:RootPath", f.Root); builder.UseSetting("Authentication:DeviceFile", f.DeviceFile);
            builder.UseSetting("Data:DerivedPath", f.DerivedRoot); builder.UseSetting("Data:AssetsPath", Path.Combine(f.DerivedRoot, "assets"));
            builder.UseSetting("Git:StatePath", Path.Combine(f.DerivedRoot, "git")); builder.UseSetting("Git:SyncIntervalSeconds", "3600");
        }
    }
    private static LibraryApiClient Api(HttpClient http, TestLibrary f) { var api = new LibraryApiClient(http); api.Connect("http://localhost", new DeviceTokens(f.DeviceFile).Create("offline-tests")); return api; }
    [Fact]
    public async Task ExplicitFolderDownloadSurvivesRestartSearchesAndKeepsDraftsWhenRemoved()
    {
        using var f = new TestLibrary(); using var factory = new Factory(f); using var http = factory.CreateClient(); var api = Api(http, f);
        var status = await api.StatusAsync(default); var state = new ClientStateStore(f.DerivedRoot, Path.Combine(f.DerivedRoot, "cache"), status.LibraryId, 1024);
        var draft = new DraftRecord(f.NoteId, DraftKind.ExistingNote, "unsent", DateTimeOffset.UtcNow); await state.SaveDraftAsync(draft);
        var selection = await state.Offline.DownloadAsync(api, null, "Science"); Assert.Equal("ready", selection.State); Assert.Equal(1, selection.Downloaded);
        var reopened = new OfflineLibrary(f.DerivedRoot, status.LibraryId); Assert.Equal(f.NoteId, Assert.Single(await reopened.SearchAsync("real disk")).Note.Id);
        Assert.Empty(await reopened.SearchAsync("unmatched")); Assert.NotNull((await reopened.StatusAsync()).Manifest.Selections[0].RefreshedAt);
        await reopened.RemoveAsync(selection.Key); Assert.Empty(await reopened.NotesAsync()); Assert.Equal(draft, await state.ReadDraftAsync(f.NoteId));
    }
    [Fact]
    public async Task QuotaRefusalPreservesDownloadsAndExistingJsonState()
    {
        using var f = new TestLibrary(); using var factory = new Factory(f); using var http = factory.CreateClient(); var api = Api(http, f); var status = await api.StatusAsync(default);
        var state = new ClientStateStore(f.DerivedRoot, f.DerivedRoot, status.LibraryId, 1024);
        await state.SaveDraftAsync(new(f.NoteId, DraftKind.ExistingNote, "legacy draft", DateTimeOffset.UtcNow));
        await state.CacheNoteAsync(await api.ReadAsync(f.NoteId, default));
        await state.Offline.DownloadAsync(api, f.NoteId, null);
        await Assert.ThrowsAsync<ArgumentException>(() => state.Offline.SetQuotaAsync(1));
        Assert.NotNull(await state.ReadDraftAsync(f.NoteId)); Assert.NotNull(await state.ReadCachedNoteAsync(f.NoteId));
        Assert.Equal(f.NoteId, Assert.Single(await state.Offline.NotesAsync()).Note.Id);
        var manifest = Path.Combine(f.DerivedRoot, "libraries", status.LibraryId.ToString(), "offline", "manifest.json");
        var data = JsonNode.Parse(File.ReadAllText(manifest))!; data["schema"] = 999; File.WriteAllText(manifest, data.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => state.Offline.StatusAsync()); Assert.NotNull(await state.ReadDraftAsync(f.NoteId));
    }
    [Fact]
    public async Task AssetDependencyAndCreateReplaySurviveRestartWithoutDuplicates()
    {
        using var f = new TestLibrary(); using var factory = new Factory(f); using var http = factory.CreateClient(); var api = Api(http, f); var status = await api.StatusAsync(default);
        var state = new ClientStateStore(f.DerivedRoot, f.DerivedRoot, status.LibraryId, 1024); var pending = await new PendingAssetStore(f.DerivedRoot, status.LibraryId).StageAsync(new MemoryStream("hello attachment"u8.ToArray()), "note.txt", "text/plain");
        var id = Guid.NewGuid(); var markdown = $"---\nid: {id}\n---\n# Offline\n[file](asset-pending://{pending.Id})";
        var request = new ReplayNoteRequest(Guid.NewGuid(), id, "create", markdown, Create: new("Empty", "Offline", Id: id, InitialMarkdown: markdown), LibraryId: status.LibraryId);
        var operation = await state.Offline.Queue.EnqueueAsync(id, request, "Empty/Offline.md", [pending]);
        var restarted = new OfflineLibrary(f.DerivedRoot, status.LibraryId);
        var replay = Assert.Single(await restarted.Queue.ReplayAsync(api)); Assert.Equal("completed", replay.State); Assert.Equal(1, replay.Attempts);
        Assert.NotNull(Assert.Single(replay.Uploads).Uploaded); Assert.DoesNotContain("asset-pending", replay.Result!.Markdown);
        Assert.Equal(replay.Result, await api.ReplayAsync(replay.ReadyRequest!)); Assert.Single((await api.ListAsync("Empty", 0, default)).Entries);
        var copy = await restarted.DownloadAsync(api, id, null); Assert.Equal("ready", copy.State);
        Assert.Equal("hello attachment", System.Text.Encoding.UTF8.GetString((await restarted.ReadAssetAsync(pending.Id))!.Value.Bytes));
    }
    [Fact]
    public void PreparedEditRecoversLostAcknowledgementAndRejectsChangedIdentity()
    {
        using var f = new TestLibrary(); var store = new LibraryStore(new(f.Root)); var original = store.Read(f.NoteId);
        var request = new ReplayNoteRequest(Guid.NewGuid(), f.NoteId, "edit", original.Markdown + "\nlocal", original.Revision);
        var saved = store.Replay(request);
        var receipt = Path.Combine(f.Root, ".slate", "operations", "replay", request.OperationId + ".json");
        var json = JsonNode.Parse(File.ReadAllText(receipt))!; json["result"] = null; File.WriteAllText(receipt, json.ToJsonString());
        var restarted = new LibraryStore(new(f.Root)); Assert.Equal(saved, restarted.Replay(request));
        Assert.Throws<LibraryConflictException>(() => restarted.Replay(request with { Markdown = original.Markdown }));
        Assert.Throws<LibraryConflictException>(() => restarted.Replay(request with { OperationId = Guid.NewGuid(), LibraryId = Guid.NewGuid() }));
    }
    [Fact]
    public async Task ConflictStopsAutomaticRetryAndKeepsNewerDraft()
    {
        using var f = new TestLibrary(); using var factory = new Factory(f); using var http = factory.CreateClient(); var api = Api(http, f); var status = await api.StatusAsync(default);
        var state = new ClientStateStore(f.DerivedRoot, f.DerivedRoot, status.LibraryId, 1024); var original = await api.ReadAsync(f.NoteId, default);
        var operation = await state.Offline.Queue.EnqueueAsync(f.NoteId, new(Guid.NewGuid(), f.NoteId, "edit", original.Markdown + "local", original.Revision), original.Path);
        await api.UpdateNoteAsync(f.NoteId, new(original.Markdown + "server", original.Revision), default);
        await state.SaveDraftAsync(new(f.NoteId, DraftKind.ExistingNote, "even newer local", DateTimeOffset.UtcNow));
        var result = Assert.Single(await state.Offline.Queue.ReplayAsync(api)); Assert.Equal("conflict", result.State);
        Assert.Empty(await state.Offline.Queue.ReplayAsync(api));
        Assert.Equal("even newer local", (await state.ReadDraftAsync(f.NoteId))!.Markdown);
        await Assert.ThrowsAsync<InvalidOperationException>(() => state.Offline.Queue.CancelAsync(operation.Id));
        Assert.Contains("server", (await api.ReadAsync(f.NoteId, default)).Markdown);
    }
    [Fact]
    public async Task UnattemptedCancellationKeepsPayloadAndNewerDraftSurvivesSuccessfulReplay()
    {
        using var f = new TestLibrary(); using var factory = new Factory(f); using var http = factory.CreateClient(); var api = Api(http, f); var status = await api.StatusAsync(default);
        var state = new ClientStateStore(f.DerivedRoot, f.DerivedRoot, status.LibraryId, 1024); var note = await api.ReadAsync(f.NoteId, default);
        var first = await state.Offline.Queue.EnqueueAsync(f.NoteId, new(Guid.NewGuid(), f.NoteId, "edit", note.Markdown + "cancelled", note.Revision), note.Path);
        await state.Offline.Queue.CancelAsync(first.Id); Assert.Equal("cancelled", (await state.Offline.Queue.ReadAsync())[0].State);
        await state.Offline.Queue.EnqueueAsync(f.NoteId, new(Guid.NewGuid(), f.NoteId, "edit", note.Markdown + "sent", note.Revision), note.Path);
        await state.SaveDraftAsync(new(f.NoteId, DraftKind.ExistingNote, "newer unsent", DateTimeOffset.UtcNow)); await state.ReconcileOfflineAsync(api);
        Assert.Equal("newer unsent", (await state.ReadDraftAsync(f.NoteId))!.Markdown); Assert.Contains("sent", (await api.ReadAsync(f.NoteId, default)).Markdown);
    }
    private sealed class LostReplyHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private bool lost;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (!lost && request.RequestUri!.AbsolutePath == "/v1/offline/replay" && response.IsSuccessStatusCode)
            { lost = true; response.Dispose(); throw new HttpRequestException("Connection lost after server saved."); }
            return response;
        }
    }
    [Fact]
    public async Task LostHttpAcknowledgementReplaysSameReceiptAfterClientRestart()
    {
        using var f = new TestLibrary(); using var factory = new Factory(f); using var initial = factory.CreateClient();
        using var http = new HttpClient(new LostReplyHandler(factory.Server.CreateHandler())); var api = Api(http, f);
        var status = await api.StatusAsync(default); var queue = new OfflineLibrary(f.DerivedRoot, status.LibraryId).Queue;
        var id = Guid.NewGuid(); var capture = new CaptureNoteRequest(id, "survive lost reply", CapturedAt: DateTimeOffset.UtcNow);
        var operation = await queue.EnqueueAsync(id, new(Guid.NewGuid(), id, "capture", capture.Content, Capture: capture), "inbox/capture.md");
        Assert.Equal("failed", Assert.Single(await queue.ReplayAsync(api)).State);
        var restarted = new OfflineLibrary(f.DerivedRoot, status.LibraryId).Queue;
        var complete = Assert.Single(await restarted.ReplayAsync(api)); Assert.Equal("completed", complete.State); Assert.Equal(2, complete.Attempts);
        Assert.Single((await api.ListAsync("inbox", 0, default)).Entries);
    }
}

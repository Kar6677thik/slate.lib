using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Slate.Lib.Api;
using Slate.Lib.Core;
using Xunit;

public sealed class Phase3BTests
{
    [Fact]
    public void QuickThoughtCreatesInboxIsSearchableGitPendingAndIdempotent()
    {
        using var fixture = new Phase3BFixture(withGit: true);
        var id = Guid.NewGuid();
        var request = new CaptureNoteRequest(id, "Why does PostgreSQL HOT avoid index updates?", CapturedAt: DateTimeOffset.Parse("2026-09-24T12:00:00Z"));
        var first = fixture.Store.Capture(request);
        var retry = fixture.Store.Capture(request);
        Assert.Equal(first, retry);
        Assert.StartsWith("inbox/2026-09-24-why-does-postgresql-hot-avoid-index", first.Path);
        Assert.True(File.Exists(Path.Combine(fixture.Root, first.Path.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal(id, Assert.Single(fixture.Store.Search("postgresql", 0, 20).Results).Id);
        Assert.True(fixture.Git!.Status.Pending);
        Assert.Single(fixture.Store.List("inbox", 0).Entries);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CaptureRejectsInvalidData(string content)
    {
        using var fixture = new Phase3BFixture();
        Assert.Throws<ArgumentException>(() => fixture.Store.Capture(new(Guid.NewGuid(), content)));
        Assert.Throws<ArgumentException>(() => fixture.Store.Capture(new(Guid.Empty, "valid")));
        Assert.Throws<ArgumentException>(() => fixture.Store.Capture(new(Guid.NewGuid(), "valid", Kind: "image")));
    }

    [Fact]
    public void ReusedCaptureIdCannotChangeContent()
    {
        using var fixture = new Phase3BFixture();
        var id = Guid.NewGuid();
        fixture.Store.Capture(new(id, "one"));
        Assert.Throws<LibraryConflictException>(() => fixture.Store.Capture(new(id, "two")));
        Assert.Single(fixture.Store.List("inbox", 0).Entries);
    }

    [Fact]
    public void CreateWithInitialMarkdownUsesRequestedIdentityAndExistingRevisionRules()
    {
        using var fixture = new Phase3BFixture();
        var id = Guid.NewGuid();
        var note = fixture.Store.CreateNote(new("", "Mobile", "Mobile", id, "# From Android\n\nInitial text"));
        Assert.Equal(id, note.Id);
        Assert.Contains("Initial text", note.Markdown);
        Assert.Throws<LibraryPreconditionException>(() => fixture.Store.Update(id, new(note.Markdown + "x", "\"stale\"")));
    }

    [Fact]
    public async Task DraftPersistsReloadsUpdatesAndDeletesAcrossStoreInstances()
    {
        using var fixture = new Phase3BFixture();
        var draft = new DraftRecord(Guid.NewGuid(), DraftKind.ExistingNote, "first", DateTimeOffset.UtcNow,
            fixture.SeedId, "Seed.md", "etag-a", "server text");
        var first = fixture.ClientState();
        await first.SaveDraftAsync(draft);
        var restarted = fixture.ClientState();
        Assert.Equal("etag-a", (await restarted.ReadDraftAsync(draft.DraftId))!.BaseRevision);
        await restarted.SaveDraftAsync(draft with { Markdown = "second", UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(1) });
        Assert.Equal("second", (await first.ReadDraftAsync(draft.DraftId))!.Markdown);
        await first.DeleteDraftAsync(draft.DraftId);
        Assert.Null(await restarted.ReadDraftAsync(draft.DraftId));
    }

    [Fact]
    public async Task DebouncerPersistsLatestVersionAndFlushesImmediately()
    {
        using var fixture = new Phase3BFixture();
        var store = fixture.ClientState();
        var id = Guid.NewGuid();
        await using var debouncer = new DraftDebouncer(store, TimeSpan.FromMilliseconds(40));
        debouncer.Schedule(new(id, DraftKind.NewNote, "old", DateTimeOffset.UtcNow));
        debouncer.Schedule(new(id, DraftKind.NewNote, "latest", DateTimeOffset.UtcNow));
        await Task.Delay(100);
        Assert.Equal("latest", (await store.ReadDraftAsync(id))!.Markdown);
        debouncer.Schedule(new(id, DraftKind.NewNote, "flushed", DateTimeOffset.UtcNow));
        await debouncer.FlushAsync();
        Assert.Equal("flushed", (await store.ReadDraftAsync(id))!.Markdown);
    }

    [Fact]
    public async Task PendingCaptureFailureRetainsDraftAndSuccessRemovesIt()
    {
        using var fixture = new Phase3BFixture();
        var store = fixture.ClientState();
        var capture = new CaptureNoteRequest(Guid.NewGuid(), "offline thought", CapturedAt: DateTimeOffset.UtcNow);
        await store.SaveDraftAsync(new(capture.CaptureId, DraftKind.QuickThought, capture.Content, DateTimeOffset.UtcNow, PendingCapture: capture));
        var processor = new PendingCaptureProcessor(store);
        Assert.Equal(0, await processor.RetryAsync((_, _) => throw new HttpRequestException("offline")));
        Assert.NotNull(await store.ReadDraftAsync(capture.CaptureId));
        Assert.Equal(1, await processor.RetryAsync((request, _) => Task.FromResult(fixture.Store.Capture(request))));
        Assert.Null(await store.ReadDraftAsync(capture.CaptureId));
        Assert.Single(fixture.Store.List("inbox", 0).Entries);
        Assert.Equal(0, await processor.RetryAsync((request, _) => Task.FromResult(fixture.Store.Capture(request))));
    }

    [Fact]
    public async Task RecentCacheIsBoundedAndSupportsOfflineRead()
    {
        using var fixture = new Phase3BFixture();
        var store = fixture.ClientState(cacheBytes: 1024, recents: 2);
        var one = NewCached("One", 600);
        var two = NewCached("Two", 600);
        var three = NewCached("Three", 600);
        await store.CacheNoteAsync(one, DateTimeOffset.UtcNow.AddMinutes(-2));
        await store.CacheNoteAsync(two, DateTimeOffset.UtcNow.AddMinutes(-1));
        await store.CacheNoteAsync(three, DateTimeOffset.UtcNow);
        var recents = await store.ReadRecentsAsync();
        Assert.Equal([three.Id, two.Id], recents.Select(x => x.Id));
        Assert.NotNull(await store.ReadCachedNoteAsync(three.Id));
        Assert.True(Directory.EnumerateFiles(fixture.CacheRoot, "*.json", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length) <= 1024);
    }

    [Fact]
    public void AndroidShareParserAcceptsTextAndUrlsAndRejectsFiles()
    {
        Assert.False(SharedCaptureInput.Parse("android.intent.action.SEND", "text/plain", "selected text").IsUrl);
        Assert.True(SharedCaptureInput.Parse("android.intent.action.SEND", "text/plain", "https://example.com/article").IsUrl);
        Assert.Throws<ArgumentException>(() => SharedCaptureInput.Parse("android.intent.action.SEND", "image/png", "content://image"));
    }

    private static LibraryNote NewCached(string title, int bodySize)
    {
        var id = Guid.NewGuid();
        return new(id, title + ".md", title, $"---\nid: {id:D}\n---\n\n# {title}\n\n{new string('x', bodySize)}", "etag");
    }

    private sealed class Phase3BFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "slate-3b-lib-" + Guid.NewGuid().ToString("N"));
        public string StateRoot { get; } = Path.Combine(Path.GetTempPath(), "slate-3b-state-" + Guid.NewGuid().ToString("N"));
        public string DerivedRoot { get; } = Path.Combine(Path.GetTempPath(), "slate-3b-derived-" + Guid.NewGuid().ToString("N"));
        public string AppRoot { get; } = Path.Combine(Path.GetTempPath(), "slate-3b-app-" + Guid.NewGuid().ToString("N"));
        public string CacheRoot { get; } = Path.Combine(Path.GetTempPath(), "slate-3b-cache-" + Guid.NewGuid().ToString("N"));
        public Guid LibraryId { get; } = Guid.NewGuid();
        public Guid SeedId { get; } = Guid.NewGuid();
        public SearchIndex Search { get; }
        public GitSyncService? Git { get; }
        public LibraryStore Store { get; }

        public Phase3BFixture(bool withGit = false)
        {
            Directory.CreateDirectory(Path.Combine(Root, ".slate"));
            File.WriteAllText(Path.Combine(Root, ".slate", "library.json"), JsonSerializer.Serialize(new LibraryIdentity(1, LibraryId)));
            File.WriteAllText(Path.Combine(Root, "Seed.md"), $"---\nid: {SeedId:D}\n---\n\n# Seed\n");
            var paths = new LibraryPaths(Root);
            Search = new SearchIndex(Path.Combine(DerivedRoot, "search"));
            if (withGit)
            {
                Git = new GitSyncService(paths, Options.Create(new GitOptions { StatePath = StateRoot }), NullLogger<GitSyncService>.Instance);
                Git.InitializeAsync().GetAwaiter().GetResult();
            }
            Store = new(paths, Search, Git);
        }

        public ClientStateStore ClientState(long cacheBytes = 1024 * 1024, int recents = 100) => new(AppRoot, CacheRoot, LibraryId, cacheBytes, recents);

        public void Dispose()
        {
            Search.Dispose();
            foreach (var path in new[] { Root, StateRoot, DerivedRoot, AppRoot, CacheRoot }) DeleteTree(path);
        }

        private static void DeleteTree(string path)
        {
            if (!Directory.Exists(path)) return;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, true);
        }
    }
}

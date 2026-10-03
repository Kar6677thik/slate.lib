using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Slate.Lib.Api;
using Slate.Lib.Core;
using Xunit;

namespace Slate.Lib.Tests;

public sealed class HistoryRestoreTests
{
    private static GitSyncService Git(TestLibrary f) => new(new(f.Root), Options.Create(new GitOptions { StatePath = Path.Combine(f.DerivedRoot, "state") }), NullLogger<GitSyncService>.Instance);
    [Fact]
    public async Task RestoreCreatesNewCommitAndRejectsStaleCurrentRevision()
    {
        using var f = new TestLibrary(); var git = Git(f); await git.InitializeAsync(); var library = new LibraryStore(new(f.Root), null, git);
        var original = library.Read(f.NoteId); var originalCommit = (await git.HistoryAsync(f.NoteId, original.Path))[0].Commit;
        var changed = library.Update(original.Id, new(original.Markdown + "\nnew text", original.Revision)); await git.FlushAsync(library);
        var beforeRestore = (await git.HistoryAsync(original.Id, original.Path))[0].Commit;
        await Assert.ThrowsAsync<LibraryPreconditionException>(() => git.RestoreAsync(library, original.Id, new(originalCommit, "current", original.Revision)));
        Assert.Equal(changed.Markdown, library.Read(original.Id).Markdown);
        var restored = await git.RestoreAsync(library, original.Id, new(originalCommit, "current", changed.Revision));
        Assert.Equal(original.Markdown, restored.Markdown);
        var history = await git.HistoryAsync(original.Id, original.Path); Assert.Equal(3, history.Count); Assert.NotEqual(beforeRestore, history[0].Commit);
        Assert.Contains(history, x => x.Commit == originalCommit); Assert.Contains(history, x => x.Commit == beforeRestore);
        Assert.Equal(changed.Markdown, (await git.HistoricalNoteAsync(original.Id, beforeRestore)).Markdown);
    }
    [Fact]
    public async Task RestoreAsNewPreservesSourceAndRefusesFilenameCollisions()
    {
        using var f = new TestLibrary(); var git = Git(f); await git.InitializeAsync(); var library = new LibraryStore(new(f.Root), null, git);
        var original = library.Read(f.NoteId); var commit = (await git.HistoryAsync(original.Id, original.Path))[0].Commit;
        var restored = await git.RestoreAsync(library, original.Id, new(commit, "new", Folder: "Empty", Name: "Recovered"));
        Assert.NotEqual(original.Id, restored.Id); Assert.Equal(original.Markdown, library.Read(original.Id).Markdown);
        Assert.Contains("From disk.", restored.Markdown); Assert.Equal(restored.Id, NoteDocument.Parse(restored.Markdown, restored.Path).Id);
        await Assert.ThrowsAsync<LibraryConflictException>(() => git.RestoreAsync(library, original.Id, new(commit, "new", Folder: "Empty", Name: "Recovered")));
        Assert.Equal(restored.Markdown, library.Read(restored.Id).Markdown);
    }
    [Fact]
    public async Task DeletedRecoveryPreviewsOriginalBytesAndPreservesStableIdentity()
    {
        using var f = new TestLibrary(); var git = Git(f); await git.InitializeAsync(); var library = new LibraryStore(new(f.Root), null, git);
        var original = library.Read(f.NoteId); library.Delete(new(original.Path, false)); await git.FlushAsync(library);
        var deleted = Assert.Single((await git.RecoverableAsync(library)).Notes); Assert.Equal(original.Id, deleted.Id);
        var preview = await git.HistoricalNoteAsync(deleted.Id, deleted.SourceCommit); Assert.Equal(original.Markdown, preview.Markdown);
        var recovered = await git.RestoreAsync(library, deleted.Id, new(deleted.SourceCommit, "recover", Folder: "Empty", Name: "Restored"));
        Assert.Equal(original.Id, recovered.Id); Assert.Empty((await git.RecoverableAsync(library)).Notes);
        await Assert.ThrowsAsync<LibraryConflictException>(() => git.RestoreAsync(library, deleted.Id, new(deleted.SourceCommit, "recover", Folder: "Empty", Name: "Another")));
        Assert.Single(library.List("Empty", 0).Entries);
    }
    [Fact]
    public async Task RestorationIntoAnotherFolderRebasesRelativeAttachmentPaths()
    {
        using var f = new TestLibrary(); var asset = Guid.NewGuid();
        f.Write("Science/Databases/Note.md", $"---\nid: {f.NoteId}\n---\n![photo](../../.assets/{asset}.png)\n");
        var git = Git(f); await git.InitializeAsync(); var library = new LibraryStore(new(f.Root), null, git);
        var commit = (await git.HistoryAsync(f.NoteId, "Science/Databases/Note.md"))[0].Commit;
        var restored = await git.RestoreAsync(library, f.NoteId, new(commit, "new", Folder: "Empty", Name: "Photo"));
        Assert.Contains($"../.assets/{asset}.png", restored.Markdown); Assert.DoesNotContain("../../.assets", restored.Markdown);
    }
}

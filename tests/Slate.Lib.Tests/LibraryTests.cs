using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Slate.Lib.Api;
using Slate.Lib.Core;
using Xunit;

namespace Slate.Lib.Tests;

public sealed class TestLibrary : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "slate-tests-" + Guid.NewGuid().ToString("N"));
    public Guid NoteId { get; } = Guid.NewGuid();
    public string DeviceFile => Path.Combine(Root, ".devices.json");
    public string DerivedRoot { get; } = Path.Combine(Path.GetTempPath(), "slate-derived-" + Guid.NewGuid().ToString("N"));
    public TestLibrary()
    {
        Directory.CreateDirectory(Root);
        Write("Science/Databases/Note.md", $"---\nid: {NoteId}\n---\n# A real note\n\nFrom disk.");
        Write("Science/ignore.txt", "not a note");
        Write(".hidden.md", "hidden");
        Directory.CreateDirectory(Path.Combine(Root, "Empty"));
        LibrarySetup.Initialize(new LibraryPaths(Root));
    }
    public void Write(string path, string text)
    {
        var full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text, new UTF8Encoding(false));
    }
    public void Dispose()
    {
        var resolved = Path.GetFullPath(Root);
        var expected = Path.GetFullPath(Path.GetTempPath());
        if (!resolved.StartsWith(expected, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("slate-tests-"))
            throw new InvalidOperationException("Unsafe test cleanup path.");
        DeleteTestDirectory(resolved);
        DeleteTestDirectory(DerivedRoot);
    }

    private static void DeleteTestDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, true);
    }
}

public sealed class LibraryTests
{
    [Fact]
    public void ListsRealNestedFoldersAndOnlyMarkdown()
    {
        using var fixture = new TestLibrary();
        var store = new LibraryStore(new LibraryPaths(fixture.Root));
        Assert.Equal(["Empty", "Science"], store.List("", 0).Entries.Select(x => x.Name));
        Assert.Equal("Science/Databases", Assert.Single(store.List("Science", 0).Entries).Path);
        var note = Assert.Single(store.List("Science/Databases", 0).Entries);
        Assert.False(note.IsDirectory);
        Assert.Equal(fixture.NoteId, note.Id);
        Assert.Equal("A real note", note.Title);
        Assert.Empty(store.List("Empty", 0).Entries);
        Assert.DoesNotContain(fixture.Root, note.Path);
    }

    [Fact]
    public void ReadsDiskChangesAndDetectsDeletion()
    {
        using var fixture = new TestLibrary();
        var store = new LibraryStore(new LibraryPaths(fixture.Root));
        var original = store.Read(fixture.NoteId);
        Assert.Contains("From disk.", original.Markdown);
        fixture.Write(original.Path, $"---\nid: {fixture.NoteId}\n---\n# Changed\nNew content.");
        var updated = store.Read(fixture.NoteId);
        Assert.Equal("Changed", updated.Title);
        Assert.NotEqual(original.Revision, updated.Revision);
        File.Delete(Path.Combine(fixture.Root, original.Path));
        Assert.Throws<FileNotFoundException>(() => store.Read(fixture.NoteId));
        Assert.Throws<FileNotFoundException>(() => store.Read(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData("Science/../../outside.md")]
    [InlineData("/etc/passwd.md")]
    [InlineData("C:/outside.md")]
    [InlineData("Science\\Note.md")]
    [InlineData(".slate/library.json")]
    [InlineData("Science//Note.md")]
    [InlineData("CON.md")]
    [InlineData("note.md:stream")]
    [InlineData("folder./note.md")]
    public void RejectsUnsafePaths(string path)
    {
        using var fixture = new TestLibrary();
        Assert.Throws<ArgumentException>(() => new LibraryPaths(fixture.Root).Resolve(path));
    }

    [Fact]
    public void PaginatesWithoutReturningAllNotes()
    {
        using var fixture = new TestLibrary();
        for (var i = 0; i < 105; i++) fixture.Write($"Many/Note-{i:D3}.md", $"---\nid: {Guid.NewGuid()}\n---\n# Note {i}");
        var store = new LibraryStore(new LibraryPaths(fixture.Root));
        var first = store.List("Many", 0);
        var second = store.List("Many", first.NextPage!.Value);
        Assert.Equal(100, first.Entries.Count);
        Assert.Equal(5, second.Entries.Count);
        Assert.Null(second.NextPage);
        Assert.Equal(105, first.Entries.Concat(second.Entries).Select(x => x.Id).Distinct().Count());
    }

    [Fact]
    public void SetupIsExplicitIdempotentAndPreservesUnknownMetadata()
    {
        using var fixture = new TestLibrary();
        const string source = "---\ntitle: Imported\ncustom: [one, two]\n---\n\n# Original body\n";
        fixture.Write("New.md", source);
        var paths = new LibraryPaths(fixture.Root);
        Assert.Equal(1, LibrarySetup.Initialize(paths));
        var added = File.ReadAllText(Path.Combine(fixture.Root, "New.md"));
        Assert.Contains("custom: [one, two]", added);
        Assert.EndsWith("# Original body\n", added);
        Assert.NotNull(NoteDocument.Parse(added, "New.md").Id);
        Assert.Equal(0, LibrarySetup.Initialize(paths));
        Assert.Equal(added, File.ReadAllText(Path.Combine(fixture.Root, "New.md")));
    }

    [Fact]
    public void RejectsDuplicateIdsInvalidUtf8AndOversizedNotes()
    {
        using var fixture = new TestLibrary();
        fixture.Write("Duplicate.md", $"---\nid: {fixture.NoteId}\n---\n# Duplicate");
        Assert.Throws<InvalidDataException>(() => new LibraryStore(new LibraryPaths(fixture.Root)));
        File.Delete(Path.Combine(fixture.Root, "Duplicate.md"));
        var store = new LibraryStore(new LibraryPaths(fixture.Root));
        File.WriteAllBytes(Path.Combine(fixture.Root, "Bad.md"), [0xff, 0xfe, 0xff]);
        Assert.Throws<InvalidDataException>(() => store.ReadPath("Bad.md"));
        fixture.Write("Large.md", new string('a', NoteDocument.MaxBytes + 1));
        Assert.Throws<InvalidDataException>(() => store.ReadPath("Large.md"));
    }

    [Theory]
    [InlineData("id: nope")]
    [InlineData("id: 00000000-0000-0000-0000-000000000000")]
    [InlineData("id: &anchor value\ntitle: *anchor")]
    [InlineData("id: one\nid: two")]
    [InlineData("title: [not, scalar]")]
    public void RejectsMalformedMetadata(string yaml) =>
        Assert.Throws<InvalidDataException>(() => NoteDocument.Parse($"---\n{yaml}\n---\n# Example", "Example.md"));

    [Fact]
    public void NormalizesWindowsEditorLineEndingsBeforeParsing()
    {
        var id = Guid.NewGuid();
        var editorText = $"---\rid: {id:D}\r---\r\r# Hello Slate\r\rBody";
        var normalized = NoteDocument.NormalizeLineEndings(editorText);

        Assert.DoesNotContain('\r', normalized);
        Assert.Equal(id, NoteDocument.Parse(normalized, "Hello Slate.md").Id);
        Assert.Contains("# Hello Slate\n\nBody", normalized);
    }

    [Fact]
    public void ExtractsAssetReferencesAlongsideWikiLinks()
    {
        var assetId = Guid.NewGuid();
        var markdown = $"[[Second Note]]\n\n![small image](../.assets/{assetId:D}.png)";

        Assert.Equal([assetId], AssetReferences.Extract("Test/Hello Slate.md", markdown));
    }

    [Fact]
    public void RendersMarkdownWithoutActiveContentOrRemoteImages()
    {
        var id = Guid.NewGuid();
        var markdown = $"---\nid: {id}\n---\n# Hello\n\n**bold**\n\n```sql\nSELECT 1;\n```\n\n| A | B |\n| --- | --- |\n| 1 | 2 |\n\n<script>alert(1)</script>\n\n[bad](javascript:alert%281%29)\n\n![remote](https://example.com/track.png)\n\n[local](Other.md)";
        var html = new MarkdownReader().Render(new(id, "Folder/Note.md", "Hello", markdown, "revision"));
        Assert.Contains("<h1 id=\"hello\" data-slate-heading=\"true\">Hello</h1>", html);
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<table>", html);
        Assert.Contains("SELECT 1;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("href=\"javascript:", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("https://slate.invalid/Folder/Other.md", html);
        Assert.Contains("default-src 'none'", html);
    }

    [Fact]
    public void CreatesNotesAndNestedFoldersWithoutOverwriting()
    {
        using var fixture = new TestLibrary();
        var store = new LibraryStore(new LibraryPaths(fixture.Root));
        var folder = store.CreateFolder(new("", "Work"));
        store.CreateFolder(new(folder.Path, "Deep"));
        var note = store.CreateNote(new("Work/Deep", "First note", "First note"));
        Assert.Equal("Work/Deep/First note.md", note.Path);
        Assert.Contains($"id: {note.Id:D}", File.ReadAllText(Path.Combine(fixture.Root, note.Path)));
        Assert.Throws<LibraryConflictException>(() => store.CreateNote(new("Work/Deep", "first NOTE.md")));
        Assert.Throws<LibraryConflictException>(() => store.CreateFolder(new("", "work")));
    }

    [Fact]
    public void UpdatesAtomicallyAndRejectsStaleOrChangedIdentity()
    {
        using var fixture = new TestLibrary();
        var store = new LibraryStore(new LibraryPaths(fixture.Root));
        var current = store.Read(fixture.NoteId);
        var source = current.Markdown.Replace("From disk.", "Saved through the API.");
        var saved = store.Update(current.Id, new(source, current.Revision));
        Assert.Contains("Saved through the API.", store.Read(current.Id).Markdown);
        Assert.NotEqual(current.Revision, saved.Revision);
        Assert.Throws<LibraryPreconditionException>(() => store.Update(current.Id, new(source + "again", current.Revision)));
        Assert.Throws<InvalidDataException>(() => store.Update(current.Id, new(source.Replace(current.Id.ToString(), Guid.NewGuid().ToString()), saved.Revision)));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(Path.Combine(fixture.Root, current.Path))!, ".slate-tmp-*"));
    }

    [Fact]
    public void RenamesFilesAndFoldersWhilePreservingIds()
    {
        using var fixture = new TestLibrary();
        var store = new LibraryStore(new LibraryPaths(fixture.Root));
        var renamed = store.Rename(new("Science/Databases/Note.md", "Renamed"));
        Assert.Equal(fixture.NoteId, renamed.Id);
        Assert.False(File.Exists(Path.Combine(fixture.Root, "Science/Databases/Note.md")));
        Assert.Equal("Science/Databases/Renamed.md", store.Read(fixture.NoteId).Path);
        store.Rename(new("Science/Databases", "Storage"));
        Assert.Equal("Science/Storage/Renamed.md", store.Read(fixture.NoteId).Path);
        store.CreateNote(new("Science/Storage", "Collision"));
        Assert.Throws<LibraryConflictException>(() => store.Rename(new("Science/Storage/Renamed.md", "collision.md")));
    }

    [Fact]
    public void MovesFilesAndFoldersAndRejectsDescendantMoves()
    {
        using var fixture = new TestLibrary();
        var store = new LibraryStore(new LibraryPaths(fixture.Root));
        store.CreateFolder(new("", "Destination"));
        store.Move(new("Science/Databases/Note.md", "Destination"));
        Assert.Equal("Destination/Note.md", store.Read(fixture.NoteId).Path);
        store.CreateFolder(new("Science", "Child"));
        Assert.Throws<ArgumentException>(() => store.Move(new("Science", "Science/Child")));
        store.Move(new("Science/Child", "Destination"));
        Assert.True(Directory.Exists(Path.Combine(fixture.Root, "Destination/Child")));
    }

    [Fact]
    public void CopiesNotesAndFoldersWithFreshIdsAndPredictableNames()
    {
        using var fixture = new TestLibrary();
        var store = new LibraryStore(new LibraryPaths(fixture.Root));
        var copy = store.Copy(new("Science/Databases/Note.md", "Science/Databases"));
        Assert.Equal("Science/Databases/Note copy.md", copy.Path);
        Assert.NotEqual(fixture.NoteId, copy.Id);
        Assert.Contains("From disk.", store.Read(copy.Id!.Value).Markdown);
        var folderCopy = store.Copy(new("Science/Databases", ""));
        var copiedNotes = store.List(folderCopy.Path, 0).Entries;
        Assert.Equal(2, copiedNotes.Count);
        Assert.All(copiedNotes, copiedNote => Assert.NotEqual(fixture.NoteId, copiedNote.Id));
        Assert.Equal(4, store.NoteCount);
    }

    [Fact]
    public void DeletesFilesAndConfirmedNonEmptyFolders()
    {
        using var fixture = new TestLibrary();
        var store = new LibraryStore(new LibraryPaths(fixture.Root));
        Assert.Throws<LibraryConflictException>(() => store.Delete(new("Science", false)));
        var removed = store.Delete(new("Science", true));
        Assert.True(removed.AffectedItems >= 4);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Science")));
        Assert.Throws<FileNotFoundException>(() => store.Delete(new("Science", true)));
        Assert.Throws<FileNotFoundException>(() => store.Read(fixture.NoteId));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("Science/../../outside")]
    [InlineData("C:/outside")]
    [InlineData(".slate")]
    public void MutationsRejectUnsafeSourceAndDestinationPaths(string unsafePath)
    {
        using var fixture = new TestLibrary();
        var store = new LibraryStore(new LibraryPaths(fixture.Root));
        Assert.Throws<ArgumentException>(() => store.CreateFolder(new(unsafePath, "Folder")));
        Assert.Throws<ArgumentException>(() => store.Rename(new("Science/Databases/Note.md", unsafePath)));
        Assert.Throws<ArgumentException>(() => store.Move(new("Science/Databases/Note.md", unsafePath)));
        Assert.Throws<ArgumentException>(() => store.Copy(new("Science/Databases/Note.md", unsafePath)));
        Assert.Throws<ArgumentException>(() => store.Delete(new(unsafePath, true)));
    }

    [Theory]
    [InlineData("Nested/Folder")]
    [InlineData("Nested\\Folder")]
    [InlineData(".")]
    [InlineData("CON")]
    public void CreateAndRenameRequireSinglePortableNames(string unsafeName)
    {
        using var fixture = new TestLibrary();
        var store = new LibraryStore(new LibraryPaths(fixture.Root));
        Assert.Throws<ArgumentException>(() => store.CreateFolder(new("", unsafeName)));
        Assert.Throws<ArgumentException>(() => store.CreateNote(new("", unsafeName)));
        Assert.Throws<ArgumentException>(() => store.Rename(new("Science/Databases/Note.md", unsafeName)));
    }
}

public sealed class SearchTests
{
    [Fact]
    public void SearchesTitleBodyHeadingsPathTagsAndAliasesCaseInsensitively()
    {
        using var fixture = new TestLibrary();
        var rankedId = Guid.NewGuid();
        fixture.Write("Projects/Storage/Postgres.md", $"---\nid: {rankedId}\ntitle: PostgreSQL MVCC\naliases: [Postgres concurrency]\ntags: [database, kubernetes]\n---\n# Tuple Readers\n\nReaders can access older tuple versions.");
        fixture.Write("Projects/Other.md", $"---\nid: {Guid.NewGuid()}\n---\n# Other\n\nA passing PostgreSQL mention.");
        using var index = new SearchIndex(Path.Combine(fixture.DerivedRoot, "search"));
        var store = new LibraryStore(new LibraryPaths(fixture.Root), index, null);
        var ranked = store.Search("POSTGRESQL", 0, 20).Results;
        Assert.Equal(2, ranked.Count);
        Assert.Equal(rankedId, ranked[0].Id);
        Assert.Single(store.Search("older tuple", 0, 20).Results);
        Assert.Single(store.Search("readers", 0, 20).Results);
        Assert.Single(store.Search("title:\"postgresql mvcc\"", 0, 20).Results);
        Assert.Single(store.Search("path:storage", 0, 20).Results);
        Assert.Single(store.Search("tag:kubernetes", 0, 20).Results);
        Assert.Single(store.Search("concurrency", 0, 20).Results);
        Assert.Throws<SearchQueryException>(() => store.Search("unknown:value", 0, 20));
        Assert.Throws<SearchQueryException>(() => store.Search("\"unclosed", 0, 20));
    }

    [Fact]
    public void MutationsIncrementallyUpdateSearchDocuments()
    {
        using var fixture = new TestLibrary();
        using var index = new SearchIndex(Path.Combine(fixture.DerivedRoot, "search"));
        var store = new LibraryStore(new LibraryPaths(fixture.Root), index, null);
        store.CreateFolder(new("", "Journal"));
        store.CreateFolder(new("", "Archive"));
        var note = store.CreateNote(new("Journal", "Today", "Today"));
        var saved = store.Update(note.Id, new(note.Markdown + "incremental-pineapple\n", note.Revision));
        Assert.Single(store.Search("incremental-pineapple", 0, 20).Results);
        saved = store.Update(note.Id, new(saved.Markdown.Replace("incremental-pineapple", "replacement-mango"), saved.Revision));
        Assert.Empty(store.Search("incremental-pineapple", 0, 20).Results);
        Assert.Single(store.Search("replacement-mango", 0, 20).Results);
        var renamed = store.Rename(new(saved.Path, "Renamed"));
        Assert.Equal("Journal/Renamed.md", Assert.Single(store.Search("path:journal", 0, 20).Results).Path);
        var moved = store.Move(new(renamed.Path, "Archive"));
        Assert.Equal("Archive/Renamed.md", Assert.Single(store.Search("path:archive", 0, 20).Results).Path);
        var copied = store.Copy(new(moved.Path, "Archive"));
        Assert.Equal(2, store.Search("replacement-mango", 0, 20).Total);
        store.Delete(new(copied.Path, false));
        Assert.Single(store.Search("replacement-mango", 0, 20).Results);
        store.Delete(new(moved.Path, false));
        Assert.Empty(store.Search("replacement-mango", 0, 20).Results);
    }

    [Fact]
    public void IndexRebuildsAndSurvivesRestartAsDerivedData()
    {
        using var fixture = new TestLibrary();
        var indexPath = Path.Combine(fixture.DerivedRoot, "search");
        using (var first = new SearchIndex(indexPath))
        {
            var store = new LibraryStore(new LibraryPaths(fixture.Root), first, null);
            Assert.Single(store.Search("real note", 0, 20).Results);
            Assert.Equal(1, first.Rebuild(store.AllNotes()));
        }
        using (var reopened = new SearchIndex(indexPath))
        {
            var store = new LibraryStore(new LibraryPaths(fixture.Root), reopened, null);
            Assert.Single(store.Search("real note", 0, 20).Results);
        }
        Directory.Delete(indexPath, true);
        Directory.CreateDirectory(indexPath);
        File.WriteAllText(Path.Combine(indexPath, "segments_1"), "corrupt index");
        using var rebuilt = new SearchIndex(indexPath);
        var recovered = new LibraryStore(new LibraryPaths(fixture.Root), rebuilt, null);
        Assert.Single(recovered.Search("real note", 0, 20).Results);
    }
}

public sealed class GitTests
{
    [Fact]
    public async Task BatchesLocalEditsAndKeepsSearchWorkingWhenRemoteIsUnavailable()
    {
        using var fixture = new TestLibrary();
        var paths = new LibraryPaths(fixture.Root);
        var git = CreateGit(paths, fixture.DerivedRoot);
        await git.InitializeAsync();
        var tracked = RunGit(fixture.Root, "ls-files");
        Assert.DoesNotContain(".hidden.md", tracked);
        Assert.DoesNotContain("Science/ignore.txt", tracked);
        using var index = new SearchIndex(Path.Combine(fixture.DerivedRoot, "search"));
        var store = new LibraryStore(paths, index, git);
        var before = int.Parse(RunGit(fixture.Root, "rev-list", "--count", "HEAD").Trim());
        var note = store.Read(fixture.NoteId);
        var first = store.Update(note.Id, new(note.Markdown + "\nfirst batched edit", note.Revision));
        store.Update(note.Id, new(first.Markdown + "\nsecond batched edit", first.Revision));
        Assert.Single(store.Search("second batched", 0, 20).Results);
        await git.FlushAsync(store);
        Assert.Equal(before + 1, int.Parse(RunGit(fixture.Root, "rev-list", "--count", "HEAD").Trim()));
        RunGit(fixture.Root, "remote", "add", "origin", Path.Combine(fixture.DerivedRoot, "missing-remote.git"));
        var sync = await git.SynchronizeAsync(store);
        Assert.Equal("Error", sync.Status.State);
        Assert.Contains("second batched edit", File.ReadAllText(Path.Combine(fixture.Root, "Science/Databases/Note.md")));
    }

    [Fact]
    public async Task PushesAndImportsRemoteAddModifyDeleteRenameWithHistoryAndSearch()
    {
        using var fixture = new TestLibrary();
        var paths = new LibraryPaths(fixture.Root);
        var bootstrap = new LibraryStore(paths);
        var deleteMe = bootstrap.CreateNote(new("", "Delete Me", "Delete Me"));
        var git = CreateGit(paths, fixture.DerivedRoot);
        await git.InitializeAsync();
        using var index = new SearchIndex(Path.Combine(fixture.DerivedRoot, "search"));
        var links = new LinkIndex();
        var store = new LibraryStore(paths, index, git, links, null);
        var bare = Path.Combine(fixture.DerivedRoot, "remote.git");
        var clone = Path.Combine(fixture.DerivedRoot, "external");
        Directory.CreateDirectory(fixture.DerivedRoot);
        RunGit(fixture.DerivedRoot, "init", "--bare", "-b", "main", bare);
        RunGit(fixture.Root, "remote", "add", "origin", bare);
        Assert.Equal("Synced", (await git.SynchronizeAsync(store)).Status.State);
        RunGit(fixture.DerivedRoot, "clone", "--branch", "main", bare, clone);
        RunGit(clone, "config", "user.name", "External Editor");
        RunGit(clone, "config", "user.email", "external@example.invalid");
        var oldPath = Path.Combine(clone, "Science", "Databases", "Note.md");
        var renamedPath = Path.Combine(clone, "Science", "Databases", "Renamed.md");
        File.WriteAllText(oldPath, File.ReadAllText(oldPath).Replace("From disk.", "From disk with remote-orchid."), new UTF8Encoding(false));
        RunGit(clone, "mv", "Science/Databases/Note.md", "Science/Databases/Renamed.md");
        var remoteAddedId = Guid.NewGuid();
        File.WriteAllText(Path.Combine(clone, "Remote Added.md"), $"---\nid: {remoteAddedId}\n---\n# Remote Added\n\nremote-cobalt\n\n[[id:{fixture.NoteId}]]", new UTF8Encoding(false));
        File.Delete(Path.Combine(clone, deleteMe.Path));
        RunGit(clone, "add", "-A");
        RunGit(clone, "commit", "-m", "External knowledge changes");
        RunGit(clone, "push", "origin", "main");
        var imported = await git.SynchronizeAsync(store);
        Assert.True(imported.Imported, imported.Status.Detail);
        Assert.Equal("Science/Databases/Renamed.md", store.Read(fixture.NoteId).Path);
        Assert.False(File.Exists(Path.Combine(fixture.Root, deleteMe.Path)));
        Assert.Single(store.Search("remote-orchid", 0, 20).Results);
        Assert.Single(store.Search("remote-cobalt", 0, 20).Results);
        Assert.Empty(store.Search("Delete Me", 0, 20).Results);
        Assert.Equal(remoteAddedId, Assert.Single(store.Links(fixture.NoteId).Backlinks).SourceId);
        var history = await git.HistoryAsync(fixture.NoteId, "Science/Databases/Renamed.md");
        Assert.True(history.Count >= 2);
        var oldest = history[^1];
        var historical = await git.HistoricalNoteAsync(fixture.NoteId, oldest.Commit);
        Assert.Contains("From disk.", historical.Markdown);
        Assert.DoesNotContain("remote-orchid", historical.Markdown);
        Assert.True(File.Exists(renamedPath));
    }

    [Fact]
    public async Task DivergenceStopsWithoutDestroyingEitherHistory()
    {
        using var fixture = new TestLibrary();
        var paths = new LibraryPaths(fixture.Root);
        var git = CreateGit(paths, fixture.DerivedRoot);
        await git.InitializeAsync();
        using var index = new SearchIndex(Path.Combine(fixture.DerivedRoot, "search"));
        var store = new LibraryStore(paths, index, git);
        var bare = Path.Combine(fixture.DerivedRoot, "remote.git");
        var clone = Path.Combine(fixture.DerivedRoot, "external");
        Directory.CreateDirectory(fixture.DerivedRoot);
        RunGit(fixture.DerivedRoot, "init", "--bare", "-b", "main", bare);
        RunGit(fixture.Root, "remote", "add", "origin", bare);
        await git.SynchronizeAsync(store);
        RunGit(fixture.DerivedRoot, "clone", "--branch", "main", bare, clone);
        RunGit(clone, "config", "user.name", "External Editor");
        RunGit(clone, "config", "user.email", "external@example.invalid");
        File.AppendAllText(Path.Combine(clone, "Science/Databases/Note.md"), "\nremote branch text\n");
        RunGit(clone, "add", "-A"); RunGit(clone, "commit", "-m", "Remote branch"); RunGit(clone, "push", "origin", "main");
        var local = store.Read(fixture.NoteId);
        store.Update(local.Id, new(local.Markdown + "\nlocal branch text\n", local.Revision));
        await git.FlushAsync(store);
        var localHead = RunGit(fixture.Root, "rev-parse", "HEAD").Trim();
        var remoteHead = RunGit(clone, "rev-parse", "HEAD").Trim();
        var result = await git.SynchronizeAsync(store);
        Assert.True(result.Status.State == "Conflict", result.Status.Detail);
        await Assert.ThrowsAsync<LibraryConflictException>(() => git.PreviewSafeMergeAsync(store));
        Assert.Equal(localHead, RunGit(fixture.Root, "rev-parse", "HEAD").Trim());
        Assert.Equal(remoteHead, RunGit(clone, "rev-parse", "HEAD").Trim());
        Assert.Contains("local branch text", File.ReadAllText(Path.Combine(fixture.Root, "Science/Databases/Note.md")));
    }

    [Fact]
    public async Task DeleteCheckpointsAJustCreatedNoteBeforeRemovingIt()
    {
        using var fixture = new TestLibrary();
        var paths = new LibraryPaths(fixture.Root);
        var git = CreateGit(paths, fixture.DerivedRoot);
        await git.InitializeAsync();
        using var index = new SearchIndex(Path.Combine(fixture.DerivedRoot, "search"));
        var store = new LibraryStore(paths, index, git);
        var note = store.CreateNote(new("", "Ephemeral", "Ephemeral"));
        store.Delete(new(note.Path, false));
        await git.FlushAsync(store);
        Assert.False(File.Exists(Path.Combine(fixture.Root, note.Path)));
        Assert.Empty(store.Search("Ephemeral", 0, 20).Results);
        Assert.True(int.Parse(RunGit(fixture.Root, "rev-list", "--count", "--all", "--", note.Path).Trim()) >= 1);
        var source = RunGit(fixture.Root, "show", $"HEAD^:{note.Path}");
        Assert.Contains($"id: {note.Id:D}", source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoversKnownInterruptedImportStatesOnStartup(bool activationCompleted)
    {
        using var fixture = new TestLibrary();
        var paths = new LibraryPaths(fixture.Root);
        var git = CreateGit(paths, fixture.DerivedRoot);
        await git.InitializeAsync();
        var head = RunGit(fixture.Root, "rev-parse", "HEAD").Trim();
        var other = new string('a', 40);
        var pending = Path.Combine(fixture.DerivedRoot, "state", "pending", "git-import.json");
        Directory.CreateDirectory(Path.GetDirectoryName(pending)!);
        File.WriteAllText(pending, System.Text.Json.JsonSerializer.Serialize(new
        {
            OldHead = activationCompleted ? other : head,
            NewHead = activationCompleted ? head : other
        }));
        _ = CreateGit(paths, fixture.DerivedRoot);
        Assert.False(File.Exists(pending));
        Assert.Equal(head, RunGit(fixture.Root, "rev-parse", "HEAD").Trim());
    }

    private static GitSyncService CreateGit(LibraryPaths paths, string dataRoot) => new(paths,
        Options.Create(new GitOptions { StatePath = Path.Combine(dataRoot, "state"), CommitDebounceSeconds = 1, CommitMaximumSeconds = 2, SyncIntervalSeconds = 3600 }),
        NullLogger<GitSyncService>.Instance);

    private static string RunGit(string workingDirectory, params string[] arguments)
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

public sealed class ApiTests
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
    public async Task AuthenticatedApiReadsRealFilesAndHonorsEtags()
    {
        using var fixture = new TestLibrary();
        var token = new DeviceTokens(fixture.DeviceFile).Create("test");
        using var factory = new Factory(fixture);
        using var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/v1/library")).StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var page = await http.GetFromJsonAsync<FolderPage>("/v1/library?path=Science%2FDatabases");
        Assert.Equal(fixture.NoteId, Assert.Single(page!.Entries).Id);
        var response = await http.GetAsync($"/v1/notes/{fixture.NoteId}");
        response.EnsureSuccessStatusCode();
        var note = await response.Content.ReadFromJsonAsync<LibraryNote>();
        Assert.Contains("From disk.", note!.Markdown);
        Assert.Equal(note.Revision, response.Headers.ETag!.ToString());
        using var conditional = new HttpRequestMessage(HttpMethod.Get, $"/v1/notes/{fixture.NoteId}");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", note.Revision);
        Assert.Equal(HttpStatusCode.NotModified, (await http.SendAsync(conditional)).StatusCode);
        File.Delete(Path.Combine(fixture.Root, note.Path));
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/v1/notes/{fixture.NoteId}")).StatusCode);
        File.WriteAllText(fixture.DeviceFile, "{}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/v1/status")).StatusCode);
    }

    [Theory]
    [InlineData("..%2Foutside.md")]
    [InlineData("%2Fetc%2Fpasswd.md")]
    [InlineData("C%3A%5Coutside.md")]
    [InlineData(".slate%2Flibrary.json")]
    public async Task ApiRejectsEncodedTraversalWithoutLeakingPhysicalPath(string path)
    {
        using var fixture = new TestLibrary();
        var token = new DeviceTokens(fixture.DeviceFile).Create("test");
        using var factory = new Factory(fixture);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await http.GetAsync("/v1/notes/by-path?path=" + path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(fixture.Root, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ApiReportsMalformedNoteAndClientCanReconnect()
    {
        using var fixture = new TestLibrary();
        var token = new DeviceTokens(fixture.DeviceFile).Create("test");
        using var factory = new Factory(fixture);
        using var http = factory.CreateClient();
        var api = new LibraryApiClient(http);
        api.Connect(http.BaseAddress!.ToString(), token);
        Assert.Equal(1, (await api.StatusAsync(default)).NoteCount);
        fixture.Write("Science/Databases/Note.md", "---\nid: bad\n---\n# Broken");
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => api.ReadAsync(fixture.NoteId, default));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        fixture.Write("Science/Databases/Note.md", $"---\nid: {fixture.NoteId}\n---\n# Repaired");
        api.Connect(http.BaseAddress.ToString(), token);
        Assert.Equal("Repaired", (await api.ReadAsync(fixture.NoteId, default)).Title);
    }

    [Fact]
    public async Task MutationApiCreatesSavesMovesCopiesAndDeletesRealFiles()
    {
        using var fixture = new TestLibrary();
        var token = new DeviceTokens(fixture.DeviceFile).Create("test");
        using var factory = new Factory(fixture);
        using var http = factory.CreateClient();
        var api = new LibraryApiClient(http);
        api.Connect(http.BaseAddress!.ToString(), token);
        var folder = await api.CreateFolderAsync(new("", "Journal"), default);
        var note = await api.CreateNoteAsync(new(folder.Path, "Today", "Today"), default);
        var saved = await api.UpdateNoteAsync(note.Id, new(note.Markdown + "Written and saved.\n", note.Revision), default);
        Assert.Contains("Written and saved.", File.ReadAllText(Path.Combine(fixture.Root, saved.Path)));
        var moved = await api.RenameAsync(new(saved.Path, "Renamed"), default);
        var copied = await api.CopyAsync(new(moved.Path, ""), default);
        Assert.NotEqual(note.Id, copied.Id);
        await api.DeleteAsync(new(moved.Path, false), default);
        Assert.False(File.Exists(Path.Combine(fixture.Root, moved.Path)));
        Assert.True(File.Exists(Path.Combine(fixture.Root, copied.Path)));
    }

    [Fact]
    public async Task UpdateApiRequiresAndChecksIfMatch()
    {
        using var fixture = new TestLibrary();
        var token = new DeviceTokens(fixture.DeviceFile).Create("test");
        using var factory = new Factory(fixture);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var note = await http.GetFromJsonAsync<LibraryNote>($"/v1/notes/{fixture.NoteId}");
        using var missing = await http.PutAsJsonAsync($"/v1/notes/{fixture.NoteId}", new UpdateNoteRequest(note!.Markdown, note.Revision));
        Assert.Equal((HttpStatusCode)428, missing.StatusCode);
        using var staleRequest = new HttpRequestMessage(HttpMethod.Put, $"/v1/notes/{fixture.NoteId}") { Content = JsonContent.Create(new UpdateNoteRequest(note.Markdown, note.Revision)) };
        staleRequest.Headers.TryAddWithoutValidation("If-Match", "\"stale\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await http.SendAsync(staleRequest)).StatusCode);
    }

    [Fact]
    public async Task SearchApiReturnsCompactCurrentResultsAndValidatesQueries()
    {
        using var fixture = new TestLibrary();
        var token = new DeviceTokens(fixture.DeviceFile).Create("test");
        using var factory = new Factory(fixture);
        using var http = factory.CreateClient();
        var api = new LibraryApiClient(http);
        api.Connect(http.BaseAddress!.ToString(), token);
        var created = await api.CreateNoteAsync(new("", "Search API", "Search API"), default);
        await api.UpdateNoteAsync(created.Id, new(created.Markdown + "compact-api-saffron\n", created.Revision), default);
        var results = await api.SearchAsync("compact-api-saffron", 0, default);
        var hit = Assert.Single(results.Results);
        Assert.Equal(created.Id, hit.Id);
        Assert.Equal("Search API.md", hit.Path);
        Assert.Contains("compact-api-saffron", hit.Snippet);
        Assert.DoesNotContain("---", hit.Snippet);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync("/v1/search?q=unknown%3Avalue")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/v1/search?q=note")).StatusCode);
    }

    [Fact]
    public async Task CaptureApiCreatesSearchableInboxNoteAndRetriesWithoutDuplication()
    {
        using var fixture = new TestLibrary();
        var token = new DeviceTokens(fixture.DeviceFile).Create("test");
        using var factory = new Factory(fixture);
        using var http = factory.CreateClient();
        var api = new LibraryApiClient(http);
        api.Connect(http.BaseAddress!.ToString(), token);
        var request = new CaptureNoteRequest(Guid.NewGuid(), "api-capture-saffron", "remember this", "share", DateTimeOffset.Parse("2026-09-24T12:00:00Z"));
        var created = await api.CaptureAsync(request, default);
        var retried = await api.CaptureAsync(request, default);
        Assert.Equal(created, retried);
        Assert.StartsWith("inbox/", created.Path);
        Assert.Equal(created.Id, Assert.Single((await api.SearchAsync("api-capture-saffron", 0, default)).Results).Id);
        Assert.Single((await api.ListAsync("inbox", 0, default)).Entries);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync("/v1/captures", request)).StatusCode);
    }

    [Fact]
    public async Task HistoryApiRejectsInjectedCommitIdentifiers()
    {
        using var fixture = new TestLibrary();
        var token = new DeviceTokens(fixture.DeviceFile).Create("test");
        using var factory = new Factory(fixture);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await http.GetAsync($"/v1/notes/{fixture.NoteId}/history/not-a-commit%3A--help");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(fixture.Root, await response.Content.ReadAsStringAsync());
    }
}

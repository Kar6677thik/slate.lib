using System.Text.Json.Nodes;
using Slate.Lib.Api;
using Slate.Lib.Core;
using Xunit;

namespace Slate.Lib.Tests;

public sealed class LinkManagementTests
{
    [Fact]
    public void RenameSupportsCaseOnlyPathsAndRepairsIncomingLinks()
    {
        using var f = new TestLibrary(); var store = new LibraryStore(new(f.Root), null, null, new LinkIndex(), null);
        var source = store.CreateNote(new("Empty", "Source", InitialMarkdown: "[real](../Science/Databases/Note.md)"));
        var preview = store.PreviewBulk(new(Guid.NewGuid(), "rename", ["Science/Databases/Note.md"], NewName: "NOTE.md"));
        store.ApplyBulk(new(preview.OperationId, preview.Fingerprint, true));
        Assert.Equal("Science/Databases/NOTE.md", store.Read(f.NoteId).Path); Assert.Contains("Databases/NOTE.md", store.Read(source.Id).Markdown);
        Assert.Contains("Note", NoteDocument.Parse(store.Read(f.NoteId).Markdown, "NOTE.md").Aliases);
    }
    [Fact]
    public void MarkdownEdgesResolveRelativePathsAndDoNotIndexCodeOrExternalUrls()
    {
        using var f = new TestLibrary(); var index = new LinkIndex(); var store = new LibraryStore(new(f.Root), null, null, index, null);
        var note = store.CreateNote(new("Empty", "Source", InitialMarkdown: "[real](../Science/Databases/Note.md)\n[missing](lost.md)\n[web](https://example.com/a.md)\n`[code](no.md)`\n```md\n[fenced](no.md)\n```"));
        var outgoing = store.Links(note.Id).Outgoing; Assert.Equal(2, outgoing.Count);
        Assert.Equal(f.NoteId, outgoing[0].TargetId); Assert.Equal("markdown", outgoing[0].Kind);
        Assert.Equal(note.Id, Assert.Single(store.Links(f.NoteId).Backlinks).SourceId);
        Assert.Equal("lost.md", Assert.Single(store.LinkIssues(0).Results).Link.Target);
    }
    [Fact]
    public void MoveReviewsIncomingReplacementsAndAtomicallyRetainsOriginals()
    {
        using var f = new TestLibrary(); var store = new LibraryStore(new(f.Root), null, null, new LinkIndex(), null);
        var source = store.CreateNote(new("Empty", "Source", InitialMarkdown: "[real](../Science/Databases/Note.md)\n[[Science/Databases/Note.md]]\n[[id:" + f.NoteId + "]]"));
        var preview = store.PreviewBulk(new(Guid.NewGuid(), "move", ["Science/Databases"], "Empty"));
        var repair = Assert.Single(preview.Repairs!); Assert.Equal(2, repair.Changes.Count); Assert.Equal(source.Markdown, store.Read(source.Id).Markdown);
        Assert.Contains("(Databases/Note.md)", repair.ProposedMarkdown);
        var result = store.ApplyBulk(new(preview.OperationId, preview.Fingerprint, true));
        Assert.Equal("completed", result.State); Assert.Equal("Empty/Databases/Note.md", store.Read(f.NoteId).Path);
        Assert.Equal(repair.ProposedMarkdown, store.Read(source.Id).Markdown); Assert.All(store.Links(source.Id).Outgoing, x => Assert.Equal("resolved", x.State));
        Assert.Equal(source.Markdown, File.ReadAllText(Path.Combine(f.Root, ".slate", "operations", preview.OperationId.ToString(), "link-held", "0")));
    }
    [Fact]
    public void StaleIncomingSourceAbortsWholeMoveAndRejectingRepairsPreservesSource()
    {
        using var f = new TestLibrary(); var store = new LibraryStore(new(f.Root), null, null, new LinkIndex(), null);
        var source = store.CreateNote(new("Empty", "Source", InitialMarkdown: "[real](../Science/Databases/Note.md)"));
        var preview = store.PreviewBulk(new(Guid.NewGuid(), "move", ["Science/Databases"], "Empty"));
        var changed = store.Update(source.Id, new(source.Markdown + "\nOther device", source.Revision));
        Assert.Throws<LibraryPreconditionException>(() => store.ApplyBulk(new(preview.OperationId, preview.Fingerprint, true)));
        Assert.Equal("Science/Databases/Note.md", store.Read(f.NoteId).Path);
        store.ApplyBulk(new(preview.OperationId, preview.Fingerprint, false));
        Assert.Equal(changed.Markdown, store.Read(source.Id).Markdown); Assert.Equal("broken", Assert.Single(store.Links(source.Id).Outgoing).State);
    }
    [Fact]
    public void InterruptedMoveAndIncomingRepairRecoverTogether()
    {
        using var f = new TestLibrary(); var store = new LibraryStore(new(f.Root), null, null, new LinkIndex(), null);
        var source = store.CreateNote(new("Empty", "Source", InitialMarkdown: "[real](../Science/Databases/Note.md)"));
        var preview = store.PreviewBulk(new(Guid.NewGuid(), "move", ["Science/Databases/Note.md"], "Empty"));
        var directory = Path.Combine(f.Root, ".slate", "operations", preview.OperationId.ToString()); var journal = Path.Combine(directory, "plan.json");
        var json = JsonNode.Parse(File.ReadAllText(journal))!; json["state"] = "applying"; json["repairIncoming"] = true; File.WriteAllText(journal, json.ToJsonString());
        Directory.CreateDirectory(Path.Combine(directory, "held")); Directory.CreateDirectory(Path.Combine(directory, "link-held"));
        File.Move(Path.Combine(f.Root, "Science/Databases/Note.md"), Path.Combine(directory, "held/0"));
        File.Move(Path.Combine(directory, "payload/0"), Path.Combine(f.Root, "Empty/Note.md"));
        File.Move(Path.Combine(f.Root, source.Path), Path.Combine(directory, "link-held/0"));
        File.Move(Path.Combine(directory, "link-payload/0"), Path.Combine(f.Root, source.Path));
        var recovered = new LibraryStore(new(f.Root)); Assert.Equal(source.Markdown, recovered.Read(source.Id).Markdown); Assert.Equal("Science/Databases/Note.md", recovered.Read(f.NoteId).Path);
        Assert.Equal("rolled-back", recovered.BulkStatus(preview.OperationId).State);
    }
    [Fact]
    public void WikiConversionPreservesFragmentsCodeAndUnresolvedLinksAndRejectsChangedTargets()
    {
        using var f = new TestLibrary(); var store = new LibraryStore(new(f.Root), null, null, new LinkIndex(), null);
        var source = store.CreateNote(new("Empty", "Source", InitialMarkdown: $"[[id:{f.NoteId}#A real note|Read this]]\n[[Unknown]]\n`[[Unknown]]`"));
        var preview = store.PreviewWikiExport(source.Id); Assert.Single(preview.Changes);
        Assert.Contains("[Read this](../Science/Databases/Note.md#a-real-note)", preview.ProposedMarkdown); Assert.Contains("[[Unknown]]\n`[[Unknown]]`", preview.ProposedMarkdown);
        store.Move(new("Science/Databases/Note.md", "Empty"));
        Assert.Throws<LibraryPreconditionException>(() => store.ApplyWikiExport(source.Id, new(preview.Revision, preview.ProposedMarkdown)));
        preview = store.PreviewWikiExport(source.Id); var converted = store.ApplyWikiExport(source.Id, new(preview.Revision, preview.ProposedMarkdown));
        Assert.Contains("(Note.md#a-real-note)", converted.Markdown);
    }
    [Fact]
    public void AmbiguousWikiLinkRequiresExplicitDestinationAndRevalidatesBothNotes()
    {
        using var f = new TestLibrary(); var store = new LibraryStore(new(f.Root), null, null, new LinkIndex(), null);
        var one = store.CreateNote(new("Empty", "One", "Same")); store.CreateNote(new("Empty", "Two", "Same"));
        var source = store.CreateNote(new("Empty", "Source", InitialMarkdown: "[[Same]]"));
        var issue = Assert.Single(store.LinkIssues(0).Results); Assert.Equal("ambiguous", issue.Link.State);
        Assert.Empty(store.PreviewWikiExport(source.Id).Changes);
        var request = new LinkRepairRequest(source.Revision, issue.Link.Start, one.Id, one.Revision);
        var preview = store.PreviewLinkRepair(source.Id, request); Assert.Contains("[[id:" + one.Id, preview.ProposedMarkdown);
        var updated = store.Update(one.Id, new(one.Markdown + "\nChanged", one.Revision));
        Assert.Throws<LibraryPreconditionException>(() => store.ApplyLinkRepair(source.Id, request));
        store.ApplyLinkRepair(source.Id, request with { TargetRevision = updated.Revision });
        Assert.Empty(store.LinkIssues(0).Results);
    }
    [Fact]
    public void MarkdownBacklinksDriveOrphanAndSearchViews()
    {
        using var f = new TestLibrary(); using var search = new SearchIndex(Path.Combine(f.DerivedRoot, "search")); var store = new LibraryStore(new(f.Root), search, null, new LinkIndex(), null);
        store.CreateNote(new("Empty", "Source", InitialMarkdown: "[read](../Science/Databases/Note.md)"));
        Assert.DoesNotContain(store.SmartView("orphans", 0, 20).Results, x => x.Id == f.NoteId);
        Assert.Equal(f.NoteId, Assert.Single(store.Search("has:backlinks", 0, 20).Results).Id);
    }
}

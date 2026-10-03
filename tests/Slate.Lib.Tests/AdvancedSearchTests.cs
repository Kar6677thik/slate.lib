using Slate.Lib.Api;
using Slate.Lib.Core;
using Xunit;

namespace Slate.Lib.Tests;

public sealed class AdvancedSearchTests
{
    [Fact]
    public void FiltersCombineWithTextAndUpdateIncrementally()
    {
        using var fixture = new TestLibrary(); using var index = new SearchIndex(Path.Combine(fixture.DerivedRoot, "search"));
        var store = new LibraryStore(new(fixture.Root), index, null, new LinkIndex(), null); var id = Guid.NewGuid();
        var note = store.CreateNote(new("Empty", "Postgres", Id: id, InitialMarkdown: $"---\nid: {id}\ntype: question\nstatus: open\ncreated: 2024-03-04\nupdated: 2026-09-30T23:30:00-02:00\n---\n# Postgres\n\n```sql\nselect 1;\n```\n![Image](https://example.com/image.png)\n[file](../.assets/{Guid.NewGuid()}.pdf)\n"));
        Assert.Equal(id, Assert.Single(store.Search("type:QUESTION status:open postgres has:code has:question has:image has:file has:links", 0, 20).Results).Id);
        Assert.Single(store.Search("created:2024-03-04 date:2024-01-01..2024-12-31 modified:2026-10-01", 0, 20).Results);
        Assert.Empty(store.Search("modified:2026-09-30", 0, 20).Results); // Timestamp is normalized to UTC.
        store.AnswerQuestion(id, new("Done", note.Revision, DateTimeOffset.UtcNow));
        Assert.Empty(store.Search("type:question status:open postgres", 0, 20).Results);
        Assert.Single(store.Search("type:question status:answered postgres", 0, 20).Results);
    }

    [Fact]
    public void OpenDateRangesExcludeUnknownDatesAndIncludeBothBoundaries()
    {
        using var fixture = new TestLibrary();
        foreach (var day in new[] { "2024-01-01", "2024-12-31", "2025-01-01" }) fixture.Write($"Empty/{day}.md", $"---\nid: {Guid.NewGuid()}\ncreated: {day}\n---\nDate");
        using var index = new SearchIndex(Path.Combine(fixture.DerivedRoot, "search")); var store = new LibraryStore(new(fixture.Root), index, null);
        Assert.Equal(2, store.Search("created:2024-01-01..2024-12-31", 0, 20).Total);
        Assert.Equal(2, store.Search("created:*..2024-12-31", 0, 20).Total);
        Assert.Equal(1, store.Search("created:2025-01-01..*", 0, 20).Total);
        Assert.Equal(3, store.Search("created:*", 0, 20).Total);
    }

    [Theory]
    [InlineData("date:yesterday")]
    [InlineData("created:2024-02-30")]
    [InlineData("modified:2026-10-02..2020-01-01")]
    [InlineData("has:magic")]
    [InlineData("type:")]
    public void InvalidFiltersReturnUsefulQueryErrors(string query)
    {
        using var fixture = new TestLibrary(); using var index = new SearchIndex(Path.Combine(fixture.DerivedRoot, "search")); var store = new LibraryStore(new(fixture.Root), index, null);
        Assert.Throws<SearchQueryException>(() => store.Search(query, 0, 20));
    }

    [Fact]
    public void ExactTitleOutranksAliasHeadingAndRepeatedBodyAndPrefixCanFindTitle()
    {
        using var fixture = new TestLibrary(); var exact = Guid.NewGuid(); var alias = Guid.NewGuid();
        fixture.Write("Empty/Exact.md", $"---\nid: {exact}\ntitle: Postgres\n---\nA reference.");
        fixture.Write("Empty/Alias.md", $"---\nid: {alias}\naliases: [Postgres]\n---\n# Database notes\nOther content");
        fixture.Write("Empty/Body.md", $"---\nid: {Guid.NewGuid()}\n---\n# Other\n{string.Join(' ', Enumerable.Repeat("Postgres", 300))}");
        using var index = new SearchIndex(Path.Combine(fixture.DerivedRoot, "search")); var store = new LibraryStore(new(fixture.Root), index, null);
        Assert.Equal(exact, store.Search("postgres", 0, 20).Results[0].Id);
        Assert.Equal(exact, Assert.Single(store.Search("postg", 0, 20).Results).Id);
        Assert.Equal(exact, store.Search("\"postgres\"", 0, 20).Results[0].Id);
    }

    [Fact]
    public void BacklinkPredicateTracksCurrentLinkState()
    {
        using var fixture = new TestLibrary(); using var index = new SearchIndex(Path.Combine(fixture.DerivedRoot, "search")); var store = new LibraryStore(new(fixture.Root), index, null, new LinkIndex(), null);
        var source = store.CreateNote(new("Empty", "Incoming", InitialMarkdown: $"[[id:{fixture.NoteId}]]"));
        Assert.Equal(fixture.NoteId, Assert.Single(store.Search("has:backlinks", 0, 20).Results).Id);
        store.Update(source.Id, new(NoteDocument.AddId("Link removed", source.Id), source.Revision));
        Assert.Empty(store.Search("has:backlinks", 0, 20).Results);
    }
}

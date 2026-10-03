using Slate.Lib.Api;
using Slate.Lib.Core;
using Xunit;

namespace Slate.Lib.Tests;

public sealed class RediscoveryTests
{
    [Fact]
    public void EligibilityDatesAndExplanationsStayDeterministicAndRefreshAfterEdits()
    {
        using var fixture = new TestLibrary(); using var search = new SearchIndex(Path.Combine(fixture.DerivedRoot, "rediscovery"));
        var links = new LinkIndex(); var store = new LibraryStore(new(fixture.Root), search, null, links, null);
        LibraryNote Add(string name, string metadata) { var id = Guid.NewGuid(); return store.CreateNote(new("Empty", name, Id: id, InitialMarkdown: $"---\nid: {id}\n{metadata}\n---\n# {name}")); }
        var old = Add("Question", "type: question\nstatus: open\ncreated: 2020-10-02");
        var unknown = Add("Unknown", "type: idea\ncreated: someday");
        var learned = Add("Learned", "status: learned\nupdated: 2026-09-20");
        Add("Future", "status: learned\nupdated: 2028-01-01");
        var today = new DateOnly(2026, 10, 2);
        var hit = Assert.Single(links.Rediscover("old-question", today, 0).Results); Assert.Equal(old.Id, hit.Id); Assert.Contains("Open question", hit.Reason);
        Assert.Equal(old.Id, Assert.Single(links.Rediscover("on-this-day", today, 0).Results).Id);
        Assert.Equal(learned.Id, Assert.Single(links.Rediscover("recently-learned", today, 0).Results).Id);
        Assert.DoesNotContain(links.Rediscover("timeline", today, 0).Results, x => x.Id == unknown.Id);
        var parsed = links.ParsedNotes;
        Assert.Equal(links.Rediscover("timeline", today, 0).Results, links.Rediscover("timeline", today, 0).Results);
        Assert.Equal(parsed, links.ParsedNotes);
        store.Update(old.Id, new(old.Markdown.Replace("status: open", "status: answered"), old.Revision));
        Assert.Empty(links.Rediscover("old-question", today, 0).Results);
        Assert.Throws<ArgumentException>(() => links.Rediscover("timeline", today, -1));
    }

    [Fact]
    public async Task ActivityIsOptInLocalBoundedAndClearableAcrossRestart()
    {
        using var fixture = new TestLibrary(); var library = Guid.NewGuid(); var id = Guid.NewGuid();
        var activity = new ReadingActivityStore(fixture.DerivedRoot, library);
        await activity.OpenedAsync(id); Assert.Empty((await activity.ReadAsync()).Notes);
        await activity.ConfigureAsync(true); await activity.OpenedAsync(id);
        var reopened = new ReadingActivityStore(fixture.DerivedRoot, library);
        Assert.Equal(id, Assert.Single((await reopened.ReadAsync()).Notes).NoteId);
        Assert.Empty((await reopened.ReadAsync(DateTimeOffset.UtcNow.AddDays(181))).Notes);
        await reopened.ConfigureAsync(false); await reopened.OpenedAsync(Guid.NewGuid());
        Assert.Single((await reopened.ReadAsync()).Notes);
        await reopened.ConfigureAsync(false, true); Assert.Empty((await activity.ReadAsync()).Notes);
        Assert.False((await activity.ReadAsync()).Enabled);
        Assert.Empty((await new ReadingActivityStore(fixture.DerivedRoot, Guid.NewGuid()).ReadAsync()).Notes);
    }
}

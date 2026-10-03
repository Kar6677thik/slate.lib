using Slate.Lib.Api;
using Slate.Lib.Core;
using Xunit;

namespace Slate.Lib.Tests;

public sealed class KnowledgeGraphTests
{
    [Fact]
    public void LargeIndexedLibraryKeepsGraphAndRelatedResponsesBounded()
    {
        var links = new LinkIndex(); var ids = Enumerable.Range(0, 10000).Select(_ => Guid.NewGuid()).ToArray();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        links.Reconcile(ids.Select((id, i) => new LibraryNote(id, $"Project/Note-{i:D5}.md", $"Note {i}", $"---\nid: {id}\ntags: [project]\n---\n# Note {i}\n" + (i == 0 ? string.Join('\n', ids.Skip(1).Select(target => $"[[id:{target}]]")) : ""), i.ToString())));
        var indexed = timer.ElapsedMilliseconds; timer.Restart();
        var graph = links.Graph(ids[0], 3, 40); var related = links.Related(ids[0]);
        Assert.Equal(40, graph.Nodes.Count); Assert.True(graph.Limited); Assert.InRange(graph.Edges.Count, 1, 240); Assert.Equal(8, related.Count);
        Assert.Equal(10000, links.ParsedNotes);
        Console.WriteLine($"10,000-note graph benchmark: index {indexed} ms; graph + related {timer.ElapsedMilliseconds} ms; graph nodes {graph.Nodes.Count}, related {related.Count}.");
    }
    [Fact]
    public void GraphUsesResolvedWikiAndMarkdownEdgesAndBoundsTraversal()
    {
        using var fixture = new TestLibrary(); using var search = new SearchIndex(Path.Combine(fixture.DerivedRoot, "graph"));
        var links = new LinkIndex(); var store = new LibraryStore(new(fixture.Root), search, null, links, null);
        LibraryNote Add(string name, string body) { var id = Guid.NewGuid(); return store.CreateNote(new("Empty", name, Id: id, InitialMarkdown: $"---\nid: {id}\ntype: question\n---\n# {name}\n{body}")); }
        var c = Add("C", ""); var b = Add("B", "[C](C.md)"); var a = Add("A", $"[[id:{b.Id}]] [[Missing]]");
        var graph = links.Graph(a.Id); Assert.Equal(2, graph.Nodes.Count); Assert.Equal(new GraphEdge(a.Id, b.Id), Assert.Single(graph.Edges));
        graph = links.Graph(a.Id, 2); Assert.Equal(3, graph.Nodes.Count); Assert.Contains(new GraphEdge(b.Id, c.Id), graph.Edges);
        Assert.Equal(2, links.Graph(c.Id, 1).Nodes.Count); // Incoming edges are traversable.
        Assert.True(links.Graph(a.Id, 2, 2).Limited); Assert.Equal(2, links.Graph(a.Id, 2, 2).Nodes.Count);
        Assert.Single(links.Graph(a.Id, type: "idea").Nodes); Assert.Single(links.Graph(a.Id, folder: "Elsewhere").Nodes);
        Assert.Equal(3, links.Graph(a.Id, 2, folder: "Empty", type: "question").Nodes.Count);
        var parses = links.ParsedNotes; links.Graph(a.Id); links.Related(a.Id); Assert.Equal(parses, links.ParsedNotes);
        store.Update(a.Id, new(a.Markdown.Replace($"[[id:{b.Id}]]", ""), a.Revision));
        Assert.Single(links.Graph(a.Id).Nodes); Assert.Throws<ArgumentException>(() => links.Graph(a.Id, 4));
    }

    [Fact]
    public void RelatedScoresAreExplainableStableAndLimited()
    {
        using var fixture = new TestLibrary(); using var search = new SearchIndex(Path.Combine(fixture.DerivedRoot, "related"));
        var links = new LinkIndex(); var store = new LibraryStore(new(fixture.Root), search, null, links, null);
        LibraryNote Add(string name, string body = "") { var id = Guid.NewGuid(); return store.CreateNote(new("Empty", name, Id: id, InitialMarkdown: $"---\nid: {id}\ntags: [database, study]\n---\n# {name}\n{body}")); }
        var target = Add("Database internals"); var direct = Add("Database indexes", $"[[id:{target.Id}]]");
        Add("Shared source", $"[[id:{target.Id}]] [[id:{direct.Id}]]");
        for (var i = 0; i < 12; i++) Add("Other " + i);
        var results = links.Related(target.Id); Assert.Equal(8, results.Count); Assert.DoesNotContain(results, x => x.Id == target.Id);
        var hit = results.First(x => x.Id == direct.Id); Assert.Equal(24, hit.Score);
        Assert.Contains("Direct note link (+12)", hit.Reasons); Assert.Contains("2 shared tags (+6)", hit.Reasons); Assert.Contains("1 shared linking notes (+4)", hit.Reasons);
        Assert.Equal(results.Select(x => x.Id), links.Related(target.Id).Select(x => x.Id));
    }
}

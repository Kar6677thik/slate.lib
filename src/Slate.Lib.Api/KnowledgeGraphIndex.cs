using System.Text.RegularExpressions;
using Slate.Lib.Core;

namespace Slate.Lib.Api;

public sealed partial class LinkIndex
{
    private long graphVersion = -1;
    private Dictionary<Guid, HashSet<Guid>> forward = [], backward = [];
    private Dictionary<Guid, HashSet<string>> terms = [];
    private void EnsureGraph()
    {
        if (graphVersion == Version) return;
        forward = notes.Keys.ToDictionary(x => x, _ => new HashSet<Guid>());
        backward = notes.Keys.ToDictionary(x => x, _ => new HashSet<Guid>());
        foreach (var (source, links) in resolved)
            foreach (var target in links.Outgoing.Where(x => x.TargetId is not null && x.State == "resolved").Select(x => x.TargetId!.Value).Distinct())
                if (source != target) { forward[source].Add(target); backward[target].Add(source); }
        terms = Summaries().ToDictionary(x => x.Id, x => Regex.Matches(x.Title + " " + string.Join(' ', x.Headings), @"[\p{L}\p{N}]{3,}").Select(m => m.Value.ToLowerInvariant()).Take(512).ToHashSet());
        graphVersion = Version;
    }
    public KnowledgeGraph Graph(Guid id, int depth = 1, int limit = 40, string? folder = null, string? type = null)
    {
        if (depth is < 1 or > 3 || limit is < 2 or > 60 || folder?.Length > 512 || type?.Length > 80) throw new ArgumentException("Graph supports depth 1–3 and 2–60 nodes.");
        lock (gate)
        {
            if (!notes.ContainsKey(id)) throw new FileNotFoundException(); EnsureGraph();
            var distances = new Dictionary<Guid, int> { [id] = 0 }; var queue = new Queue<Guid>(); queue.Enqueue(id); var limited = false;
            bool Eligible(Guid key) => (string.IsNullOrWhiteSpace(folder) || notes[key].Note.Path.StartsWith(folder.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrWhiteSpace(type) || notes[key].Document.Type?.Equals(type, StringComparison.OrdinalIgnoreCase) == true);
            while (queue.TryDequeue(out var current))
            {
                if (distances[current] == depth) continue;
                foreach (var next in forward[current].Concat(backward[current]).Distinct().Where(Eligible).OrderBy(x => notes[x].Note.Path, StringComparer.OrdinalIgnoreCase))
                {
                    if (distances.ContainsKey(next)) continue;
                    if (distances.Count == limit) { limited = true; break; }
                    distances[next] = distances[current] + 1; queue.Enqueue(next);
                }
            }
            var edges = distances.Keys.SelectMany(source => forward[source].Where(distances.ContainsKey).OrderBy(x => notes[x].Note.Path, StringComparer.OrdinalIgnoreCase).Select(target => new GraphEdge(source, target))).Take(241).ToArray();
            return new(id, distances.Select(x => { var note = notes[x.Key]; return new GraphNode(x.Key, note.Note.Title, note.Note.Path, note.Document.Type, note.Document.Status, x.Value); }).ToArray(), edges.Take(240).ToArray(), limited || edges.Length > 240);
        }
    }
    public IReadOnlyList<RelatedNote> Related(Guid id)
    {
        lock (gate)
        {
            if (!notes.ContainsKey(id)) throw new FileNotFoundException(); EnsureGraph();
            var source = notes[id]; var tags = source.Document.Tags.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var folder = Path.GetDirectoryName(source.Note.Path);
            return Summaries().Where(x => x.Id != id && !x.Path.StartsWith("templates/", StringComparison.OrdinalIgnoreCase)).Select(x =>
            {
                var reasons = new List<string>(); var score = 0;
                if (forward[id].Contains(x.Id) || backward[id].Contains(x.Id)) { score += 12; reasons.Add("Direct note link (+12)"); }
                var common = backward[id].Intersect(backward[x.Id]).Count(); if (common > 0) { score += Math.Min(common, 5) * 4; reasons.Add($"{common} shared linking notes (+{Math.Min(common, 5) * 4})"); }
                var shared = x.Tags.Distinct(StringComparer.OrdinalIgnoreCase).Count(tags.Contains); if (shared > 0) { score += Math.Min(shared, 5) * 3; reasons.Add($"{shared} shared tags (+{Math.Min(shared, 5) * 3})"); }
                var words = terms[id].Intersect(terms[x.Id]).Count(); if (words > 0) { score += Math.Min(words, 4); reasons.Add($"{words} shared title/heading terms (+{Math.Min(words, 4)})"); }
                if (!string.IsNullOrEmpty(folder) && string.Equals(folder, Path.GetDirectoryName(x.Path), StringComparison.OrdinalIgnoreCase)) { score += 1; reasons.Add("Same folder (+1)"); }
                return new RelatedNote(x.Id, x.Title, x.Path, score, reasons);
            }).Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        }
    }
}

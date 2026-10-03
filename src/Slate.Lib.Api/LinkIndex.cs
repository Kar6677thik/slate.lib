using Slate.Lib.Core;

namespace Slate.Lib.Api;

public sealed partial class LinkIndex
{
    private sealed record Indexed(LibraryNote Note, NoteDocument Document, IReadOnlyList<WikiLinkToken> Tokens, IReadOnlyList<MarkdownLinkToken> Markdown);
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, Indexed> notes = [];
    private Dictionary<Guid, NoteLinks> resolved = [];
    private Dictionary<string, List<Indexed>> byPath = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<Indexed>> byName = new(StringComparer.OrdinalIgnoreCase);
    private LinkIssue[] issues = [];

    public long Version { get; private set; }
    public long ParsedNotes { get; private set; }

    public void Reconcile(IEnumerable<LibraryNote> current)
    {
        lock (gate)
        {
            var seen = new HashSet<Guid>();
            var changed = false;
            foreach (var note in current)
            {
                seen.Add(note.Id);
                if (notes.TryGetValue(note.Id, out var previous) && previous.Note.Revision == note.Revision) continue;
                var document = NoteDocument.Parse(note.Markdown, note.Path);
                notes[note.Id] = new(note, document, WikiLinks.Extract(document.Body), PortableNoteLinks.Extract(document.Body));
                ParsedNotes++; changed = true;
            }
            foreach (var id in notes.Keys.Where(id => !seen.Contains(id)).ToArray()) { notes.Remove(id); changed = true; }
            if (changed) ResolveAll();
        }
    }

    public void Upsert(LibraryNote note)
    {
        lock (gate)
        {
            var document = NoteDocument.Parse(note.Markdown, note.Path);
            notes[note.Id] = new(note, document, WikiLinks.Extract(document.Body), PortableNoteLinks.Extract(document.Body));
            ParsedNotes++; ResolveAll();
        }
    }

    public NoteLinks Read(Guid id)
    {
        lock (gate) return resolved.TryGetValue(id, out var links) ? links : throw new FileNotFoundException();
    }

    public IReadOnlyList<Guid> WithBacklinks()
    {
        lock (gate) return resolved.Where(x => x.Value.Backlinks.Count > 0).Select(x => x.Key).ToArray();
    }
    public LinkIssuePage Issues(int page)
    {
        if (page is < 0 or > 1999) throw new ArgumentException("Invalid page.");
        lock (gate) return new(page, issues.Length, issues.Skip(page * 20).Take(20).ToArray());
    }

    private void ResolveAll()
    {
        byPath = notes.Values.GroupBy(n => n.Note.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        byName = notes.Values.SelectMany(n => n.Document.Aliases.Append(n.Note.Title).Append(Path.GetFileNameWithoutExtension(n.Note.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Select(key => (key, n)))
            .GroupBy(x => x.key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(x => x.n).ToList(), StringComparer.OrdinalIgnoreCase);
        var headingIds = notes.ToDictionary(n => n.Key, n => { var slugger = new HeadingSlugger(); return n.Value.Document.Headings.Select(slugger.Add).ToHashSet(StringComparer.Ordinal); });
        var outgoing = new Dictionary<Guid, List<NoteLink>>();
        var incoming = notes.Keys.ToDictionary(id => id, _ => new List<Backlink>());
        foreach (var source in notes.Values)
        {
            var links = new List<NoteLink>();
            foreach (var token in source.Tokens)
            {
                var candidates = ResolveCandidates(token.Target);
                var state = candidates.Count switch { 0 => "broken", 1 => "resolved", _ => "ambiguous" };
                var target = candidates.Count == 1 ? candidates[0] : null;
                if (target is not null && token.Heading is { Length: > 0 })
                {
                    if (!headingIds[target.Note.Id].Contains(HeadingSlugger.Slug(token.Heading))) state = "missing-heading";
                }
                links.Add(new(token.Start, token.Length, token.Raw, token.Target, token.Label, token.Heading, state,
                    target?.Note.Id, target?.Note.Path, target?.Note.Title, candidates.Count > 1 ? candidates.Select(x => x.Note.Id).ToArray() : null));
                if (target is not null && target.Note.Id != source.Note.Id)
                    incoming[target.Note.Id].Add(new(source.Note.Id, source.Note.Title, source.Note.Path, token.Heading));
            }
            foreach (var token in source.Markdown.DistinctBy(x => (x.Start, x.Length)))
            {
                if (token.Image) continue;
                var targetPath = token.Url.StartsWith('#') ? source.Note.Path : PortableNoteLinks.ResolvePath(source.Note.Path, token.Url);
                if (targetPath is null || !targetPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
                var candidates = byPath.GetValueOrDefault(targetPath) ?? [];
                var target = candidates.Count == 1 ? candidates[0] : null;
                var heading = token.Url.Contains('#') ? Uri.UnescapeDataString(token.Url[(token.Url.IndexOf('#') + 1)..]) : null;
                var state = candidates.Count switch { 0 => "broken", 1 => "resolved", _ => "ambiguous" };
                if (target is not null && !string.IsNullOrEmpty(heading) && !headingIds[target.Note.Id].Contains(heading)) state = "missing-heading";
                links.Add(new(token.Start, token.Length, source.Document.Body.Substring(token.Start, token.Length), token.Url, null, heading, state,
                    target?.Note.Id, target?.Note.Path, target?.Note.Title, candidates.Count > 1 ? candidates.Select(x => x.Note.Id).ToArray() : null, "markdown"));
                if (target is not null && target.Note.Id != source.Note.Id) incoming[target.Note.Id].Add(new(source.Note.Id, source.Note.Title, source.Note.Path, heading));
            }
            outgoing[source.Note.Id] = links;
        }
        resolved = notes.Keys.ToDictionary(id => id, id => new NoteLinks(id, outgoing.GetValueOrDefault(id) ?? [], incoming[id]
            .DistinctBy(x => x.SourceId).OrderBy(x => x.SourceTitle, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray()));
        issues = resolved.SelectMany(x => x.Value.Outgoing.Where(l => l.State != "resolved").Select(l => new LinkIssue(x.Key, notes[x.Key].Note.Title, notes[x.Key].Note.Path, notes[x.Key].Note.Revision, l)))
            .OrderBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Link.Start).ToArray();
        Version++;
    }

    private List<Indexed> ResolveCandidates(string target)
    {
        if (target.StartsWith("id:", StringComparison.OrdinalIgnoreCase))
            return Guid.TryParse(target[3..], out var id) && notes.TryGetValue(id, out var byId) ? [byId] : [];
        if (target.Contains('/') || target.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return byPath.GetValueOrDefault(target) ?? [];
        return byName.GetValueOrDefault(target) ?? [];
    }
}

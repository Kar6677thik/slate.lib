using Slate.Lib.Core;

namespace Slate.Lib.Api;

public sealed class LinkIndex
{
    private sealed record Indexed(LibraryNote Note, NoteDocument Document, IReadOnlyList<WikiLinkToken> Tokens);
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, Indexed> notes = [];
    private Dictionary<Guid, NoteLinks> resolved = [];

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
                notes[note.Id] = new(note, document, WikiLinks.Extract(document.Body));
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
            notes[note.Id] = new(note, document, WikiLinks.Extract(document.Body));
            ParsedNotes++; ResolveAll();
        }
    }

    public NoteLinks Read(Guid id)
    {
        lock (gate) return resolved.TryGetValue(id, out var links) ? links : throw new FileNotFoundException();
    }

    private void ResolveAll()
    {
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
                    var slugger = new HeadingSlugger();
                    var headings = target.Document.Headings.Select(slugger.Add).ToHashSet(StringComparer.Ordinal);
                    if (!headings.Contains(HeadingSlugger.Slug(token.Heading))) state = "missing-heading";
                }
                links.Add(new(token.Start, token.Length, token.Raw, token.Target, token.Label, token.Heading, state,
                    target?.Note.Id, target?.Note.Path, target?.Note.Title, candidates.Count > 1 ? candidates.Select(x => x.Note.Id).ToArray() : null));
                if (target is not null && target.Note.Id != source.Note.Id)
                    incoming[target.Note.Id].Add(new(source.Note.Id, source.Note.Title, source.Note.Path, token.Heading));
            }
            outgoing[source.Note.Id] = links;
        }
        resolved = notes.Keys.ToDictionary(id => id, id => new NoteLinks(id, outgoing.GetValueOrDefault(id) ?? [], incoming[id]
            .DistinctBy(x => x.SourceId).OrderBy(x => x.SourceTitle, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray()));
        Version++;
    }

    private List<Indexed> ResolveCandidates(string target)
    {
        if (target.StartsWith("id:", StringComparison.OrdinalIgnoreCase))
            return Guid.TryParse(target[3..], out var id) && notes.TryGetValue(id, out var byId) ? [byId] : [];
        if (target.Contains('/') || target.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return notes.Values.Where(x => x.Note.Path.Equals(target, StringComparison.OrdinalIgnoreCase)).ToList();
        return notes.Values.Where(x => x.Note.Title.Equals(target, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(x.Note.Path).Equals(target, StringComparison.OrdinalIgnoreCase) ||
            x.Document.Aliases.Contains(target, StringComparer.OrdinalIgnoreCase)).DistinctBy(x => x.Note.Id).ToList();
    }
}

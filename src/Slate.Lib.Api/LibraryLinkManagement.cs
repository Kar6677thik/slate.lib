using Slate.Lib.Core;

namespace Slate.Lib.Api;

public sealed partial class LibraryStore
{
    public LinkIssuePage LinkIssues(int page) => links?.Issues(page) ?? throw new InvalidOperationException("Links are not configured.");

    private static NoteTextPreview TextPreview(LibraryNote note, IReadOnlyList<LinkReplacement> replacements)
    {
        var source = note.Markdown;
        foreach (var change in replacements.OrderByDescending(x => x.Start))
        {
            if (source.Substring(change.Start, change.Length) != change.Original) throw new LibraryPreconditionException("A link changed while its preview was prepared.");
            source = source.Remove(change.Start, change.Length).Insert(change.Start, change.Proposed);
        }
        NoteDocument.Parse(source, note.Path);
        return new(note.Id, note.Path, note.Revision, note.Markdown, source, replacements);
    }
    private IReadOnlyList<NoteTextPreview> IncomingRepairs(IReadOnlyDictionary<string, string> moved, LinkIndex resolver)
    {
        var candidates = notes.Where(x => moved.ContainsKey(x.Value)).SelectMany(x => resolver.Read(x.Key).Backlinks).Select(x => x.SourceId).Distinct().ToArray();
        var previews = new List<NoteTextPreview>(); long bytes = 0;
        foreach (var id in candidates)
        {
            var note = Read(id); if (moved.ContainsKey(note.Path)) continue;
            var parsed = NoteDocument.Parse(note.Markdown, note.Path); var changes = new List<LinkReplacement>();
            foreach (var link in resolver.Read(id).Outgoing)
            {
                if (link.TargetId is not { } targetId || link.TargetPath is not { } oldPath || !moved.TryGetValue(oldPath, out var newPath)) continue;
                string replacement;
                if (link.Kind == "markdown") replacement = PortableNoteLinks.RelativeUrl(note.Path, newPath, link.Target.Contains('#') ? link.Target[(link.Target.IndexOf('#') + 1)..] : null);
                else if (link.Target.Equals(oldPath, StringComparison.OrdinalIgnoreCase)) replacement = "[[id:" + targetId + (link.Heading is null ? "" : "#" + link.Heading) + "|" + (link.Label ?? link.Target).Replace("]", "", StringComparison.Ordinal) + "]]";
                else continue; // UUID/title links survive the move without a repair.
                changes.Add(new(parsed.HeaderEnd + link.Start, link.Length, link.Raw, replacement, targetId, newPath));
            }
            if (changes.Count == 0) continue;
            bytes += System.Text.Encoding.UTF8.GetByteCount(note.Markdown) * 2L;
            if (previews.Count >= 100 || bytes > 16 * 1024 * 1024 || changes.Count > 5000) throw new ArgumentException("This move affects too many incoming links. Split the selection into smaller moves.");
            previews.Add(TextPreview(note, changes));
        }
        return previews;
    }

    public NoteTextPreview PreviewWikiExport(Guid id)
    {
        lock (gate)
        {
            var note = Read(id); var parsed = NoteDocument.Parse(note.Markdown, note.Path);
            var resolver = new LinkIndex(); resolver.Reconcile(AllNotes());
            var changes = resolver.Read(id).Outgoing.Where(x => x.Kind == "wiki" && x.State == "resolved" && x.TargetId is not null && x.TargetPath is not null).Select(link =>
            {
                var label = (link.Label ?? link.TargetTitle ?? link.Target).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);
                var url = PortableNoteLinks.RelativeUrl(note.Path, link.TargetPath!, link.Heading is null ? null : Uri.EscapeDataString(HeadingSlugger.Slug(link.Heading)));
                return new LinkReplacement(parsed.HeaderEnd + link.Start, link.Length, link.Raw, $"[{label}]({url})", link.TargetId!.Value, link.TargetPath!);
            }).ToArray();
            return TextPreview(note, changes);
        }
    }

    public NoteTextPreview PreviewLinkRepair(Guid id, LinkRepairRequest request)
    {
        lock (gate)
        {
            var note = Read(id); var target = Read(request.TargetId);
            if (note.Revision != request.SourceRevision || target.Revision != request.TargetRevision) throw new LibraryPreconditionException("A note changed. Review the link again.");
            var resolver = new LinkIndex(); resolver.Reconcile(AllNotes());
            var link = resolver.Read(id).Outgoing.FirstOrDefault(l => l.Start == request.Start && l.State != "resolved") ?? throw new LibraryPreconditionException("The link is no longer broken.");
            // A missing heading is explicitly repaired to the chosen note, without guessing another heading.
            var replacement = link.Kind == "markdown" ? PortableNoteLinks.RelativeUrl(note.Path, target.Path) : "[[id:" + target.Id + "|" + (link.Label ?? target.Title).Replace("]", "", StringComparison.Ordinal) + "]]";
            var parsed = NoteDocument.Parse(note.Markdown, note.Path);
            return TextPreview(note, [new(parsed.HeaderEnd + link.Start, link.Length, link.Raw, replacement, target.Id, target.Path)]);
        }
    }
    public LibraryNote ApplyWikiExport(Guid id, WikiExportRequest request)
    {
        lock (gate)
        {
            var preview = PreviewWikiExport(id);
            if (preview.Revision != request.SourceRevision || preview.ProposedMarkdown != request.ProposedMarkdown) throw new LibraryPreconditionException("A source or link target changed. Preview the conversion again.");
            return Update(id, new(preview.ProposedMarkdown, preview.Revision));
        }
    }
    public LibraryNote ApplyLinkRepair(Guid id, LinkRepairRequest request)
    {
        lock (gate) { var preview = PreviewLinkRepair(id, request); return Update(id, new(preview.ProposedMarkdown, preview.Revision)); }
    }
}

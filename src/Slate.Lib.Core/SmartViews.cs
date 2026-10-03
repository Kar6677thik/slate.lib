using Markdig;
using Markdig.Syntax;

namespace Slate.Lib.Core;

public sealed record SmartView(string Id, string Title, string Description);
public static class SmartViews
{
    public static IReadOnlyList<SmartView> All { get; } = [
        new("unanswered", "Unanswered Questions", "Questions marked open, or without a status."),
        new("modified", "Recently Modified", "Newest updated/modified metadata first; falls back to created. Undated notes follow."),
        new("orphans", "Orphan Notes", "No incoming links from other notes. Templates are excluded; self-links do not count."),
        new("diagrams", "Notes With Diagrams", "Contains a fenced Mermaid diagram."),
        new("code", "Notes With Code", "Contains a fenced code block, including Mermaid."),
        new("learning", "Currently Learning", "Status learning, currently-learning, or in-progress."),
        new("review", "Needs Review", "Status needs-review or review.")];
    public static SmartView Get(string id) => All.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException("Unknown smart view.");
}

public static class NotePredicates
{
    public static IReadOnlySet<string> Read(NoteDocument note)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var blocks = Markdown.Parse(note.Body).Descendants<FencedCodeBlock>().ToArray();
        if (blocks.Length > 0) result.Add("code");
        if (blocks.Any(b => b.Info?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Equals("mermaid", StringComparison.OrdinalIgnoreCase) == true)) result.Add("diagram");
        foreach (var link in PortableNoteLinks.Extract(note.Body))
        {
            result.Add("links");
            if (link.Image) result.Add("image");
            else if (link.Url.Contains(".assets/", StringComparison.OrdinalIgnoreCase)) result.Add("file");
        }
        if (WikiLinks.Extract(note.Body).Count > 0) result.Add("links");
        if (note.Type?.Equals("question", StringComparison.OrdinalIgnoreCase) == true) result.Add("question");
        return result;
    }
}

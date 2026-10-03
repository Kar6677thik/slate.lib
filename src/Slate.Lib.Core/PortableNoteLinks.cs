using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Slate.Lib.Core;

public sealed record MarkdownLinkToken(int Start, int Length, string Url, bool Image);

public static class PortableNoteLinks
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build();
    public static IReadOnlyList<MarkdownLinkToken> Extract(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        return document.Descendants<LinkInline>().Where(x => x.Url is not null && x.UrlSpan.Start >= 0 && x.UrlSpan.Length > 0)
            .Select(x => new MarkdownLinkToken(x.UrlSpan.Start, x.UrlSpan.Length, x.Url!, x.IsImage)).ToArray();
    }
    public static string? ResolvePath(string sourcePath, string url)
    {
        var value = url.Split('#', 2)[0];
        if (value.Length == 0 || value.Contains(':') || value.StartsWith('/') || value.Contains('\\')) return null;
        try { value = Uri.UnescapeDataString(value); } catch (UriFormatException) { return null; }
        var parts = sourcePath.Split('/').SkipLast(1).ToList();
        foreach (var part in value.Split('/'))
        {
            if (part is "." or "") continue;
            if (part == "..") { if (parts.Count == 0) return null; parts.RemoveAt(parts.Count - 1); }
            else parts.Add(part);
        }
        return string.Join('/', parts);
    }
    public static string RelativeUrl(string from, string to, string? heading = null)
    {
        var directory = from.Split('/').SkipLast(1).ToArray(); var target = to.Split('/'); var common = 0;
        while (common < directory.Length && common < target.Length && directory[common].Equals(target[common], StringComparison.OrdinalIgnoreCase)) common++;
        return string.Join('/', Enumerable.Repeat("..", directory.Length - common).Concat(target.Skip(common).Select(Uri.EscapeDataString)))
            + (string.IsNullOrEmpty(heading) ? "" : "#" + heading);
    }
    public static string Rewrite(string markdown, string oldPath, string newPath, IReadOnlyDictionary<string, string> movedPaths,
        IReadOnlyDictionary<Guid, Guid>? copiedIds = null, IReadOnlyList<NoteLink>? wiki = null)
    {
        var parsed = NoteDocument.Parse(markdown, oldPath);
        var replacements = new List<(int Start, int Length, string Value)>();
        foreach (var token in Extract(parsed.Body))
        {
            if (token.Image && !AssetReferences.TryParse(oldPath, token.Url, out _)) continue;
            var resolved = ResolvePath(oldPath, token.Url);
            if (resolved is null) continue;
            var target = movedPaths.TryGetValue(resolved, out var replacement) ? replacement : resolved;
            if (!target.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !AssetReferences.TryParse(oldPath, token.Url, out _)) continue;
            var fragment = token.Url.Contains('#') ? token.Url[(token.Url.IndexOf('#') + 1)..] : null;
            replacements.Add((parsed.HeaderEnd + token.Start, token.Length, RelativeUrl(newPath, target, fragment)));
        }
        foreach (var link in wiki ?? [])
        {
            if (link.Kind != "wiki") continue;
            if (link.State != "resolved" || link.TargetId is not { } id) continue;
            if (copiedIds?.TryGetValue(id, out var copyId) == true) id = copyId;
            else if (copiedIds is null && link.TargetPath is { } path && !movedPaths.ContainsKey(path)) continue;
            else if (copiedIds is not null) continue;
            var value = "[[id:" + id.ToString("D") + (link.Heading is null ? "" : "#" + link.Heading)
                + "|" + (link.Label ?? link.TargetTitle ?? link.Target) + "]]";
            replacements.Add((parsed.HeaderEnd + link.Start, link.Length, value));
        }
        foreach (var change in replacements.OrderByDescending(x => x.Start))
            markdown = markdown.Remove(change.Start, change.Length).Insert(change.Start, change.Value);
        return markdown;
    }
}

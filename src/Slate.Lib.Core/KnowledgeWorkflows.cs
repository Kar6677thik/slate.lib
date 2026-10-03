using System.Globalization;
using System.Text.Json;
using YamlDotNet.RepresentationModel;

namespace Slate.Lib.Core;

public static class FrontMatter
{
    // Replace only named scalar values; leave unknown mappings/lists, comments and the body intact.
    public static string SetScalars(string source, IReadOnlyDictionary<string, string> values)
    {
        foreach (var key in values.Keys)
            if (key is not ("type" or "status" or "title" or "created" or "updated" or "answered")) throw new ArgumentException("Unsupported metadata edit.");
        var parsed = NoteDocument.Parse(source, "note.md", allowMissingId: true);
        var text = source.TrimStart('\uFEFF'); var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (parsed.HeaderEnd == 0)
            return "---" + newline + string.Join(newline, values.Select(p => p.Key + ": " + JsonSerializer.Serialize(p.Value))) + newline + "---" + newline + newline + text;
        var start = text.IndexOf('\n') + 1; var close = text.LastIndexOf("---", parsed.HeaderEnd - 1, parsed.HeaderEnd, StringComparison.Ordinal);
        var stream = new YamlStream(); stream.Load(new StringReader(text[start..close]));
        var mapping = (YamlMappingNode)stream.Documents[0].RootNode;
        var replacements = new List<(int Start, int Length, string Text)>(); var missing = new List<string>();
        foreach (var (key, value) in values)
        {
            if (mapping.Children.TryGetValue(new YamlScalarNode(key), out var node))
            {
                if (node is not YamlScalarNode) throw new InvalidDataException("The metadata field is not a scalar; review its source first.");
                replacements.Add((checked(start + (int)node.Start.Index), checked((int)(node.End.Index - node.Start.Index)), JsonSerializer.Serialize(value)));
            }
            else missing.Add(key + ": " + JsonSerializer.Serialize(value));
        }
        if (missing.Count > 0) replacements.Add((close, 0, string.Join(newline, missing) + newline));
        foreach (var change in replacements.OrderByDescending(x => x.Start)) text = text.Remove(change.Start, change.Length).Insert(change.Start, change.Text);
        NoteDocument.Parse(text, "note.md", allowMissingId: true); return text;
    }
}

public static class KnowledgeWorkflows
{
    public static string Question(Guid id, string title, string body, DateTimeOffset instant)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("A question title is required.");
        var source = NoteDocument.AddId("# " + title.Trim().Replace('\n', ' ').Replace('\r', ' ') + "\n\n" + body.Trim() + "\n", id);
        return FrontMatter.SetScalars(source, new Dictionary<string, string> { ["type"] = "question", ["status"] = "open", ["created"] = instant.ToString("O"), ["updated"] = instant.ToString("O") });
    }
    public static string Answer(string source, string answer, DateTimeOffset instant)
    {
        if (NoteDocument.Parse(source, "question.md").Type != "question") throw new ArgumentException("This note is not a question.");
        if (string.IsNullOrWhiteSpace(answer)) throw new ArgumentException("An answer is required.");
        var next = source.TrimEnd() + "\n\n## Answer\n\n" + answer.Trim() + "\n";
        return FrontMatter.SetScalars(next, new Dictionary<string, string> { ["status"] = "answered", ["answered"] = instant.ToString("O"), ["updated"] = instant.ToString("O") });
    }
    public static string Link(Guid id, string url, string? title, string? comment, DateTimeOffset instant)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("Enter an HTTP or HTTPS link.");
        var label = string.IsNullOrWhiteSpace(title) ? uri.Host : title.Trim().Replace('\n', ' ').Replace('\r', ' ');
        var source = NoteDocument.AddId("# " + label + "\n\n<" + uri.AbsoluteUri.Replace(">", "%3E", StringComparison.Ordinal) + ">\n\n" + (comment ?? "").Trim() + "\n", id);
        return FrontMatter.SetScalars(source, new Dictionary<string, string> { ["type"] = "link", ["created"] = instant.ToString("O"), ["updated"] = instant.ToString("O") });
    }
    public static string Template(string source, Guid id, string title, DateOnly date, DateTimeOffset instant)
    {
        var parsed = NoteDocument.Parse(source, "template.md", allowMissingId: true);
        var result = parsed.Id is null ? NoteDocument.AddId(source, id) : NoteDocument.ReplaceId(source, id);
        parsed = NoteDocument.Parse(result, "template.md");
        // Body placeholders cannot inject YAML or change an identity; header values are edited separately.
        result = result[..parsed.HeaderEnd] + result[parsed.HeaderEnd..].Replace("{{title}}", title, StringComparison.Ordinal).Replace("{{date}}", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return FrontMatter.SetScalars(result, new Dictionary<string, string> { ["title"] = title, ["created"] = instant.ToString("O"), ["updated"] = instant.ToString("O") });
    }
    public static string AppendCapture(string destinationSource, string destinationPath, string captureSource, string capturePath)
    {
        var body = PortableNoteLinks.Rewrite(captureSource, capturePath, destinationPath, new Dictionary<string, string>());
        body = NoteDocument.Parse(body, capturePath).Body.Trim();
        return destinationSource.TrimEnd() + "\n\n" + body + "\n";
    }
}

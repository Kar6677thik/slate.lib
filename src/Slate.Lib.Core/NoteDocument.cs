using Markdig;
using Markdig.Syntax;
using System.Text;
using System.Text.Json;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Slate.Lib.Core;

public sealed record NoteDocument(
    Guid? Id,
    string Title,
    string Body,
    int HeaderEnd,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Headings,
    string? Type,
    string? Status,
    string PlainText)
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public static NoteDocument Parse(string source, string path, bool allowMissingId = false)
    {
        var text = source.TrimStart('\uFEFF');
        YamlMappingNode? metadata = null;
        var headerEnd = 0;
        if (text.StartsWith("---\n", StringComparison.Ordinal) || text.StartsWith("---\r\n", StringComparison.Ordinal))
        {
            var start = text.IndexOf('\n') + 1;
            var cursor = start;
            while (cursor < text.Length)
            {
                var end = text.IndexOf('\n', cursor);
                if (end < 0) end = text.Length;
                if (text[cursor..end].TrimEnd('\r') == "---")
                {
                    headerEnd = Math.Min(end + 1, text.Length);
                    break;
                }
                cursor = end + 1;
            }
            if (headerEnd == 0) throw new InvalidDataException("Front matter has no closing --- delimiter.");
            var yaml = text[start..cursor];
            if (Encoding.UTF8.GetByteCount(yaml) > 32 * 1024)
                throw new InvalidDataException("Front matter exceeds 32 KiB.");
            try
            {
                var events = new Parser(new StringReader(yaml));
                var depth = 0;
                var count = 0;
                while (events.MoveNext())
                {
                    if (++count > 4096 || events.Current is AnchorAlias)
                        throw new InvalidDataException("YAML aliases or excessive complexity are not supported.");
                    if (events.Current is NodeEvent node && (!node.Anchor.IsEmpty || !node.Tag.IsEmpty))
                        throw new InvalidDataException("YAML anchors and explicit tags are not supported.");
                    if (events.Current is MappingStart or SequenceStart && ++depth > 16)
                        throw new InvalidDataException("Front matter is nested too deeply.");
                    if (events.Current is MappingEnd or SequenceEnd) depth--;
                }
                var stream = new YamlStream();
                stream.Load(new StringReader(yaml));
                if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode mapping)
                    throw new InvalidDataException("Front matter must be one YAML mapping.");
                metadata = mapping;
            }
            catch (YamlException exception)
            {
                throw new InvalidDataException("Invalid YAML front matter (including duplicate keys).", exception);
            }
        }

        string? Scalar(string key)
        {
            if (metadata is null || !metadata.Children.TryGetValue(new YamlScalarNode(key), out var value)) return null;
            if (value is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value))
                throw new InvalidDataException($"Front matter '{key}' must be a nonempty scalar.");
            return scalar.Value;
        }

        IReadOnlyList<string> Scalars(string key)
        {
            if (metadata is null || !metadata.Children.TryGetValue(new YamlScalarNode(key), out var value)) return [];
            IEnumerable<YamlNode> nodes = value switch
            {
                YamlScalarNode => [value],
                YamlSequenceNode sequence => sequence.Children,
                _ => throw new InvalidDataException($"Front matter '{key}' must be a scalar or scalar list.")
            };
            var result = new List<string>();
            foreach (var node in nodes)
            {
                if (node is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value))
                    throw new InvalidDataException($"Front matter '{key}' must contain nonempty scalars.");
                result.Add(scalar.Value.Trim());
            }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        Guid? id = null;
        if (Scalar("id") is { } idText)
        {
            if (!Guid.TryParseExact(idText, "D", out var parsed) || idText[14] != '4' ||
                !"89abAB".Contains(idText[19]))
                throw new InvalidDataException("Note ID must be a UUIDv4.");
            id = parsed;
        }
        if (id is null && !allowMissingId)
            throw new InvalidDataException("Note has no ID. Run --initialize-library before starting the server.");

        var body = text[headerEnd..];
        var document = Markdown.Parse(body);
        var headingBlocks = document.Descendants<HeadingBlock>().ToArray();
        var heading = headingBlocks.FirstOrDefault(x => x.Level == 1);
        var headingTitle = heading is null ? null : Markdown.ToPlainText(body.Substring(heading.Span.Start, heading.Span.Length)).Trim();
        var headings = headingBlocks.Select(block => Markdown.ToPlainText(body.Substring(block.Span.Start, block.Span.Length)).Trim())
            .Where(value => value.Length > 0).ToArray();
        return new(id,
            Scalar("title") ?? (string.IsNullOrWhiteSpace(headingTitle) ? System.IO.Path.GetFileNameWithoutExtension(path) : headingTitle),
            body,
            headerEnd,
            Scalars("aliases"),
            Scalars("tags"),
            headings,
            Scalar("type"),
            Scalar("status"),
            Markdown.ToPlainText(body).Trim());
    }

    public static string AddId(string source, Guid id)
    {
        var text = source.TrimStart('\uFEFF');
        if (text.StartsWith("---\n", StringComparison.Ordinal) || text.StartsWith("---\r\n", StringComparison.Ordinal))
        {
            var end = text.IndexOf('\n') + 1;
            return text.Insert(end, $"id: {id:D}\n");
        }
        return $"---\nid: {id:D}\n---\n\n{text}";
    }

    public static string ReplaceId(string source, Guid id)
    {
        var parsed = Parse(source, "copied.md");
        var text = source.TrimStart('\uFEFF');
        var header = text[..parsed.HeaderEnd];
        var lines = header.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].TrimEnd('\r').StartsWith("id:", StringComparison.Ordinal)) continue;
            var ending = lines[index].EndsWith('\r') ? "\r" : "";
            lines[index] = $"id: {id:D}{ending}";
            return string.Join('\n', lines) + text[parsed.HeaderEnd..];
        }
        throw new InvalidDataException("Note ID was not found in front matter.");
    }

    public static string AddAlias(string source, string alias)
    {
        var parsed = Parse(source, "renamed.md");
        if (parsed.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase)) return source;
        var text = source.TrimStart('\uFEFF');
        var header = text[..parsed.HeaderEnd];
        var lines = header.Split('\n').ToList();
        var aliasIndex = lines.FindIndex(line => line.TrimEnd('\r').StartsWith("aliases:", StringComparison.Ordinal));
        var quoted = JsonSerializer.Serialize(alias);
        if (aliasIndex < 0)
        {
            var idIndex = lines.FindIndex(line => line.TrimEnd('\r').StartsWith("id:", StringComparison.Ordinal));
            lines.Insert(idIndex < 0 ? 1 : idIndex + 1, $"aliases: [{quoted}]");
        }
        else
        {
            var ending = lines[aliasIndex].EndsWith('\r') ? "\r" : "";
            var aliases = parsed.Aliases.Append(alias).Distinct(StringComparer.OrdinalIgnoreCase).Select(value => JsonSerializer.Serialize(value));
            lines[aliasIndex] = "aliases: [" + string.Join(", ", aliases) + "]" + ending;
            var remove = aliasIndex + 1;
            while (remove < lines.Count && (lines[remove].StartsWith("  -", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(lines[remove])))
                lines.RemoveAt(remove);
        }
        var updated = string.Join('\n', lines) + text[parsed.HeaderEnd..];
        Parse(updated, "renamed.md");
        return updated;
    }
}

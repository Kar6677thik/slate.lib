namespace Slate.Lib.Core;

/// <summary>Selection is independent of native controls, folder expansion and note navigation.</summary>
public sealed class ExplorerSelection
{
    private readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
    private string? anchor;
    public IReadOnlySet<string> Paths => selected;
    public int Count => selected.Count;
    public void Clear() { selected.Clear(); anchor = null; }
    public void Select(string path, IReadOnlyList<string> visible, bool toggle = false, bool range = false)
    {
        if (!visible.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        if (range && anchor is not null)
        {
            var start = Index(visible, anchor); var end = Index(visible, path);
            if (start >= 0)
            {
                if (!toggle) selected.Clear();
                for (var i = Math.Min(start, end); i <= Math.Max(start, end); i++) selected.Add(visible[i]);
                return;
            }
        }
        if (!toggle) selected.Clear();
        if (!toggle || !selected.Remove(path)) selected.Add(path);
        anchor = path;
    }
    public void SelectAll(IEnumerable<string> visible) { foreach (var path in visible) selected.Add(path); }
    public void Retain(IEnumerable<string> existing) => selected.IntersectWith(existing);
    private static int Index(IReadOnlyList<string> paths, string path)
    { for (var i = 0; i < paths.Count; i++) if (paths[i].Equals(path, StringComparison.OrdinalIgnoreCase)) return i; return -1; }
}

public sealed record EditSelection(string Text, int Cursor, int Length);

public static class MarkdownEditing
{
    public static EditSelection Transform(string text, int cursor, int length, string command)
    {
        cursor = Math.Clamp(cursor, 0, text.Length); length = Math.Clamp(length, 0, text.Length - cursor);
        var chosen = text.Substring(cursor, length);
        var (before, after, placeholder) = command switch
        {
            "bold" => ("**", "**", "bold text"), "italic" => ("*", "*", "italic text"),
            "code" => ("`", "`", "code"), "link" => ("[", "](https://)", "link text"),
            "wiki" => ("[[", "]]", "Note title"), "fence" => ("```\n", "\n```", "code"),
            _ => ("", "", "")
        };
        if (before.Length > 0)
        {
            var value = length == 0 ? placeholder : chosen;
            var replacement = before + value + after;
            return new(text.Remove(cursor, length).Insert(cursor, replacement), cursor + before.Length, value.Length);
        }
        var prefix = command switch
        {
            "bullet" => "- ", "number" => "1. ", "task" => "- [ ] ", "quote" => "> ",
            "h1" => "# ", "h2" => "## ", "h3" => "### ", _ => throw new ArgumentException("Unknown Markdown command.")
        };
        var start = cursor == 0 ? 0 : text.LastIndexOf('\n', cursor - 1) + 1;
        var end = text.IndexOf('\n', Math.Min(text.Length, cursor + length)); if (end < 0) end = text.Length;
        var lines = text[start..end].Split('\n');
        var replacementLines = lines.Select((line, i) => (command == "number" ? $"{i + 1}. " : prefix) + line);
        var inserted = string.Join('\n', replacementLines);
        return new(text[..start] + inserted + text[end..], start + prefix.Length, length == 0 ? 0 : inserted.Length - prefix.Length);
    }
}

public sealed record SlateCommand(string Id, string Title, Func<Task> Execute, Func<bool>? Available = null, string? Shortcut = null);

public sealed class CommandRegistry
{
    private readonly Dictionary<string, SlateCommand> commands = new(StringComparer.Ordinal);
    public void Register(SlateCommand command) => commands.Add(command.Id, command);
    public async Task<bool> ExecuteAsync(string id)
    {
        if (!commands.TryGetValue(id, out var command) || command.Available?.Invoke() == false) return false;
        await command.Execute(); return true;
    }
    public IReadOnlyList<SlateCommand> Search(string query) => commands.Values.Where(c => c.Available?.Invoke() != false)
        .Select(c => (Command: c, Score: FuzzyScore(c.Title, query)))
        .Where(x => x.Score >= 0).OrderByDescending(x => x.Score).ThenBy(x => x.Command.Title, StringComparer.OrdinalIgnoreCase)
        .Select(x => x.Command).ToArray();
    private static int FuzzyScore(string title, string query)
    {
        title = title.ToLowerInvariant(); query = query.Trim().ToLowerInvariant();
        if (query.Length == 0) return 0;
        if (title.Contains(query, StringComparison.Ordinal)) return 1000 - title.IndexOf(query, StringComparison.Ordinal);
        var cursor = 0; var score = 0; var previous = -2;
        foreach (var ch in query)
        {
            var found = title.IndexOf(ch, cursor); if (found < 0) return -1;
            score += found == previous + 1 ? 10 : 1; previous = found; cursor = found + 1;
        }
        return score;
    }
}

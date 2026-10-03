using System.Text.RegularExpressions;

namespace Slate.Lib.Core;

public sealed record TextEdit(int Start, int Count, IReadOnlyList<string> Replacement);
public sealed record MergeBlock(string Base, string Local, string Current, string Proposed, bool Conflict);
public sealed record TextMergeResult(IReadOnlyList<MergeBlock> Blocks)
{
    public bool HasConflicts => Blocks.Any(x => x.Conflict);
    public string Proposed => string.Concat(Blocks.Select(x => x.Proposed));
}

public static class TextMerge
{
    private static string[] Lines(string value)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(value) > NoteDocument.MaxBytes) throw new InvalidDataException("Comparison is limited to 2 MiB per version.");
        return Regex.Matches(NoteDocument.NormalizeLineEndings(value), @"[^\n]*\n|[^\n]+$").Select(x => x.Value).ToArray();
    }
    public static IReadOnlyList<TextEdit> Diff(string before, string after) => Edits(Lines(before), Lines(after));
    private static IReadOnlyList<TextEdit> Edits(string[] before, string[] after)
    {
        var prefix = 0; while (prefix < Math.Min(before.Length, after.Length) && before[prefix] == after[prefix]) prefix++;
        var suffix = 0; while (suffix < Math.Min(before.Length, after.Length) - prefix && before[^(suffix + 1)] == after[^(suffix + 1)]) suffix++;
        var left = before.Length - prefix - suffix; var right = after.Length - prefix - suffix;
        if (left == 0 && right == 0) return [];
        // Large comparisons become one conservative edit, never an expensive quadratic UI operation.
        if ((long)(left + 1) * (right + 1) > 2_000_000) return [new(prefix, left, after.Skip(prefix).Take(right).ToArray())];
        var lcs = new int[left + 1, right + 1];
        for (var i = left - 1; i >= 0; i--) for (var j = right - 1; j >= 0; j--)
            lcs[i, j] = before[prefix + i] == after[prefix + j] ? 1 + lcs[i + 1, j + 1] : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        var edits = new List<TextEdit>(); var a = 0; var b = 0;
        while (a < left || b < right)
        {
            if (a < left && b < right && before[prefix + a] == after[prefix + b]) { a++; b++; continue; }
            var start = a; var replacement = new List<string>();
            while (a < left || b < right)
            {
                if (a < left && b < right && before[prefix + a] == after[prefix + b]) break;
                if (b < right && (a == left || lcs[a, b + 1] > lcs[a + 1, b])) replacement.Add(after[prefix + b++]); else a++;
            }
            edits.Add(new(prefix + start, a - start, replacement));
        }
        return edits;
    }
    public static TextMergeResult Merge(string basis, string local, string current)
    {
        var source = Lines(basis); var left = Edits(source, Lines(local)); var right = Edits(source, Lines(current));
        var all = left.Select(x => (Edit: x, Local: true)).Concat(right.Select(x => (Edit: x, Local: false))).OrderBy(x => x.Edit.Start).ToArray();
        var blocks = new List<MergeBlock>(); var position = 0; var index = 0;
        while (index < all.Length)
        {
            var start = all[index].Edit.Start;
            if (position < start) { var unchanged = string.Concat(source[position..start]); blocks.Add(new(unchanged, unchanged, unchanged, unchanged, false)); }
            var group = new List<(TextEdit Edit, bool Local)> { all[index++] }; var end = start + group[0].Edit.Count;
            while (index < all.Length && (all[index].Edit.Start < end || all[index].Edit.Start == start || (all[index].Edit.Start == end && (all[index].Edit.Count == 0 || group.Any(x => x.Edit.Count == 0 && x.Edit.Start == end)))))
            { group.Add(all[index]); end = Math.Max(end, all[index].Edit.Start + all[index].Edit.Count); index++; }
            string Apply(IEnumerable<TextEdit> changes)
            {
                var output = new System.Text.StringBuilder(); var cursor = start;
                foreach (var edit in changes.OrderBy(x => x.Start)) { output.Append(string.Concat(source[cursor..edit.Start])); output.Append(string.Concat(edit.Replacement)); cursor = edit.Start + edit.Count; }
                output.Append(string.Concat(source[cursor..end])); return output.ToString();
            }
            var original = string.Concat(source[start..end]); var localText = Apply(group.Where(x => x.Local).Select(x => x.Edit)); var currentText = Apply(group.Where(x => !x.Local).Select(x => x.Edit));
            var conflict = group.Any(x => x.Local) && group.Any(x => !x.Local) && localText != currentText;
            blocks.Add(new(original, localText, currentText, group.Any(x => x.Local) ? localText : currentText, conflict)); position = end;
        }
        if (position < source.Length) { var rest = string.Concat(source[position..]); blocks.Add(new(rest, rest, rest, rest, false)); }
        return new(blocks);
    }
    public static string VisualDiff(string before, string after)
    {
        var lines = Lines(before); var edits = Edits(lines, Lines(after)); var result = new System.Text.StringBuilder();
        foreach (var edit in edits)
        {
            result.AppendLine($"@@ line {edit.Start + 1} · {edit.Count} removed, {edit.Replacement.Count} added @@");
            foreach (var line in lines.Skip(edit.Start).Take(edit.Count)) result.Append("− ").Append(line).Append(line.EndsWith('\n') ? "" : "\n");
            foreach (var line in edit.Replacement) result.Append("+ ").Append(line).Append(line.EndsWith('\n') ? "" : "\n");
        }
        return result.Length == 0 ? "No text changes." : result.ToString();
    }
}

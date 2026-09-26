using System.Text;
using System.Text.RegularExpressions;

namespace Slate.Lib.Core;

public sealed record WikiLinkToken(int Start, int Length, string Raw, string Target, string? Label, string? Heading);

public static partial class WikiLinks
{
    [GeneratedRegex(@"\[\[([^\]\r\n]+)\]\]", RegexOptions.CultureInvariant)]
    private static partial Regex LinkPattern();

    public static IReadOnlyList<WikiLinkToken> Extract(string markdown)
    {
        var result = new List<WikiLinkToken>();
        var fenced = false;
        var offset = 0;
        foreach (var segment in markdown.Split('\n'))
        {
            var line = segment.TrimEnd('\r');
            var trim = line.TrimStart();
            if (trim.StartsWith("```", StringComparison.Ordinal) || trim.StartsWith("~~~", StringComparison.Ordinal)) { fenced = !fenced; offset += segment.Length + 1; continue; }
            if (!fenced)
            {
                foreach (Match match in LinkPattern().Matches(line))
                {
                    if (InsideInlineCode(line, match.Index)) continue;
                    var inner = match.Groups[1].Value.Trim();
                    var pipe = inner.IndexOf('|');
                    var destination = (pipe < 0 ? inner : inner[..pipe]).Trim();
                    var label = pipe < 0 ? null : inner[(pipe + 1)..].Trim();
                    var hash = destination.IndexOf('#');
                    var target = (hash < 0 ? destination : destination[..hash]).Trim();
                    var heading = hash < 0 ? null : destination[(hash + 1)..].Trim();
                    if (target.Length > 0) result.Add(new(offset + match.Index, match.Length, match.Value, target, string.IsNullOrWhiteSpace(label) ? null : label, string.IsNullOrWhiteSpace(heading) ? null : heading));
                }
            }
            offset += segment.Length + 1;
        }
        return result;
    }

    private static bool InsideInlineCode(string line, int position)
    {
        var ticks = 0;
        for (var index = 0; index < position; index++) if (line[index] == '`') ticks++;
        return ticks % 2 == 1;
    }
}

public sealed class HeadingSlugger
{
    private readonly HashSet<string> used = new(StringComparer.Ordinal);

    public string Add(string heading)
    {
        var normalized = heading.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        var builder = new StringBuilder();
        var pendingDash = false;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune) || rune.Value is '-' or '_')
            {
                if (pendingDash && builder.Length > 0 && builder[^1] != '-') builder.Append('-');
                builder.Append(rune.ToString()); pendingDash = false;
            }
            else if (Rune.IsWhiteSpace(rune)) pendingDash = true;
        }
        var basis = builder.ToString().Trim('-');
        if (basis.Length == 0) basis = "section";
        var candidate = basis;
        for (var suffix = 1; !used.Add(candidate); suffix++) candidate = basis + "-" + suffix;
        return candidate;
    }

    public static string Slug(string heading) => new HeadingSlugger().Add(heading);
}

public static class AssetReferences
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".gif" };

    public static string ForNote(string notePath, AssetMetadata asset, string? label = null)
    {
        var relative = PathForNote(notePath, asset.Id, asset.Extension);
        var safeLabel = string.IsNullOrWhiteSpace(label) ? Path.GetFileNameWithoutExtension(asset.OriginalFilename) : label.Trim();
        return asset.InlineImage || ImageExtensions.Contains(asset.Extension) ? $"![{safeLabel}]({relative})" : $"[{safeLabel}]({relative})";
    }

    public static string PathForNote(string notePath, Guid assetId, string extension)
    {
        var depth = notePath.Count(c => c == '/');
        return string.Concat(Enumerable.Repeat("../", depth)) + ".assets/" + assetId.ToString("D") + extension;
    }

    public static bool TryParse(string notePath, string url, out Guid assetId)
    {
        assetId = default;
        if (url.StartsWith("asset://", StringComparison.OrdinalIgnoreCase)) return Guid.TryParse(url[8..].Split(['/', '?', '#'])[0], out assetId);
        var pathOnly = url.Split(['?', '#'])[0].Replace('\\', '/');
        var noteDirectory = notePath.Contains('/') ? notePath[..notePath.LastIndexOf('/')] : "";
        var parts = noteDirectory.Length == 0 ? new List<string>() : noteDirectory.Split('/').ToList();
        foreach (var part in pathOnly.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..") { if (parts.Count == 0) return false; parts.RemoveAt(parts.Count - 1); }
            else parts.Add(part);
        }
        if (parts.Count != 2 || parts[0] != ".assets") return false;
        return Guid.TryParse(Path.GetFileNameWithoutExtension(parts[1]), out assetId);
    }

    public static IReadOnlyList<Guid> Extract(string notePath, string markdown)
    {
        var ids = new HashSet<Guid>();
        foreach (Match match in Regex.Matches(markdown, @"!?\[[^\]\r\n]*\]\((?<url>[^)\s]+))", RegexOptions.CultureInvariant))
            if (TryParse(notePath, match.Groups["url"].Value, out var id)) ids.Add(id);
        return ids.ToArray();
    }
}

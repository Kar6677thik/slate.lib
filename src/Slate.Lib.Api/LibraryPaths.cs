using System.Text;

namespace Slate.Lib.Api;

public sealed class LibraryPaths
{
    public string Root { get; }
    public LibraryPaths(string root)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(Root)) throw new DirectoryNotFoundException($"Library directory does not exist: {Root}. Set Library:RootPath.");
        for (var directory = new DirectoryInfo(Root); directory is not null; directory = directory.Parent)
            RejectLink(directory.FullName);
    }

    public string Resolve(string relative, bool allowRoot = false)
    {
        if (relative == "" && allowRoot) { RejectLink(Root); return Root; }
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 220 || relative != relative.Normalize(NormalizationForm.FormC) || Path.IsPathRooted(relative))
            throw new ArgumentException("Invalid library-relative path.");
        var segments = relative.Split('/');
        foreach (var segment in segments)
        {
            var stem = segment.Split('.')[0];
            if (segment.Length is 0 or > 100 || segment is "." or ".." || segment.StartsWith('.') ||
                segment.EndsWith('.') || segment.EndsWith(' ') || segment.Any(c => char.IsControl(c) || "<>:\"\\|?*".Contains(c)) ||
                new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Invalid or reserved library path segment.");
        }
        var full = Path.GetFullPath(Path.Combine(Root, Path.Combine(segments)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, comparison)) throw new ArgumentException("Path escapes the library.");
        RejectLink(Root);
        var current = Root;
        foreach (var segment in segments) { current = Path.Combine(current, segment); RejectLink(current); }
        return full;
    }

    public string ValidateName(string name)
    {
        if (name.Contains('/')) throw new ArgumentException("A name must be one library path segment.");
        Resolve(name);
        return name;
    }

    public static void RejectLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Symbolic links and junctions are not exposed by the library.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
}

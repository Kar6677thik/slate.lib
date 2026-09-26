using System.Text;
using System.Text.RegularExpressions;
using Slate.Lib.Core;

namespace Slate.Lib.Api;

public sealed class LibraryConflictException(string message) : Exception(message);
public sealed class LibraryPreconditionException(string message) : Exception(message);

public sealed partial class LibraryStore
{
    public LibraryNote CreateNote(CreateNoteRequest request)
    {
        lock (gate)
        {
            var folder = paths.Resolve(request.FolderPath, true);
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException();
            var name = request.Name.Trim();
            if (!name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) name += ".md";
            paths.ValidateName(name);
            var relative = Join(request.FolderPath, name);
            var destination = paths.Resolve(relative);
            EnsureAvailable(destination);
            var id = request.Id is { } supplied && supplied != Guid.Empty ? supplied : Guid.NewGuid();
            if (notes.ContainsKey(id)) throw new LibraryConflictException("That note ID already exists.");
            var title = string.IsNullOrWhiteSpace(request.Title) ? Path.GetFileNameWithoutExtension(name) : request.Title.Trim();
            var source = string.IsNullOrWhiteSpace(request.InitialMarkdown)
                ? $"---\nid: {id:D}\n---\n\n# {title}\n\n"
                : PrepareInitialMarkdown(request.InitialMarkdown, id, relative);
            NoteDocument.Parse(source, relative);
            WriteNew(destination, source);
            Register(relative, source);
            var created = ReadPath(relative);
            IndexNote(created);
            return created;
        }
    }

    public LibraryNote Capture(CaptureNoteRequest request)
    {
        lock (gate)
        {
            if (request.CaptureId == Guid.Empty) throw new ArgumentException("A capture ID is required.");
            if (request.Kind is not ("quick-thought" or "share")) throw new ArgumentException("Unsupported capture kind.");
            var content = string.IsNullOrWhiteSpace(request.Content) ? null : NormalizeCaptureText(request.Content, "Capture content");
            var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : NormalizeCaptureText(request.Comment, "Capture comment");
            var assetIds = (request.AssetIds ?? []).Distinct().ToArray();
            if (assetIds.Length > 8) throw new ArgumentException("A capture can include at most eight attachments.");
            if (content is null && comment is null && assetIds.Length == 0) throw new ArgumentException("Capture content or an attachment is required.");

            const string inbox = "inbox";
            var inboxPath = paths.Resolve(inbox, true);
            if (!Directory.Exists(inboxPath))
            {
                Directory.CreateDirectory(inboxPath);
                WriteNew(Path.Combine(inboxPath, ".gitkeep"), "");
            }
            var instant = request.CapturedAt ?? DateTimeOffset.UtcNow;
            var stem = Slug(content ?? comment ?? "attachment");
            var name = $"{instant:yyyy-MM-dd}-{stem}-{request.CaptureId:N}"[..Math.Min(92, 11 + stem.Length + 1 + 32)] + ".md";
            var relative = Join(inbox, name);
            var destination = paths.Resolve(relative);
            var assetMarkdown = assetIds.Select(id => assets?.ReadMetadata(id) ?? throw new InvalidOperationException("Assets are not configured."))
                .Select(metadata => AssetReferences.ForNote(relative, metadata, metadata.OriginalFilename));
            var sections = assetMarkdown.Concat(content is null ? [] : [content]).Concat(comment is null ? [] : [comment]);
            var source = $"---\nid: {request.CaptureId:D}\n---\n\n{string.Join("\n\n", sections)}\n";
            if (Encoding.UTF8.GetByteCount(source) > NoteDocument.MaxBytes) throw new InvalidDataException("Capture exceeds 2 MiB.");

            if (notes.TryGetValue(request.CaptureId, out _))
            {
                var existing = Read(request.CaptureId);
                if (existing.Markdown == source) return existing;
                throw new LibraryConflictException("That capture ID already belongs to different content.");
            }

            EnsureAvailable(destination);
            WriteNew(destination, source);
            Register(relative, source);
            var created = ReadPath(relative);
            IndexNote(created);
            return created;
        }
    }

    private static string PrepareInitialMarkdown(string markdown, Guid id, string relative)
    {
        var source = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
        NoteDocument parsed;
        try { parsed = NoteDocument.Parse(source, relative, allowMissingId: true); }
        catch (InvalidDataException) { throw; }
        if (parsed.Id is null) source = NoteDocument.AddId(source, id);
        else if (parsed.Id != id) throw new InvalidDataException("Initial Markdown must use the requested note ID.");
        NoteDocument.Parse(source, relative);
        return source;
    }

    private static string NormalizeCaptureText(string? value, string label)
    {
        var text = value?.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException(label + " is required.");
        if (text.IndexOf('\0') >= 0) throw new ArgumentException(label + " contains an invalid character.");
        return text;
    }

    private static string Slug(string text)
    {
        var firstLine = text.Split('\n', 2)[0].Trim();
        var slug = Regex.Replace(firstLine.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "thought" : slug[..Math.Min(40, slug.Length)];
    }

    public LibraryMutation CreateFolder(CreateFolderRequest request)
    {
        lock (gate)
        {
            var parent = paths.Resolve(request.ParentPath, true);
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException();
            var name = paths.ValidateName(request.Name.Trim());
            var relative = Join(request.ParentPath, name);
            var destination = paths.Resolve(relative);
            EnsureAvailable(destination);
            Directory.CreateDirectory(destination);
            WriteNew(Path.Combine(destination, ".gitkeep"), "");
            git?.MarkPending();
            Interlocked.Increment(ref mutationVersion);
            return new(relative, true);
        }
    }

    public LibraryNote Update(Guid id, UpdateNoteRequest request)
    {
        lock (gate)
        {
            if (!notes.TryGetValue(id, out var relative)) throw new FileNotFoundException();
            var current = ReadPath(relative);
            if (!string.Equals(current.Revision, request.Revision, StringComparison.Ordinal))
                throw new LibraryPreconditionException("The note revision is stale.");
            var bytes = Utf8.GetByteCount(request.Markdown);
            if (bytes > NoteDocument.MaxBytes) throw new InvalidDataException("Markdown note exceeds 2 MiB.");
            var parsed = NoteDocument.Parse(request.Markdown, relative);
            if (parsed.Id != id) throw new InvalidDataException("An update must preserve the note ID.");
            if (request.Markdown == current.Markdown) return current;
            AtomicReplace(paths.Resolve(relative), request.Markdown);
            Register(relative, request.Markdown);
            var saved = ReadPath(relative);
            IndexNote(saved);
            return saved;
        }
    }

    public LibraryMutation Rename(RenameItemRequest request)
    {
        lock (gate)
        {
            var source = ExistingItem(request.Path, out var isDirectory);
            var parent = Parent(request.Path);
            var name = request.NewName.Trim();
            if (!isDirectory && !name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) name += ".md";
            paths.ValidateName(name);
            var targetRelative = Join(parent, name);
            var destination = paths.Resolve(targetRelative);
            MovePhysical(source, destination, isDirectory);
            if (!isDirectory)
            {
                var markdown = ReadFile(targetRelative);
                var oldStem = Path.GetFileNameWithoutExtension(request.Path);
                var newStem = Path.GetFileNameWithoutExtension(targetRelative);
                if (!oldStem.Equals(newStem, StringComparison.OrdinalIgnoreCase)) markdown = NoteDocument.AddAlias(markdown, oldStem);
                markdown = RewriteAssetReferences(markdown, request.Path, targetRelative);
                AtomicReplace(destination, markdown);
            }
            else RewriteMovedFolderAssets(request.Path, targetRelative);
            RebuildIndex();
            StructuralChange();
            return Result(targetRelative, isDirectory);
        }
    }

    public LibraryMutation Move(TransferItemRequest request)
    {
        lock (gate)
        {
            var source = ExistingItem(request.SourcePath, out var isDirectory);
            var destinationFolder = paths.Resolve(request.DestinationFolderPath, true);
            if (!Directory.Exists(destinationFolder)) throw new DirectoryNotFoundException();
            var targetRelative = Join(request.DestinationFolderPath, Path.GetFileName(request.SourcePath));
            if (isDirectory && IsSameOrDescendant(targetRelative, request.SourcePath))
                throw new ArgumentException("A folder cannot be moved into itself or a descendant.");
            var destination = paths.Resolve(targetRelative);
            MovePhysical(source, destination, isDirectory);
            if (isDirectory) RewriteMovedFolderAssets(request.SourcePath, targetRelative);
            else
            {
                var markdown = ReadFile(targetRelative);
                var rewritten = RewriteAssetReferences(markdown, request.SourcePath, targetRelative);
                if (rewritten != markdown) AtomicReplace(destination, rewritten);
            }
            RebuildIndex();
            StructuralChange();
            return Result(targetRelative, isDirectory);
        }
    }

    public LibraryMutation Copy(TransferItemRequest request)
    {
        lock (gate)
        {
            var source = ExistingItem(request.SourcePath, out var isDirectory);
            var folder = paths.Resolve(request.DestinationFolderPath, true);
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException();
            var originalName = Path.GetFileName(request.SourcePath);
            var candidate = UniqueCopyName(folder, originalName, isDirectory);
            var targetRelative = Join(request.DestinationFolderPath, candidate);
            var destination = paths.Resolve(targetRelative);
            if (isDirectory)
            {
                if (IsSameOrDescendant(targetRelative, request.SourcePath))
                    throw new ArgumentException("A folder cannot be copied into itself or a descendant.");
                CopyDirectory(source, destination);
                RewriteMovedFolderAssets(request.SourcePath, targetRelative);
            }
            else
            {
                var copied = NoteDocument.ReplaceId(ReadFile(request.SourcePath), Guid.NewGuid());
                copied = RewriteAssetReferences(copied, request.SourcePath, targetRelative);
                WriteNew(destination, copied);
            }
            RebuildIndex();
            StructuralChange();
            return Result(targetRelative, isDirectory);
        }
    }

    public ItemDetails Details(string relative)
    {
        lock (gate)
        {
            var full = ExistingItem(relative, out var isDirectory);
            var count = isDirectory ? Directory.EnumerateFileSystemEntries(full, "*", SearchOption.AllDirectories)
                .Count(entry => Path.GetFileName(entry) != ".gitkeep") : 0;
            return new(relative, isDirectory, count);
        }
    }

    public LibraryMutation Delete(DeleteItemRequest request)
    {
        git?.FlushAsync(this).GetAwaiter().GetResult();
        lock (gate)
        {
            var full = ExistingItem(request.Path, out var isDirectory);
            var descendants = isDirectory ? Directory.EnumerateFileSystemEntries(full, "*", SearchOption.AllDirectories)
                .Where(entry => Path.GetFileName(entry) != ".gitkeep").ToArray() : [];
            var count = isDirectory ? descendants.Length + 1 : 1;
            if (isDirectory && count > 1 && !request.Recursive)
                throw new LibraryConflictException("The folder is not empty; recursive confirmation is required.");
            if (isDirectory)
            {
                if (descendants.Length == 0)
                {
                    var marker = Path.Combine(full, ".gitkeep"); if (File.Exists(marker)) File.Delete(marker);
                    Directory.Delete(full, false);
                }
                else Directory.Delete(full, request.Recursive);
            }
            else File.Delete(full);
            RebuildIndex();
            StructuralChange();
            return new(request.Path, isDirectory, null, count);
        }
    }

    public void Refresh()
    {
        lock (gate) { RebuildIndex(); ReconcileSearch(); ReconcileLinks(); Interlocked.Increment(ref mutationVersion); }
    }

    private string ExistingItem(string relative, out bool isDirectory)
    {
        var full = paths.Resolve(relative);
        isDirectory = Directory.Exists(full);
        if (!isDirectory && !File.Exists(full)) throw new FileNotFoundException();
        if (!isDirectory && !Path.GetExtension(relative).Equals(".md", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only Markdown files can be changed.");
        return full;
    }

    private static string Parent(string relative) => relative.Contains('/') ? relative[..relative.LastIndexOf('/')] : "";
    private static string Join(string parent, string name) => parent.Length == 0 ? name : parent + "/" + name;
    private static bool IsSameOrDescendant(string candidate, string source) =>
        candidate.Equals(source, StringComparison.OrdinalIgnoreCase) || candidate.StartsWith(source + "/", StringComparison.OrdinalIgnoreCase);

    private static void EnsureAvailable(string destination)
    {
        var parent = Path.GetDirectoryName(destination)!;
        var name = Path.GetFileName(destination);
        if (File.Exists(destination) || Directory.Exists(destination) ||
            (Directory.Exists(parent) && Directory.EnumerateFileSystemEntries(parent).Any(entry =>
                Path.GetFileName(entry).Equals(name, StringComparison.OrdinalIgnoreCase))))
            throw new LibraryConflictException("The destination already exists.");
    }

    private static void WriteNew(string destination, string source)
    {
        using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var bytes = Utf8.GetBytes(source);
        stream.Write(bytes);
        stream.Flush(true);
    }

    private static void AtomicReplace(string destination, string source)
    {
        var temp = Path.Combine(Path.GetDirectoryName(destination)!, ".slate-tmp-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Utf8.GetBytes(source);
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temp, destination, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private void RewriteMovedFolderAssets(string oldFolder, string newFolder)
    {
        foreach (var full in Directory.EnumerateFiles(paths.Resolve(newFolder, true), "*.md", SearchOption.AllDirectories))
        {
            var newPath = Path.GetRelativePath(paths.Root, full).Replace('\\', '/');
            var suffix = newPath[(newFolder.Length + 1)..];
            var oldPath = Join(oldFolder, suffix);
            var markdown = ReadFile(newPath);
            var rewritten = RewriteAssetReferences(markdown, oldPath, newPath);
            if (rewritten != markdown) AtomicReplace(full, rewritten);
        }
    }

    private static string RewriteAssetReferences(string markdown, string oldPath, string newPath)
    {
        return Regex.Replace(markdown, @"(?<prefix>!?\[[^\]\r\n]*\]\()(?<url>[^)\s]+)(?<suffix>\))", match =>
        {
            var url = match.Groups["url"].Value;
            if (!AssetReferences.TryParse(oldPath, url, out var id)) return match.Value;
            var extension = Path.GetExtension(url.Split(['?', '#'])[0]);
            return match.Groups["prefix"].Value + AssetReferences.PathForNote(newPath, id, extension) + match.Groups["suffix"].Value;
        }, RegexOptions.CultureInvariant);
    }

    private static void MovePhysical(string source, string destination, bool directory)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(source, destination, comparison) && !string.Equals(source, destination, StringComparison.Ordinal))
        {
            var temp = Path.Combine(Path.GetDirectoryName(source)!, ".slate-tmp-" + Guid.NewGuid().ToString("N"));
            if (directory) { Directory.Move(source, temp); Directory.Move(temp, destination); }
            else { File.Move(source, temp); File.Move(temp, destination); }
            return;
        }
        EnsureAvailable(destination);
        if (directory) Directory.Move(source, destination); else File.Move(source, destination);
    }

    private void CopyDirectory(string source, string destination)
    {
        EnsureAvailable(destination);
        Directory.CreateDirectory(destination);
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                LibraryPaths.RejectLink(directory);
                var relative = Path.GetRelativePath(source, directory);
                Directory.CreateDirectory(Path.Combine(destination, relative));
            }
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                LibraryPaths.RejectLink(file);
                if (Path.GetFileName(file) == ".gitkeep")
                {
                    var marker = Path.GetRelativePath(source, file);
                    WriteNew(Path.Combine(destination, marker), "");
                    continue;
                }
                if (!Path.GetExtension(file).Equals(".md", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Folders containing non-Markdown files cannot be copied in this phase.");
                var relative = Path.GetRelativePath(source, file);
                var copied = NoteDocument.ReplaceId(File.ReadAllText(file, Utf8), Guid.NewGuid());
                WriteNew(Path.Combine(destination, relative), copied);
            }
        }
        catch { Directory.Delete(destination, true); throw; }
    }

    private static string UniqueCopyName(string folder, string original, bool directory)
    {
        var extension = directory ? "" : Path.GetExtension(original);
        var stem = directory ? original : Path.GetFileNameWithoutExtension(original);
        for (var number = 1; number < 10000; number++)
        {
            var suffix = number == 1 ? " copy" : $" copy {number}";
            var candidate = stem + suffix + extension;
            if (!File.Exists(Path.Combine(folder, candidate)) && !Directory.Exists(Path.Combine(folder, candidate))) return candidate;
        }
        throw new LibraryConflictException("No available copy name could be generated.");
    }

    private LibraryMutation Result(string relative, bool directory)
    {
        if (directory) return new(relative, true);
        var note = ReadPath(relative);
        return new(relative, false, note.Id);
    }

    private void RebuildIndex()
    {
        notes.Clear();
        foreach (var relative in Discover(paths)) Register(relative, ReadFile(relative));
    }
}

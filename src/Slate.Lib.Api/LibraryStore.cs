using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Slate.Lib.Core;

namespace Slate.Lib.Api;

public sealed partial class LibraryStore
{
    private readonly LibraryPaths paths;
    private readonly SearchIndex? search;
    private readonly GitSyncService? git;
    private readonly LinkIndex? links;
    private readonly AssetStore? assets;
    private readonly Dictionary<Guid, string> notes = [];
    private readonly Lock gate = new();
    private readonly string processVersion = Guid.NewGuid().ToString("N");
    private long mutationVersion;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public Guid LibraryId { get; }
    public int NoteCount { get { lock (gate) return notes.Count; } }

    public string LibraryVersion => processVersion + ":" + Interlocked.Read(ref mutationVersion);

    public LibraryStore(LibraryPaths paths) : this(paths, null, null, null, null) { }

    public LibraryStore(LibraryPaths paths, SearchIndex? search, GitSyncService? git) : this(paths, search, git, null, null) { }

    public LibraryStore(LibraryPaths paths, SearchIndex? search, GitSyncService? git, LinkIndex? links, AssetStore? assets)
    {
        this.paths = paths;
        this.search = search;
        this.git = git;
        this.links = links;
        this.assets = assets;
        LibraryPaths.RejectLink(Path.Combine(paths.Root, ".slate"));
        var identityPath = Path.Combine(paths.Root, ".slate", "library.json");
        LibraryPaths.RejectLink(identityPath);
        if (!File.Exists(identityPath)) throw new InvalidDataException("Library has not been initialized. Run --initialize-library first.");
        var identity = JsonSerializer.Deserialize<LibraryIdentity>(File.ReadAllText(identityPath), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (identity is null || identity.SchemaVersion != 1 || identity.LibraryId == Guid.Empty)
            throw new InvalidDataException("Unsupported or invalid .slate/library.json.");
        LibraryId = identity.LibraryId;
        RecoverBulkOperations();
        foreach (var path in Discover(paths)) Register(path, ReadFile(path));
        ReconcileSearch();
        ReconcileLinks();
    }

    public static IEnumerable<string> Discover(LibraryPaths paths, string relative = "")
    {
        var directory = paths.Resolve(relative, true);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var full in Directory.EnumerateFileSystemEntries(directory))
        {
            var name = Path.GetFileName(full);
            if (name.StartsWith('.')) continue;
            var child = relative.Length == 0 ? name : relative + "/" + name;
            paths.Resolve(child);
            if (!names.Add(name)) throw new InvalidDataException($"Case-colliding library entries: {child}");
            if (Directory.Exists(full))
            {
                foreach (var note in Discover(paths, child)) yield return note;
            }
            else if (Path.GetExtension(name).Equals(".md", StringComparison.OrdinalIgnoreCase)) yield return child;
        }
    }

    public FolderPage List(string relative, int page)
    {
        if (page < 0 || page > 1_000_000) throw new ArgumentException("Invalid page.");
        lock (gate)
        {
            var directory = paths.Resolve(relative, true);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException();
            var entries = new List<LibraryEntry>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var full in Directory.EnumerateFileSystemEntries(directory))
            {
                var name = Path.GetFileName(full);
                if (name.StartsWith('.')) continue;
                var child = relative.Length == 0 ? name : relative + "/" + name;
                paths.Resolve(child);
                if (!names.Add(name)) throw new InvalidDataException("Case-colliding names in this folder.");
                if (Directory.Exists(full)) entries.Add(new(name, child, true, null, null));
                else if (Path.GetExtension(name).Equals(".md", StringComparison.OrdinalIgnoreCase))
                {
                    var source = ReadFile(child);
                    var parsed = Register(child, source);
                    entries.Add(new(name, child, false, parsed.Id, parsed.Title));
                }
            }
            var ordered = entries.OrderByDescending(x => x.IsDirectory).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            var offset = page * 100;
            return new(relative, ordered.Skip(offset).Take(100).ToArray(), offset + 100 < ordered.Length ? page + 1 : null);
        }
    }

    public LibraryNote Read(Guid id)
    {
        lock (gate)
        {
            if (!notes.TryGetValue(id, out var relative)) throw new FileNotFoundException();
            var note = ReadPath(relative);
            if (note.Id != id) throw new InvalidDataException("The note identity changed. Restart the server after correcting the library.");
            return note;
        }
    }

    public LibraryNote ReadPath(string relative)
    {
        lock (gate)
        {
            if (!Path.GetExtension(relative).Equals(".md", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Only Markdown notes can be read.");
            var source = ReadFile(relative);
            var parsed = Register(relative, source);
            var id = parsed.Id!.Value;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var part in new[] { id.ToString("D"), relative, source })
            {
                var bytes = Utf8.GetBytes(part);
                hash.AppendData(BitConverter.GetBytes(bytes.Length));
                hash.AppendData(bytes);
            }
            return new(id, relative, parsed.Title, source, '"' + Convert.ToHexStringLower(hash.GetHashAndReset()) + '"');
        }
    }

    public string ReadFile(string relative)
    {
        var full = paths.Resolve(relative);
        using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > NoteDocument.MaxBytes) throw new InvalidDataException("Markdown note exceeds 2 MiB.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        try { return Utf8.GetString(bytes); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Markdown must be UTF-8.", exception); }
    }

    private NoteDocument Register(string path, string source)
    {
        var parsed = NoteDocument.Parse(source, path);
        var id = parsed.Id!.Value;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (notes.TryGetValue(id, out var previous) && !string.Equals(previous, path, comparison) && File.Exists(paths.Resolve(previous)))
            throw new InvalidDataException($"Duplicate note ID: {previous} and {path}");
        notes[id] = path;
        IndexAssetReferences(path, source, parsed);
        return parsed;
    }

    public IReadOnlyList<LibraryNote> AllNotes()
    {
        lock (gate) return notes.Keys.Select(Read).ToArray();
    }

    public SearchPage Search(string query, int page, int pageSize) =>
        search?.Search(query, page, pageSize) ?? throw new InvalidOperationException("Search is not configured.");
    public SearchPage SmartView(string view, int page, int pageSize) =>
        search?.Search("", page, pageSize, view) ?? throw new InvalidOperationException("Search is not configured.");

    public NoteLinks Links(Guid id) =>
        links?.Read(id) ?? throw new InvalidOperationException("Links are not configured.");

    internal T WithWriterLock<T>(Func<T> action) { lock (gate) return action(); }

    internal void RefreshAfterGitImport(IReadOnlyList<string> changedPaths)
    {
        lock (gate)
        {
            RebuildIndex();
            ReconcileSearch();
            ReconcileLinks();
            Interlocked.Increment(ref mutationVersion);
        }
    }

    private void ReconcileSearch()
    {
        if (search is null) return;
        try { search.Reconcile(notes.Keys.Select(Read).ToArray()); }
        catch { search.MarkFailed(); }
    }

    private void ReconcileLinks()
    {
        if (links is null) return;
        try { links.Reconcile(notes.Keys.Select(Read).ToArray()); search?.UpdateBacklinks(links.WithBacklinks()); }
        catch { /* Link metadata is derived and must never make note storage unavailable. */ }
    }

    private void IndexNote(LibraryNote note)
    {
        if (search is not null) try { search.Upsert(note); } catch { search.MarkFailed(); }
        if (links is not null) try { links.Upsert(note); search?.UpdateBacklinks(links.WithBacklinks()); } catch { ReconcileLinks(); }
        git?.MarkPending();
        Interlocked.Increment(ref mutationVersion);
    }

    private void StructuralChange()
    {
        ReconcileSearch();
        ReconcileLinks();
        git?.MarkPending();
        Interlocked.Increment(ref mutationVersion);
    }
}

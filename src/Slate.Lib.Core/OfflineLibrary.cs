using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace Slate.Lib.Core;

public sealed record OfflineSelection(string Key, string Label, Guid? NoteId, string? Folder, IReadOnlyList<Guid> Notes, string State,
    int Downloaded, DateTimeOffset? RefreshedAt = null, string? Error = null);
public sealed record OfflineManifest(int Schema, long QuotaBytes, IReadOnlyList<OfflineSelection> Selections);
public sealed record OfflineUsage(OfflineManifest Manifest, long UsedBytes);

/// <summary>Explicit downloads live in persistent app data, independent of the disposable cache and unsent drafts.</summary>
public sealed class OfflineLibrary
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string root;
    private readonly SemaphoreSlim gate;
    public OfflineQueue Queue { get; }
    public OfflineLibrary(string appDataRoot, Guid libraryId)
    {
        root = Path.Combine(appDataRoot, "libraries", libraryId.ToString("D"), "offline");
        gate = Gates.GetOrAdd(Path.GetFullPath(root), _ => new(1, 1));
        Queue = new OfflineQueue(Path.Combine(appDataRoot, "libraries", libraryId.ToString("D"), "queue"));
    }
    private string ManifestPath => Path.Combine(root, "manifest.json");
    private string NotePath(Guid id) => Path.Combine(root, "notes", id + ".json");
    private string AssetPath(Guid id) => Path.Combine(root, "assets", id + ".bin");
    public async Task<OfflineUsage> StatusAsync()
    {
        var manifest = File.Exists(ManifestPath) ? JsonSerializer.Deserialize<OfflineManifest>(await File.ReadAllTextAsync(ManifestPath), Json) : null;
        manifest ??= new(1, 256L * 1024 * 1024, []);
        if (manifest.Schema != 1) throw new InvalidDataException("Unsupported offline store version. Downloads and pending work were preserved.");
        return new(manifest, Directory.Exists(root) ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length) : 0);
    }
    private Task Save(OfflineManifest manifest) => AtomicJson.WriteAsync(ManifestPath, manifest, Json);
    public async Task SetQuotaAsync(long bytes)
    {
        await gate.WaitAsync();
        try
        {
            var state = await StatusAsync();
            if (bytes < 16L * 1024 * 1024 || bytes > 2L * 1024 * 1024 * 1024 || bytes < state.UsedBytes) throw new ArgumentException("Choose 16–2048 MiB, at least the current download size. Remove downloads explicitly first.");
            await Save(state.Manifest with { QuotaBytes = bytes });
        }
        finally { gate.Release(); }
    }
    public Task<OfflineSelection> DownloadAsync(LibraryApiClient api, Guid? noteId, string? folder, IProgress<string>? progress = null, CancellationToken token = default) =>
        Task.Run(() => DownloadCoreAsync(api, noteId, folder, progress, token), token);
    private async Task<OfflineSelection> DownloadCoreAsync(LibraryApiClient api, Guid? noteId, string? folder, IProgress<string>? progress, CancellationToken token)
    {
        if (noteId is null && folder is null) throw new ArgumentException("Choose a note or folder.");
        await gate.WaitAsync(token);
        try
        {
            var manifest = (await StatusAsync()).Manifest;
            var key = noteId is null ? "folder:" + folder : "note:" + noteId;
            var previous = manifest.Selections.FirstOrDefault(x => x.Key == key);
            if (previous is null && manifest.Selections.Count >= 200) throw new InvalidOperationException("At most 200 offline selections are supported.");
            var selection = new OfflineSelection(key, folder ?? noteId!.ToString()!, noteId, folder, previous?.Notes ?? [], "downloading", 0, previous?.RefreshedAt);
            async Task Checkpoint() { manifest = manifest with { Selections = manifest.Selections.Where(x => x.Key != key).Append(selection).ToArray() }; await Save(manifest); }
            await Checkpoint();
            try
            {
                var ids = new List<Guid>();
                if (noteId is { } requestedId) ids.Add(requestedId);
                else
                {
                    var folders = new Queue<string>(); folders.Enqueue(folder!); var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    while (folders.TryDequeue(out var current))
                    {
                        if (!visited.Add(current)) continue;
                        if (visited.Count > 2000) throw new InvalidDataException("Choose a smaller folder: limit 2000 directories.");
                        int? page = 0;
                        do
                        {
                            var listing = await api.ListAsync(current, page.Value, token);
                            foreach (var entry in listing.Entries) { if (entry.IsDirectory) folders.Enqueue(entry.Path); else if (entry.Id is { } child) ids.Add(child); }
                            if (ids.Count > 2000) throw new InvalidDataException("Choose a smaller folder: limit 2000 notes.");
                            page = listing.NextPage;
                        } while (page is not null);
                    }
                }
                var completed = new List<Guid>();
                foreach (var id in ids.Distinct())
                {
                    token.ThrowIfCancellationRequested(); progress?.Report($"Downloading {completed.Count + 1} of {ids.Count}…");
                    var note = await api.ReadAsync(id, token);
                    var payload = JsonSerializer.SerializeToUtf8Bytes(new CachedNoteRecord(note, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), Json);
                    await CheckSpace(payload.Length, manifest.QuotaBytes); // Budget temporary publication as well as the old copy.
                    foreach (var assetId in AssetReferences.Extract(note.Path, note.Markdown).Distinct())
                    {
                        if (File.Exists(AssetPath(assetId))) continue;
                        var metadata = await api.AssetMetadataAsync(assetId, token);
                        if (metadata.ByteSize > 25L * 1024 * 1024) throw new InvalidDataException("An attachment exceeds the offline limit.");
                        await CheckSpace(metadata.ByteSize + payload.Length + 4096, manifest.QuotaBytes);
                        Directory.CreateDirectory(Path.GetDirectoryName(AssetPath(assetId))!);
                        var temporary = AssetPath(assetId) + ".partial";
                        try
                        {
                            await api.DownloadAssetAsync(assetId, temporary, token, metadata.ByteSize);
                            await using (var stream = File.OpenRead(temporary))
                                if (stream.Length != metadata.ByteSize || Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token)) != metadata.Sha256) throw new InvalidDataException("Attachment verification failed.");
                            File.Move(temporary, AssetPath(assetId));
                            await AtomicJson.WriteAsync(AssetPath(assetId) + ".json", metadata, Json, token);
                        }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
                    await AtomicJson.WriteAsync(NotePath(id), new CachedNoteRecord(note, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), Json, token);
                    completed.Add(id); selection = selection with { Label = noteId is null ? folder! : note.Title, Notes = previous?.Notes.Union(completed).ToArray() ?? completed.ToArray(), Downloaded = completed.Count }; await Checkpoint();
                }
                selection = selection with { Notes = completed, State = "ready", RefreshedAt = DateTimeOffset.UtcNow }; await Checkpoint();
            }
            catch (Exception e) when (e is not OutOfMemoryException) { selection = selection with { State = "failed", Error = e.Message }; await Checkpoint(); }
            return selection;
        }
        finally { gate.Release(); }
    }
    private async Task CheckSpace(long additional, long quota) { if ((await StatusAsync()).UsedBytes + additional + 65536 > quota) throw new IOException("Offline quota reached. Existing downloads and drafts were kept. Increase the quota or remove downloads explicitly."); }
    public async Task<CachedNoteRecord?> ReadAsync(Guid id) => File.Exists(NotePath(id)) ? JsonSerializer.Deserialize<CachedNoteRecord>(await File.ReadAllTextAsync(NotePath(id)), Json) : null;
    public async Task<IReadOnlyList<CachedNoteRecord>> NotesAsync()
    {
        var notes = new List<CachedNoteRecord>();
        foreach (var id in (await StatusAsync()).Manifest.Selections.SelectMany(x => x.Notes).Distinct()) if (await ReadAsync(id) is { } note) notes.Add(note);
        return notes;
    }
    public async Task<IReadOnlyList<CachedNoteRecord>> SearchAsync(string query)
    {
        if (query.Length > 200) throw new ArgumentException("Offline search is limited to 200 characters.");
        return await Task.Run<IReadOnlyList<CachedNoteRecord>>(async () =>
        {
            var notes = await NotesAsync();
            var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return notes.Where(x => terms.All(t => (x.Note.Title + " " + x.Note.Path + " " + x.Note.Markdown).Contains(t, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.Note.Title.Equals(query, StringComparison.OrdinalIgnoreCase)).ThenBy(x => x.Note.Title, StringComparer.OrdinalIgnoreCase).ToArray();
        });
    }
    public async Task<(AssetMetadata Metadata, byte[] Bytes)?> ReadAssetAsync(Guid id)
    {
        if (!File.Exists(AssetPath(id)) || !File.Exists(AssetPath(id) + ".json")) return null;
        return (JsonSerializer.Deserialize<AssetMetadata>(await File.ReadAllTextAsync(AssetPath(id) + ".json"), Json)!, await File.ReadAllBytesAsync(AssetPath(id)));
    }
    public async Task RemoveAsync(string key)
    {
        await gate.WaitAsync();
        try
        {
            var manifest = (await StatusAsync()).Manifest;
            manifest = manifest with { Selections = manifest.Selections.Where(x => x.Key != key).ToArray() }; await Save(manifest);
            var keepNotes = manifest.Selections.SelectMany(x => x.Notes).ToHashSet(); var keepAssets = new HashSet<Guid>();
            foreach (var id in keepNotes) if (await ReadAsync(id) is { } note) keepAssets.UnionWith(AssetReferences.Extract(note.Note.Path, note.Note.Markdown));
            // Only explicitly downloaded copies are removed; drafts, queue payloads and pending uploads are separate siblings.
            var notesDir = Path.Combine(root, "notes");
            if (Directory.Exists(notesDir)) foreach (var file in Directory.EnumerateFiles(notesDir, "*.json")) if (Guid.TryParse(Path.GetFileNameWithoutExtension(file), out var id) && !keepNotes.Contains(id)) File.Delete(file);
            var assetsDir = Path.Combine(root, "assets");
            if (Directory.Exists(assetsDir)) foreach (var file in Directory.EnumerateFiles(assetsDir, "*.bin")) if (Guid.TryParse(Path.GetFileNameWithoutExtension(file), out var id) && !keepAssets.Contains(id)) { File.Delete(file); if (File.Exists(file + ".json")) File.Delete(file + ".json"); }
        }
        finally { gate.Release(); }
    }
}

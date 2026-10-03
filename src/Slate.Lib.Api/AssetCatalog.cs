using System.Text.Json;
using Slate.Lib.Core;

namespace Slate.Lib.Api;

public sealed partial class AssetStore
{
    private readonly Lock catalogGate = new();
    private readonly Dictionary<Guid, AssetMetadata> catalog = [];
    private AssetMetadata[]? catalogOrder;
    private string cleanup = "";
    private sealed record Removal(AssetMetadata Metadata, bool Complete);
    private void InitializeCatalog(string root)
    {
        cleanup = Path.Combine(root, "cleanup"); Directory.CreateDirectory(cleanup);
        foreach (var file in Directory.EnumerateFiles(metadata, "*.json"))
        {
            if (!Guid.TryParse(Path.GetFileNameWithoutExtension(file), out var id)) continue;
            var record = ReadMetadataUnsafe(id)!;
            if (!File.Exists(ObjectPath(id, record.Extension)))
            {
                if (File.Exists(Path.Combine(cleanup, id + ".json"))) continue;
                throw new InvalidDataException("An attachment object is missing. Restore it before running cleanup.");
            }
            catalog[id] = record;
        }
    }
    public AssetPage List(int page, Func<Guid, int> referenceCount, bool unreferenced = false)
    {
        if (page is < 0 or > 1999) throw new ArgumentException("Invalid attachment page.");
        lock (catalogGate)
        {
            catalogOrder ??= catalog.Values.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).ToArray();
            var selected = unreferenced ? catalogOrder.Where(x => referenceCount(x.Id) == 0).ToArray() : catalogOrder;
            return new(page, selected.Length, selected.Sum(x => x.ByteSize), selected.Skip(page * 20).Take(20).Select(x => new AssetListing(x, referenceCount(x.Id))).ToArray());
        }
    }
    public bool IsRemoved(Guid id)
    {
        var receipt = Path.Combine(cleanup, id + ".json"); if (!File.Exists(receipt)) return false;
        var record = JsonSerializer.Deserialize<Removal>(File.ReadAllText(receipt), Json) ?? throw new InvalidDataException("Invalid cleanup receipt.");
        return !File.Exists(ObjectPath(id, record.Metadata.Extension));
    }
    public AssetCleanupResult RemoveUnreferenced(Guid id, string expectedHash)
    {
        gate.Wait();
        try
        {
            if (IsRemoved(id)) return new(id, 0);
            var asset = Open(id); if (asset.Metadata.Sha256 != expectedHash) throw new LibraryPreconditionException("The attachment changed.");
            var receipt = Path.Combine(cleanup, id + ".json");
            AtomicJson.WriteAsync(receipt, new Removal(asset.Metadata, false), Json).GetAwaiter().GetResult();
            var bytes = new FileInfo(asset.Path).Length;
            File.Delete(asset.Path); // Only called after explicit preview and a full canonical-reference audit under the writer lock.
            lock (catalogGate) { catalog.Remove(id); catalogOrder = null; }
            AtomicJson.WriteAsync(receipt, new Removal(asset.Metadata, true), Json).GetAwaiter().GetResult();
            return new(id, bytes);
        }
        finally { gate.Release(); }
    }
}

public sealed partial class LibraryStore
{
    private readonly Dictionary<Guid, (string Signature, HashSet<Guid> Assets)> assetNoteIndex = [];
    private readonly Dictionary<Guid, Dictionary<Guid, AssetReference>> assetReferences = [];
    private void IndexAssetReferences(string path, string source, NoteDocument document)
    {
        var id = document.Id!.Value;
        var signature = path + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)));
        if (assetNoteIndex.TryGetValue(id, out var old) && old.Signature == signature) return;
        foreach (var asset in old.Assets ?? []) if (assetReferences.TryGetValue(asset, out var refs)) refs.Remove(id);
        var ids = AssetReferences.Extract(path, source).ToHashSet(); assetNoteIndex[id] = (signature, ids);
        foreach (var asset in ids)
        {
            if (!assetReferences.TryGetValue(asset, out var refs)) assetReferences[asset] = refs = [];
            refs[id] = new(id, document.Title, path);
        }
    }
    public AssetPage ListAssets(int page, bool unreferenced)
    {
        lock (gate) return (assets ?? throw new InvalidOperationException("Attachments are unavailable.")).List(page, id => assetReferences.GetValueOrDefault(id)?.Count ?? 0, unreferenced);
    }
    public AssetReferencePage AssetNotes(Guid id, int page)
    {
        if (page is < 0 or > 1999) throw new ArgumentException("Invalid page.");
        lock (gate)
        {
            var refs = assetReferences.GetValueOrDefault(id)?.Values.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
            return new(page, refs.Length, refs.Skip(page * 20).Take(20).ToArray());
        }
    }
    private void AuditAssetReferences(Guid id)
    {
        // Cleanup is rare and intentionally checks every canonical file, including externally changed files.
        // Any parse/path/read failure aborts; a stale or failed derived index never authorizes deletion.
        foreach (var path in Discover(paths))
        {
            var source = ReadFile(path); NoteDocument.Parse(source, path);
            if (AssetReferences.Extract(path, source).Contains(id)) throw new LibraryConflictException("This attachment is still referenced by a note.");
        }
    }
    public AssetCleanupPreview PreviewAssetCleanup(Guid id)
    {
        lock (gate)
        {
            var asset = (assets ?? throw new InvalidOperationException("Attachments are unavailable.")).Open(id); AuditAssetReferences(id);
            if (asset.Metadata.CreatedAt > DateTimeOffset.UtcNow.AddHours(-24)) throw new LibraryConflictException("Keep newly uploaded attachments for at least 24 hours so pending notes can finish saving.");
            return new(id, asset.Metadata.Sha256, asset.Metadata.ByteSize, LibraryVersion,
                "No current note references this attachment. Permanent removal may affect older history or pending work on another device. The binary cannot be restored from Markdown Git history.");
        }
    }
    public AssetCleanupResult CleanupAsset(Guid id, AssetCleanupRequest request)
    {
        lock (gate)
        {
            if (assets?.IsRemoved(id) == true) return new(id, 0);
            if (request.LibraryVersion != LibraryVersion) throw new LibraryPreconditionException("The library changed. Preview cleanup again.");
            var preview = PreviewAssetCleanup(id);
            if (preview.Sha256 != request.Sha256) throw new LibraryPreconditionException("The attachment changed.");
            return assets!.RemoveUnreferenced(id, request.Sha256);
        }
    }
}

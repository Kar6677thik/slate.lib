using System.Security.Cryptography;
using System.Text.Json;
using Slate.Lib.Core;

namespace Slate.Lib.Api;

public sealed partial class LibraryStore
{
    private sealed record BulkFile(string Source, string? Target, string Hash, string? OutputHash);
    private sealed record BulkRecord(int Schema, BulkPreview Preview, IReadOnlyList<BulkFile> Files, IReadOnlyDictionary<string, string> Roots, string State, bool RepairIncoming = false);
    private static readonly JsonSerializerOptions BulkJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private string OperationRoot => git?.OperationStatePath ?? Path.Combine(paths.Root, ".slate", "operations");
    private string OperationDirectory(Guid id) => Path.Combine(OperationRoot, id.ToString("D"));

    public BulkPreview PreviewBulk(BulkOperationRequest request)
    {
        lock (gate)
        {
            if (request.OperationId == Guid.Empty || request.Operation is not ("move" or "copy" or "duplicate" or "delete" or "rename") || request.Paths.Count is < 1 or > 500)
                throw new ArgumentException("Select between one and 500 items and a supported operation.");
            if (request.Operation == "rename" && (request.Paths.Count != 1 || string.IsNullOrWhiteSpace(request.NewName))) throw new ArgumentException("Rename one item at a time with a new name.");
            var directory = OperationDirectory(request.OperationId);
            if (Directory.Exists(directory)) throw new LibraryConflictException("That operation ID has already been used.");
            var requested = request.Paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var path in requested) _ = ExistingItem(path, out _);
            // Parent selection subsumes descendants; it must never operate on a descendant twice.
            var roots = requested.Where(path => !requested.Any(parent => parent != path && path.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase))).ToArray();
            if (request.Operation is "move" or "copy")
            {
                if (!Directory.Exists(paths.Resolve(request.DestinationFolderPath, true))) throw new DirectoryNotFoundException();
            }
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rootHashes = roots.ToDictionary(p => p, p => BulkTreeHash(paths.Resolve(p)), StringComparer.OrdinalIgnoreCase);
            var items = new List<BulkItem>(); var files = new List<BulkFile>();
            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var idMap = new Dictionary<Guid, Guid>(); var sources = new Dictionary<string, string>();
            var snapshots = new List<(string Path, string Full, int Root)>();
            long total = 0;
            for (var i = 0; i < roots.Length; i++)
            {
                var source = ExistingItem(roots[i], out var folder);
                var destinationFolder = request.Operation is "duplicate" or "rename" ? Parent(roots[i]) : request.DestinationFolderPath;
                if (folder && request.Operation != "delete" && IsSameOrDescendant(destinationFolder, roots[i]))
                    throw new ArgumentException("A folder cannot be dropped into itself or a descendant.");
                string? target = request.Operation == "delete" ? null : Join(destinationFolder, Path.GetFileName(roots[i]));
                if (request.Operation == "rename")
                {
                    var name = request.NewName!.Trim(); if (!folder && !name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) name += ".md";
                    paths.ValidateName(name); target = Join(destinationFolder, name);
                    if (target == roots[i]) throw new ArgumentException("Choose a different name.");
                }
                if (request.Operation is "copy" or "duplicate")
                {
                    if (request.Operation == "duplicate" || File.Exists(paths.Resolve(target!)) || Directory.Exists(paths.Resolve(target!)))
                        target = Join(destinationFolder, AvailableBulkCopyName(destinationFolder, Path.GetFileName(roots[i]), folder, targets));
                }
                if (target is not null)
                {
                    var full = paths.Resolve(target); if (!(request.Operation == "rename" && target.Equals(roots[i], StringComparison.OrdinalIgnoreCase))) EnsureAvailable(full);
                    if (!targets.Add(target)) throw new LibraryConflictException("Selected items have colliding destination names.");
                }
                items.Add(new(roots[i], target, folder));
                var fullFiles = folder ? EnumerateBulkFiles(source).ToArray() : [source];
                foreach (var file in fullFiles)
                {
                    var relative = Path.GetRelativePath(paths.Root, file).Replace('\\', '/');
                    var bytes = new FileInfo(file).Length; total += bytes;
                    if (total > 64L * 1024 * 1024 || snapshots.Count >= 5000) throw new ArgumentException("Split this selection into batches of at most 5,000 files and 64 MiB.");
                    var targetFile = target is null ? null : target + (relative == roots[i] ? "" : relative[roots[i].Length..]);
                    if (targetFile is not null) { if (Path.GetFileName(relative) != ".gitkeep") paths.Resolve(targetFile); pathMap.Add(relative, targetFile); }
                    if (relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    {
                        var text = ReadFile(relative); var parsed = NoteDocument.Parse(text, relative); sources.Add(relative, text);
                        if (request.Operation is "copy" or "duplicate") idMap.Add(parsed.Id!.Value, Guid.NewGuid());
                    }
                    snapshots.Add((relative, file, i));
                }
            }
            var linkResolver = links;
            if (linkResolver is null) { linkResolver = new LinkIndex(); linkResolver.Reconcile(AllNotes()); }
            Directory.CreateDirectory(directory);
            for (var i = 0; i < items.Count; i++)
            {
                Directory.CreateDirectory(Path.Combine(directory, "original", i.ToString()));
                if (items[i].DestinationPath is not null && items[i].IsDirectory) Directory.CreateDirectory(Path.Combine(directory, "payload", i.ToString()));
                if (items[i].IsDirectory && items[i].DestinationPath is not null)
                    foreach (var child in Directory.EnumerateDirectories(paths.Resolve(items[i].SourcePath), "*", SearchOption.AllDirectories))
                    { LibraryPaths.RejectLink(child); Directory.CreateDirectory(Path.Combine(directory, "payload", i.ToString(), Path.GetRelativePath(paths.Resolve(items[i].SourcePath), child))); }
            }
            foreach (var snapshot in snapshots)
            {
                var item = items[snapshot.Root];
                var hash = FileHash(snapshot.Full); string? outputHash = null;
                if (item.DestinationPath is not null)
                {
                    var suffix = item.IsDirectory ? snapshot.Path[(item.SourcePath.Length + 1)..] : "";
                    var destination = Path.Combine(directory, "payload", snapshot.Root.ToString(), suffix);
                    if (!item.IsDirectory) destination = Path.Combine(directory, "payload", snapshot.Root.ToString());
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (sources.TryGetValue(snapshot.Path, out var markdown))
                    {
                        var oldId = NoteDocument.Parse(markdown, snapshot.Path).Id!.Value;
                        var changed = PortableNoteLinks.Rewrite(markdown, snapshot.Path, pathMap[snapshot.Path], pathMap,
                            idMap.Count == 0 ? null : idMap, linkResolver.Read(oldId).Outgoing);
                        if (idMap.TryGetValue(oldId, out var newId)) changed = NoteDocument.ReplaceId(changed, newId);
                        if (request.Operation == "rename" && !item.IsDirectory) changed = NoteDocument.AddAlias(changed, Path.GetFileNameWithoutExtension(snapshot.Path));
                        NoteDocument.Parse(changed, pathMap[snapshot.Path]); WriteNew(destination, changed);
                    }
                    else File.Copy(snapshot.Full, destination, false);
                    outputHash = FileHash(destination);
                }
                files.Add(new(snapshot.Path, pathMap.GetValueOrDefault(snapshot.Path), hash, outputHash));
            }
            var repairs = request.Operation is "move" or "rename" ? IncomingRepairs(pathMap, linkResolver) : [];
            for (var i = 0; i < repairs.Count; i++)
            {
                var payload = Path.Combine(directory, "link-payload", i.ToString()); Directory.CreateDirectory(Path.GetDirectoryName(payload)!); WriteNew(payload, repairs[i].ProposedMarkdown);
            }
            var fingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { items, files, repairs }, BulkJson)));
            var preview = new BulkPreview(request.OperationId, request.Operation, items, sources.Count, fingerprint, repairs);
            foreach (var root in roots) if (BulkTreeHash(paths.Resolve(root)) != rootHashes[root]) throw new LibraryPreconditionException("Content changed while the preview was prepared.");
            SaveBulk(directory, new(1, preview, files, rootHashes, "prepared"));
            return preview;
        }
    }

    public BulkOperationResult ApplyBulk(BulkApplyRequest request)
    {
        // Git gate is acquired before the library writer, following existing lock ordering.
        git?.FlushAsync(this).GetAwaiter().GetResult();
        lock (gate)
        {
            var directory = OperationDirectory(request.OperationId); var record = ReadBulk(directory);
            if (record.Preview.Fingerprint != request.Fingerprint) throw new LibraryPreconditionException("The operation preview changed.");
            if (record.State == "committed") return new(request.OperationId, "completed", record.Preview.Items, record.Preview.NoteCount);
            if (record.State != "prepared") throw new LibraryConflictException("This operation is no longer pending. Preview the selection again.");
            foreach (var file in record.Files)
                if (!File.Exists(BulkResolve(file.Source)) || FileHash(BulkResolve(file.Source)) != file.Hash)
                    throw new LibraryPreconditionException("An item changed since the preview. Preview the selection again.");
            foreach (var item in record.Preview.Items)
            {
                if (BulkTreeHash(paths.Resolve(item.SourcePath)) != record.Roots[item.SourcePath]) throw new LibraryPreconditionException("The folder changed since preview.");
                if (item.DestinationPath is not null && !(record.Preview.Operation == "rename" && item.DestinationPath.Equals(item.SourcePath, StringComparison.OrdinalIgnoreCase))) EnsureAvailable(paths.Resolve(item.DestinationPath));
                if (item.IsDirectory)
                {
                    var actual = EnumerateBulkFiles(paths.Resolve(item.SourcePath)).Select(f => Path.GetRelativePath(paths.Root, f).Replace('\\', '/')).Order().ToArray();
                    var expected = record.Files.Where(f => f.Source.StartsWith(item.SourcePath + "/", StringComparison.OrdinalIgnoreCase)).Select(f => f.Source).Order().ToArray();
                    if (!actual.SequenceEqual(expected)) throw new LibraryPreconditionException("The folder contents changed since the preview.");
                }
            }
            if (request.RepairIncoming)
                foreach (var repair in record.Preview.Repairs ?? []) if (Read(repair.Id).Revision != repair.Revision) throw new LibraryPreconditionException("An incoming-link source changed. Preview this move again.");
            record = record with { RepairIncoming = request.RepairIncoming, State = "applying" };
            SaveBulk(directory, record);
            try
            {
                for (var i = 0; i < record.Preview.Items.Count; i++)
                {
                    var item = record.Preview.Items[i];
                    if (record.Preview.Operation is "move" or "rename" or "delete")
                        BulkMove(paths.Resolve(item.SourcePath), Path.Combine(directory, "held", i.ToString()), item.IsDirectory);
                    if (item.DestinationPath is not null)
                        BulkMove(Path.Combine(directory, "payload", i.ToString()), paths.Resolve(item.DestinationPath), item.IsDirectory);
                }
                if (record.RepairIncoming)
                    for (var i = 0; i < (record.Preview.Repairs?.Count ?? 0); i++)
                    {
                        var repair = record.Preview.Repairs![i];
                        BulkMove(paths.Resolve(repair.Path), Path.Combine(directory, "link-held", i.ToString()), false);
                        BulkMove(Path.Combine(directory, "link-payload", i.ToString()), paths.Resolve(repair.Path), false);
                    }
                SaveBulk(directory, record with { State = "committed" });
            }
            catch
            {
                RollbackBulk(directory, record); throw;
            }
            RebuildIndex(); StructuralChange();
            return new(request.OperationId, "completed", record.Preview.Items, record.Preview.NoteCount);
        }
    }

    public BulkOperationResult BulkStatus(Guid operationId)
    {
        lock (gate)
        {
            var record = ReadBulk(OperationDirectory(operationId));
            return new(operationId, record.State == "committed" ? "completed" : record.State, record.Preview.Items, record.Preview.NoteCount);
        }
    }

    private string BulkResolve(string relative)
    {
        if (Path.GetFileName(relative) != ".gitkeep") return paths.Resolve(relative);
        var parent = paths.Resolve(Parent(relative), true); var marker = Path.Combine(parent, ".gitkeep"); LibraryPaths.RejectLink(marker); return marker;
    }
    private IEnumerable<string> EnumerateBulkFiles(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            LibraryPaths.RejectLink(entry);
            if (Directory.Exists(entry))
            { if (Path.GetFileName(entry).StartsWith('.')) throw new LibraryConflictException("This folder contains hidden content; review it outside Slate."); foreach (var file in EnumerateBulkFiles(entry)) yield return file; }
            else
            {
                if (Path.GetFileName(entry) != ".gitkeep" && !entry.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    throw new LibraryConflictException("This folder contains unsupported files; review them outside Slate.");
                yield return entry;
            }
        }
    }
    private string AvailableBulkCopyName(string folder, string name, bool directory, HashSet<string> reserved)
    {
        var fullFolder = paths.Resolve(folder, true); var extension = directory ? "" : Path.GetExtension(name); var stem = directory ? name : Path.GetFileNameWithoutExtension(name);
        for (var i = 1; i < 10000; i++)
        {
            var candidate = stem + (i == 1 ? " copy" : $" copy {i}") + extension;
            var relative = Join(folder, candidate);
            if (reserved.Contains(relative) || Directory.EnumerateFileSystemEntries(fullFolder).Any(p => Path.GetFileName(p).Equals(candidate, StringComparison.OrdinalIgnoreCase))) continue;
            paths.Resolve(relative); return candidate;
        }
        throw new LibraryConflictException("No available copy name.");
    }
    private static string FileHash(string full) { using var stream = File.OpenRead(full); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static string BulkTreeHash(string full)
    {
        LibraryPaths.RejectLink(full);
        if (File.Exists(full))
        {
            if (new FileInfo(full).Length > 2L * 1024 * 1024) throw new ArgumentException("A selected note exceeds the managed note size limit.");
            return FileHash(full);
        }
        if (!Directory.Exists(full)) throw new FileNotFoundException();
        var inventory = new List<string>(); var pending = new Stack<string>(); pending.Push(full); long bytes = 0;
        while (pending.TryPop(out var folder))
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                // Check each entry before descending: never enumerate through a junction/link.
                LibraryPaths.RejectLink(entry);
                if (inventory.Count >= 10000) throw new ArgumentException("Split this selection into smaller batches.");
                var relative = Path.GetRelativePath(full, entry);
                if (Directory.Exists(entry)) { pending.Push(entry); inventory.Add(relative + ":directory"); }
                else
                {
                    bytes += new FileInfo(entry).Length;
                    if (bytes > 64L * 1024 * 1024 || new FileInfo(entry).Length > 2L * 1024 * 1024) throw new ArgumentException("Split this selection into smaller batches within the note size limit.");
                    inventory.Add(relative + ":" + FileHash(entry));
                }
            }
        return Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', inventory.Order(StringComparer.Ordinal)))));
    }
    private static void BulkMove(string source, string destination, bool directory)
    { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); if (directory) Directory.Move(source, destination); else File.Move(source, destination, false); }
    private static void SaveBulk(string directory, BulkRecord record) => AtomicJson.WriteAsync(Path.Combine(directory, "plan.json"), record, BulkJson).GetAwaiter().GetResult();
    private static BulkRecord ReadBulk(string directory)
    {
        var record = JsonSerializer.Deserialize<BulkRecord>(File.ReadAllText(Path.Combine(directory, "plan.json")), BulkJson);
        return record?.Schema == 1 ? record : throw new InvalidDataException("Unsupported operation journal.");
    }
    private void RecoverBulkOperations()
    {
        LibraryPaths.RejectLink(OperationRoot);
        if (!Directory.Exists(OperationRoot)) return;
        foreach (var directory in Directory.EnumerateDirectories(OperationRoot))
        {
            LibraryPaths.RejectLink(directory);
            if (!Guid.TryParse(Path.GetFileName(directory), out _) || !File.Exists(Path.Combine(directory, "plan.json"))) continue;
            var record = ReadBulk(directory); if (record.State == "applying") RollbackBulk(directory, record);
        }
    }
    private void RollbackBulk(string directory, BulkRecord record)
    {
        if (record.RepairIncoming)
            for (var i = (record.Preview.Repairs?.Count ?? 0) - 1; i >= 0; i--)
            {
                var repair = record.Preview.Repairs![i]; var held = Path.Combine(directory, "link-held", i.ToString());
                var payload = Path.Combine(directory, "link-payload", i.ToString()); var current = paths.Resolve(repair.Path);
                if (!File.Exists(held)) continue;
                if (!File.Exists(payload))
                {
                    var expected = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(repair.ProposedMarkdown)));
                    if (!File.Exists(current) || FileHash(current) != expected) throw new InvalidOperationException("An interrupted link repair needs review. Both versions remain preserved.");
                    BulkMove(current, payload, false);
                }
                EnsureAvailable(current); BulkMove(held, current, false);
            }
        for (var i = record.Preview.Items.Count - 1; i >= 0; i--)
        {
            var item = record.Preview.Items[i]; var payload = Path.Combine(directory, "payload", i.ToString());
            var held = Path.Combine(directory, "held", i.ToString());
            if (item.DestinationPath is not null && !File.Exists(payload) && !Directory.Exists(payload))
            {
                foreach (var file in record.Files.Where(f => f.Target == item.DestinationPath || f.Target?.StartsWith(item.DestinationPath + "/", StringComparison.OrdinalIgnoreCase) == true))
                    if (!File.Exists(BulkResolve(file.Target!)) || FileHash(BulkResolve(file.Target!)) != file.OutputHash)
                        throw new InvalidOperationException("An interrupted operation needs review; both original and destination data have been preserved.");
                BulkMove(paths.Resolve(item.DestinationPath), payload, item.IsDirectory);
            }
            if (File.Exists(held) || Directory.Exists(held))
            {
                EnsureAvailable(paths.Resolve(item.SourcePath)); BulkMove(held, paths.Resolve(item.SourcePath), item.IsDirectory);
            }
        }
        SaveBulk(directory, record with { State = "rolled-back" });
    }
}

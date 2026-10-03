using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace Slate.Lib.Core;

public sealed record QueuedUpload(PendingAsset Asset, AssetMetadata? Uploaded = null);
public sealed record OfflineOperation(int Schema, Guid Id, Guid DraftId, ReplayNoteRequest Request, string TargetPath,
    IReadOnlyList<QueuedUpload> Uploads, string State, int Attempts, DateTimeOffset CreatedAt, string? LastError = null,
    ReplayNoteRequest? ReadyRequest = null, LibraryNote? Result = null, bool Acknowledged = false);

/// <summary>One atomic durable record contains a note operation and its upload dependencies. Drafts remain independent.</summary>
public sealed class OfflineQueue
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string root;
    private readonly SemaphoreSlim gate;
    public OfflineQueue(string directory) { root = Path.GetFullPath(directory); gate = Gates.GetOrAdd(root, _ => new(1, 1)); }
    private string PathFor(Guid id) => Path.Combine(root, id + ".json");
    private Task Write(OfflineOperation operation) => AtomicJson.WriteAsync(PathFor(operation.Id), operation, Json);
    public async Task AcknowledgeAsync(Guid id)
    {
        await gate.WaitAsync();
        try { var record = (await ReadAsync()).Single(x => x.Id == id); if (record.State == "completed") await Write(record with { Acknowledged = true }); }
        finally { gate.Release(); }
    }
    public async Task ResolveConflictAsync(Guid noteId)
    {
        await gate.WaitAsync();
        try
        {
            foreach (var record in (await ReadAsync()).Where(x => x.Request.NoteId == noteId && x.State == "conflict")) await Write(record with { State = "cancelled", LastError = "Resolved explicitly in the merge workbench; original request retained." });
        }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<OfflineOperation>> ReadAsync()
    {
        var records = new List<OfflineOperation>();
        if (!Directory.Exists(root)) return records;
        foreach (var path in Directory.EnumerateFiles(root, "*.json"))
        {
            var value = JsonSerializer.Deserialize<OfflineOperation>(await File.ReadAllTextAsync(path), Json) ?? throw new InvalidDataException("Invalid offline queue record.");
            if (value.Schema != 1) throw new InvalidDataException("Unsupported offline queue version. Existing work was kept.");
            records.Add(value);
        }
        return records.OrderBy(x => x.CreatedAt).ToArray();
    }
    public async Task<OfflineOperation> EnqueueAsync(Guid draftId, ReplayNoteRequest request, string targetPath, IReadOnlyList<PendingAsset>? assets = null)
    {
        await gate.WaitAsync();
        try
        {
            var pending = (await ReadAsync()).FirstOrDefault(x => x.Request.NoteId == request.NoteId && x.State is not ("completed" or "cancelled"));
            if (pending is not null)
            {
                if (pending.Request with { OperationId = request.OperationId } == request) return pending;
                throw new InvalidOperationException("This note has a pending save. Retry it in Offline work before submitting another version; your newer draft is still kept.");
            }
            var operation = new OfflineOperation(1, request.OperationId, draftId, request, targetPath,
                (assets ?? []).DistinctBy(x => x.Id).Select(x => new QueuedUpload(x)).ToArray(), "pending", 0, DateTimeOffset.UtcNow);
            if (File.Exists(PathFor(operation.Id))) throw new InvalidOperationException("Operation identity already used.");
            await Write(operation); return operation;
        }
        finally { gate.Release(); }
    }
    public async Task CancelAsync(Guid id)
    {
        await gate.WaitAsync();
        try
        {
            var operation = (await ReadAsync()).Single(x => x.Id == id);
            if (operation.Attempts != 0) throw new InvalidOperationException("This save may have reached the server. Retry it to reconcile its receipt before changing it.");
            await Write(operation with { State = "cancelled" }); // Keep the draft and pending attachment bytes.
        }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<OfflineOperation>> ReplayAsync(LibraryApiClient api, Guid? only = null, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        var results = new List<OfflineOperation>();
        try
        {
            foreach (var original in (await ReadAsync()).Where(x => x.State is not ("completed" or "cancelled") && (only is null || x.Id == only)))
            {
                if (original.State == "conflict" && only is null) continue;
                var operation = original with { Attempts = original.Attempts + 1, State = "sending", LastError = null }; await Write(operation);
                try
                {
                    var uploads = operation.Uploads.ToArray();
                    for (var i = 0; i < uploads.Length; i++)
                    {
                        if (uploads[i].Uploaded is not null) continue;
                        uploads[i] = uploads[i] with { Uploaded = await PendingAssetStore.UploadAsync(api, uploads[i].Asset, token) };
                        operation = operation with { Uploads = uploads.ToArray() }; await Write(operation);
                    }
                    var ready = operation.ReadyRequest;
                    if (ready is null)
                    {
                        var markdown = operation.Request.Markdown;
                        foreach (var upload in uploads) markdown = markdown.Replace($"asset-pending://{upload.Asset.Id:D}", AssetReferences.PathForNote(operation.TargetPath, upload.Asset.Id, upload.Uploaded!.Extension), StringComparison.Ordinal);
                        ready = operation.Request with { Markdown = markdown };
                        if (ready.Create is not null) ready = ready with { Create = ready.Create with { InitialMarkdown = markdown } };
                        if (ready.Capture is not null) ready = ready with { Capture = ready.Capture with { AssetIds = (ready.Capture.AssetIds ?? []).Concat(uploads.Select(x => x.Asset.Id)).Distinct().ToArray() } };
                        operation = operation with { ReadyRequest = ready }; await Write(operation);
                    }
                    var note = await api.ReplayAsync(ready, token);
                    operation = operation with { State = "completed", Result = note }; await Write(operation);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    operation = operation with { State = exception is HttpRequestException { StatusCode: HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed } ? "conflict" : "failed", LastError = exception.Message };
                    await Write(operation);
                    if (token.IsCancellationRequested) throw;
                }
                results.Add(operation);
                if (operation.State == "failed") break; // Back off on network/dependency failure rather than hammering every queued note.
            }
            return results;
        }
        finally { gate.Release(); }
    }
}

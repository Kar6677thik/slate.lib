using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Slate.Lib.Core;

namespace Slate.Lib.Api;

public sealed partial class LibraryStore
{
    private sealed record ReplayReceipt(int Schema, string Hash, ReplayNoteRequest Request, LibraryNote? Result);
    public LibraryNote Replay(ReplayNoteRequest request)
    {
        lock (gate)
        {
            if (request.LibraryId is { } library && library != LibraryId) throw new LibraryConflictException("This queued work belongs to a different library.");
            if (request.OperationId == Guid.Empty || request.NoteId == Guid.Empty || request.Kind is not ("create" or "edit" or "capture")) throw new ArgumentException("Invalid queued operation.");
            if (Encoding.UTF8.GetByteCount(request.Markdown) > NoteDocument.MaxBytes) throw new InvalidDataException("Note exceeds 2 MiB.");
            var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, BulkJson)));
            var path = Path.Combine(OperationRoot, "replay", request.OperationId + ".json");
            var prior = File.Exists(path) ? JsonSerializer.Deserialize<ReplayReceipt>(File.ReadAllText(path), BulkJson) : null;
            if (prior is not null && (prior.Schema != 1 || prior.Hash != hash)) throw new LibraryConflictException("This operation identity was already used for different content.");
            if (prior?.Result is { } completed) return completed;
            if (prior is null && request.Kind == "create" && notes.ContainsKey(request.NoteId)) throw new LibraryConflictException("That note identity already exists.");
            if (prior is null) AtomicJson.WriteAsync(path, new ReplayReceipt(1, hash, request, null), BulkJson).GetAwaiter().GetResult();
            LibraryNote result;
            if (request.Kind == "edit")
            {
                var current = Read(request.NoteId);
                // A persisted preparation plus identical bytes safely recovers a lost acknowledgement.
                result = prior is not null && current.Markdown == request.Markdown ? current : Update(request.NoteId, new(request.Markdown, request.BaseRevision ?? ""));
            }
            else if (request.Kind == "capture")
            {
                if (request.Capture is null || request.Capture.CaptureId != request.NoteId) throw new ArgumentException("Invalid capture identity.");
                result = Capture(request.Capture);
            }
            else
            {
                if (request.Create is null || request.Create.Id != request.NoteId) throw new ArgumentException("Invalid creation identity.");
                if (prior is not null && notes.ContainsKey(request.NoteId))
                {
                    result = Read(request.NoteId);
                    var expected = PrepareInitialMarkdown(request.Markdown, request.NoteId, result.Path);
                    if (result.Markdown != expected) throw new LibraryConflictException("The previously created note changed. Inspect it before retrying.");
                }
                else result = CreateNote(request.Create with { InitialMarkdown = request.Markdown });
            }
            AtomicJson.WriteAsync(path, new ReplayReceipt(1, hash, request, result), BulkJson).GetAwaiter().GetResult();
            return result;
        }
    }
}

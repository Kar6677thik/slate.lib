using System.Text;
using System.Text.Json;
using Slate.Lib.Core;

namespace Slate.Lib.Api;

/// <summary>
/// Reader comments and questions, kept beside notes in <c>.slate/comments/{noteId}.json</c>. Files are keyed by the
/// stable note UUID, so threads follow a note through renames and moves, and they are committed and synchronized with
/// the Library like notes. The note's Markdown is never changed. Files are read on demand, so a Git import is picked
/// up without a cache to invalidate.
/// </summary>
public sealed class NoteCommentStore(LibraryPaths paths, LibraryStore library, GitSyncService? git = null)
{
    public const string RelativeDirectory = ".slate/comments";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly Lock gate = new();

    public static bool IsCommentPath(string relative) =>
        relative.StartsWith(RelativeDirectory + "/", StringComparison.Ordinal) &&
        relative.EndsWith(".json", StringComparison.Ordinal) &&
        relative.Count(c => c == '/') == 2 &&
        Guid.TryParseExact(relative[(RelativeDirectory.Length + 1)..^5], "D", out var id) && id != Guid.Empty &&
        relative[(RelativeDirectory.Length + 1)..^5] == id.ToString("D");

    /// <summary>Comment files present on disk, as library-relative paths, for Git staging.</summary>
    public static IEnumerable<string> Discover(string root)
    {
        var directory = Path.Combine(root, ".slate", "comments");
        if (!Directory.Exists(directory)) yield break;
        LibraryPaths.RejectLink(directory);
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            var relative = RelativeDirectory + "/" + Path.GetFileName(file);
            if (IsCommentPath(relative)) yield return relative;
        }
    }

    /// <summary>Parses and validates a comment file, as stored on disk or arriving in a Git import.</summary>
    public static NoteCommentFile Parse(string json, Guid expectedNoteId)
    {
        if (Encoding.UTF8.GetByteCount(json) > CommentLimits.MaxFileBytes) throw new InvalidDataException("A comment file exceeds 1 MiB.");
        NoteCommentFile? file;
        try { file = JsonSerializer.Deserialize<NoteCommentFile>(json, Json); }
        catch (JsonException exception) { throw new InvalidDataException("A comment file is not valid JSON.", exception); }
        if (file is null || file.SchemaVersion != CommentLimits.SchemaVersion || file.NoteId != expectedNoteId || file.Threads is null)
            throw new InvalidDataException("A comment file has an unsupported schema or the wrong note identity.");
        if (file.Threads.Count > CommentLimits.MaxThreadsPerNote) throw new InvalidDataException("A comment file has too many threads.");
        foreach (var thread in file.Threads)
        {
            if (thread is null || thread.Id == Guid.Empty || thread.Anchor is null || thread.Comments is null || thread.Comments.Count is 0 or > CommentLimits.MaxCommentsPerThread)
                throw new InvalidDataException("A comment thread is invalid.");
            ValidateAnchor(thread.Anchor);
            if (thread.Status is not (CommentThread.Open or CommentThread.Resolved)) throw new InvalidDataException("A comment thread has an unknown status.");
            foreach (var comment in thread.Comments)
            {
                if (comment is null || comment.Id == Guid.Empty || comment.Body is null || comment.Body.Length > CommentLimits.MaxBodyCharacters || comment.Author is null)
                    throw new InvalidDataException("A comment is invalid.");
                ValidateAuthor(comment.Author);
            }
        }
        return file;
    }

    public NoteCommentsView Read(Guid noteId)
    {
        var note = library.Read(noteId);
        lock (gate) return View(note, Load(noteId));
    }

    public CommentThreadView Create(Guid noteId, CreateCommentThreadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var anchor = NormalizeAnchor(request.Anchor ?? throw new ArgumentException("An anchor is required."));
        var body = Body(request.Body);
        var author = Author(request.Author);
        var note = library.Read(noteId);
        lock (gate)
        {
            var file = Load(noteId);
            if (file.Threads.Count >= CommentLimits.MaxThreadsPerNote) throw new LibraryConflictException("This note has reached its comment limit.");
            var now = DateTimeOffset.UtcNow;
            var thread = new CommentThread(Guid.NewGuid(), anchor, CommentThread.Open, now, now, null, [new(Guid.NewGuid(), author, body, now)]);
            Save(file with { Threads = [.. file.Threads, thread] });
            return View(note, thread);
        }
    }

    public CommentThreadView Reply(Guid noteId, Guid threadId, ReplyToCommentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = Body(request.Body, allowEmpty: request.LinkedNoteId is not null);
        var author = Author(request.Author);
        var note = library.Read(noteId);
        CommentLinkedNote? linked = null;
        if (request.LinkedNoteId is { } linkedId)
        {
            var target = library.Read(linkedId);
            linked = new(target.Id, target.Title, target.Path);
        }
        lock (gate)
        {
            var file = Load(noteId);
            var thread = Find(file, threadId);
            if (thread.Comments.Count >= CommentLimits.MaxCommentsPerThread) throw new LibraryConflictException("This thread has reached its reply limit.");
            var now = DateTimeOffset.UtcNow;
            var updated = thread with
            {
                Comments = [.. thread.Comments, new(Guid.NewGuid(), author, body, now, linked)],
                UpdatedAt = now,
                // The owner replying again reopens a resolved thread; an agent reply never resolves on its own.
                Status = author.Kind == CommentAuthor.User ? CommentThread.Open : thread.Status,
                ResolvedAt = author.Kind == CommentAuthor.User ? null : thread.ResolvedAt,
                Hidden = author.Kind == CommentAuthor.User ? false : thread.Hidden,
                HiddenAt = author.Kind == CommentAuthor.User ? null : thread.HiddenAt
            };
            Save(Replace(file, updated));
            return View(note, updated);
        }
    }

    public CommentThreadView SetStatus(Guid noteId, Guid threadId, CommentThreadStatusRequest request)
    {
        var status = request?.Status;
        if (status is not (CommentThread.Open or CommentThread.Resolved)) throw new ArgumentException("Status must be open or resolved.");
        var note = library.Read(noteId);
        lock (gate)
        {
            var file = Load(noteId);
            var thread = Find(file, threadId);
            var now = DateTimeOffset.UtcNow;
            var updated = thread with { Status = status, UpdatedAt = now, ResolvedAt = status == CommentThread.Resolved ? now : null };
            Save(Replace(file, updated));
            return View(note, updated);
        }
    }

    /// <summary>Hides or shows a thread. Hiding is the owner's choice; resolving a thread never hides it.</summary>
    public CommentThreadView SetHidden(Guid noteId, Guid threadId, CommentThreadVisibilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var note = library.Read(noteId);
        lock (gate)
        {
            var file = Load(noteId);
            var thread = Find(file, threadId);
            var updated = thread with { Hidden = request.Hidden, HiddenAt = request.Hidden ? DateTimeOffset.UtcNow : null };
            Save(Replace(file, updated));
            return View(note, updated);
        }
    }

    /// <summary>Deletes one comment. Deleting a thread's first comment deletes the whole thread.</summary>
    public void DeleteComment(Guid noteId, Guid threadId, Guid commentId)
    {
        library.Read(noteId);
        lock (gate)
        {
            var file = Load(noteId);
            var thread = Find(file, threadId);
            var index = thread.Comments.ToList().FindIndex(comment => comment.Id == commentId);
            if (index < 0) throw new FileNotFoundException("The comment does not exist.");
            if (index == 0) Save(file with { Threads = file.Threads.Where(item => item.Id != threadId).ToArray() });
            else Save(Replace(file, thread with { Comments = thread.Comments.Where(comment => comment.Id != commentId).ToArray(), UpdatedAt = DateTimeOffset.UtcNow }));
        }
    }

    /// <summary>Threads across the whole library, newest activity first, optionally only those waiting for an answer.</summary>
    public CommentInboxPage Inbox(string? status, bool awaitingReply, int page)
    {
        if (page < 0 || page > 10_000) throw new ArgumentException("Invalid page.");
        status = string.IsNullOrWhiteSpace(status) ? CommentThread.Open : status;
        if (status is not (CommentThread.Open or CommentThread.Resolved or "all")) throw new ArgumentException("Status must be open, resolved, or all.");
        var items = new List<CommentInboxItem>();
        lock (gate)
        {
            foreach (var relative in Discover(paths.Root))
            {
                var noteId = Guid.Parse(relative[(RelativeDirectory.Length + 1)..^5]);
                LibraryNote note;
                try { note = library.Read(noteId); }
                catch (FileNotFoundException) { continue; } // The note was deleted; its comments stay in history only.
                foreach (var thread in Load(noteId).Threads)
                {
                    var view = View(note, thread);
                    if (status != "all" && view.Status != status) continue;
                    if (awaitingReply && !view.AwaitingReply) continue;
                    items.Add(new(note.Id, note.Title, note.Path, view));
                }
            }
        }
        var ordered = items.OrderByDescending(item => item.Thread.UpdatedAt).ToArray();
        var offset = page * CommentLimits.InboxPageSize;
        return new(ordered.Skip(offset).Take(CommentLimits.InboxPageSize).ToArray(), ordered.Length,
            offset + CommentLimits.InboxPageSize < ordered.Length ? page + 1 : null);
    }

    private NoteCommentsView View(LibraryNote note, NoteCommentFile file) =>
        new(note.Id, note.Title, note.Path, note.Revision, file.Threads.Select(thread => View(note, thread)).ToArray());

    private CommentThreadView View(LibraryNote note, CommentThread thread)
    {
        var location = thread.Anchor.Kind == CommentAnchor.Text ? CommentAnchors.Locate(note.Markdown, thread.Anchor) : null;
        var state = thread.Anchor.Kind == CommentAnchor.Page ? "page" : location is null ? "detached" : "anchored";
        var comments = thread.Comments.Select(RefreshLink).ToArray();
        var awaiting = thread.Status == CommentThread.Open && !thread.Hidden && comments[^1].Author.Kind == CommentAuthor.User;
        return new(thread.Id, thread.Anchor, thread.Status, thread.CreatedAt, thread.UpdatedAt, thread.ResolvedAt, comments, state, location?.Line, location?.Context, awaiting, thread.Hidden);
    }

    /// <summary>Linked answer notes may have been renamed or moved since they were linked; show where they are now.</summary>
    private NoteComment RefreshLink(NoteComment comment)
    {
        if (comment.LinkedNote is not { } linked) return comment;
        try { var target = library.Read(linked.NoteId); return comment with { LinkedNote = new(target.Id, target.Title, target.Path) }; }
        catch (FileNotFoundException) { return comment; }
    }

    private static CommentThread Find(NoteCommentFile file, Guid threadId) =>
        file.Threads.FirstOrDefault(thread => thread.Id == threadId) ?? throw new FileNotFoundException("The comment thread does not exist.");

    private static NoteCommentFile Replace(NoteCommentFile file, CommentThread updated) =>
        file with { Threads = file.Threads.Select(thread => thread.Id == updated.Id ? updated : thread).ToArray() };

    private string FilePath(Guid noteId)
    {
        var directory = Path.Combine(paths.Root, ".slate", "comments");
        LibraryPaths.RejectLink(Path.Combine(paths.Root, ".slate"));
        LibraryPaths.RejectLink(directory);
        var file = Path.Combine(directory, noteId.ToString("D") + ".json");
        LibraryPaths.RejectLink(file);
        return file;
    }

    private NoteCommentFile Load(Guid noteId)
    {
        var file = FilePath(noteId);
        if (!File.Exists(file)) return new(CommentLimits.SchemaVersion, noteId, []);
        var bytes = File.ReadAllBytes(file);
        if (bytes.Length > CommentLimits.MaxFileBytes) throw new InvalidDataException("A comment file exceeds 1 MiB.");
        string json;
        try { json = Utf8.GetString(bytes); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("A comment file must be UTF-8.", exception); }
        return Parse(json, noteId);
    }

    private void Save(NoteCommentFile file)
    {
        var json = JsonSerializer.Serialize(file, Json) + "\n";
        if (Utf8.GetByteCount(json) > CommentLimits.MaxFileBytes) throw new LibraryConflictException("This note's comments exceed the storage limit.");
        var destination = FilePath(file.NoteId);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = Path.Combine(Path.GetDirectoryName(destination)!, ".slate-tmp-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(Utf8.GetBytes(json));
                stream.Flush(true);
            }
            File.Move(temp, destination, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        git?.MarkPending();
    }

    private static string Body(string? value, bool allowEmpty = false)
    {
        var body = (value ?? "").Trim().Replace("\r\n", "\n");
        if (body.Length == 0 && !allowEmpty) throw new ArgumentException("A comment needs text.");
        if (body.Length > CommentLimits.MaxBodyCharacters) throw new ArgumentException($"A comment is limited to {CommentLimits.MaxBodyCharacters} characters.");
        return body;
    }

    private static CommentAuthor Author(CommentAuthor? author)
    {
        if (author is null) return new(CommentAuthor.User, "You");
        var name = (author.Name ?? "").Trim();
        var normalized = new CommentAuthor(author.Kind, name.Length == 0 ? author.Kind == CommentAuthor.Agent ? "Assistant" : "You" : name);
        ValidateAuthor(normalized);
        return normalized;
    }

    private static void ValidateAuthor(CommentAuthor author)
    {
        if (author.Kind is not (CommentAuthor.User or CommentAuthor.Agent)) throw new ArgumentException("Author kind must be user or agent.");
        if (string.IsNullOrWhiteSpace(author.Name) || author.Name.Length > CommentLimits.MaxAuthorNameCharacters || author.Name.Any(char.IsControl))
            throw new ArgumentException("Author name is invalid.");
    }

    private static CommentAnchor NormalizeAnchor(CommentAnchor anchor)
    {
        if (anchor.Kind == CommentAnchor.Page) return new(CommentAnchor.Page);
        if (anchor.Kind != CommentAnchor.Text) throw new ArgumentException("Anchor kind must be page or text.");
        var exact = (anchor.Exact ?? "").Trim();
        if (exact.Length == 0) throw new ArgumentException("A text anchor needs the selected passage.");
        if (exact.Length > CommentLimits.MaxQuoteCharacters) throw new ArgumentException($"A selected passage is limited to {CommentLimits.MaxQuoteCharacters} characters.");
        var prefix = anchor.Prefix ?? ""; var suffix = anchor.Suffix ?? "";
        if (prefix.Length > CommentLimits.MaxContextCharacters) prefix = prefix[^CommentLimits.MaxContextCharacters..];
        if (suffix.Length > CommentLimits.MaxContextCharacters) suffix = suffix[..CommentLimits.MaxContextCharacters];
        return new(CommentAnchor.Text, exact, prefix, suffix);
    }

    private static void ValidateAnchor(CommentAnchor anchor)
    {
        if (anchor.Kind == CommentAnchor.Page) return;
        if (anchor.Kind != CommentAnchor.Text || string.IsNullOrWhiteSpace(anchor.Exact) || anchor.Exact.Length > CommentLimits.MaxQuoteCharacters ||
            (anchor.Prefix?.Length ?? 0) > CommentLimits.MaxContextCharacters || (anchor.Suffix?.Length ?? 0) > CommentLimits.MaxContextCharacters)
            throw new InvalidDataException("A comment anchor is invalid.");
    }
}

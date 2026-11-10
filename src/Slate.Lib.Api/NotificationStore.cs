using System.Text;
using System.Text.Json;
using Slate.Lib.Core;

namespace Slate.Lib.Api;

/// <summary>
/// The owner's activity feed: agent and other-device comments, and pages created elsewhere. It is server state, not
/// Library content, so it lives in the Git state directory beside operation journals and is never committed. Each
/// comment is its own notification; page creations from one actor are bundled so a bulk import or a burst of captures
/// shows up as one entry. The newest <see cref="MaxNotifications"/> entries are kept.
/// </summary>
public sealed class NotificationStore
{
    public const int MaxNotifications = 1_000;
    public const int MaxNotesPerNotification = 50;
    public const int PageSize = 30;
    public static readonly TimeSpan BundleWindow = TimeSpan.FromMinutes(10);
    private sealed record Document(int SchemaVersion, IReadOnlyList<SlateNotification> Notifications);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly string file;
    private readonly Lock gate = new();
    private readonly Func<DateTimeOffset> clock;

    public NotificationStore(string file, Func<DateTimeOffset>? clock = null)
    {
        this.file = Path.GetFullPath(file);
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public void CommentAdded(NotificationActor actor, NoteCommentsView note, CommentThreadView thread, NoteComment comment)
    {
        var excerpt = comment.Body.Length > 280 ? comment.Body[..280].TrimEnd() + "…" : comment.Body;
        var quote = thread.Anchor.Kind == CommentAnchor.Text ? thread.Anchor.Exact : null;
        if (quote is { Length: > 200 }) quote = quote[..200].TrimEnd() + "…";
        var payload = new CommentNotification(note.NoteId, note.Title, note.Path, thread.Id, comment.Id, thread.Anchor.Kind, quote, excerpt,
            thread.Comments.Count > 1 && thread.Comments[0].Id != comment.Id, comment.LinkedNote);
        var commentActor = new NotificationActor(comment.Author.Kind, comment.Author.Name, actor.DeviceId);
        lock (gate)
        {
            var items = Load().ToList();
            var key = "comment:" + comment.Id.ToString("D");
            if (items.Any(item => item.Key == key)) return;
            var now = clock();
            items.Insert(0, new(Guid.NewGuid(), SlateNotification.CommentKind, now, now, null, commentActor, Comment: payload, Key: key));
            Save(items);
        }
    }

    /// <summary>
    /// Records new pages. A <paramref name="key"/> (such as a bulk operation ID) makes the call idempotent and keeps it a
    /// single notification. Without a key, pages are merged into the newest unread bundle from the same actor and source
    /// when it was updated within <see cref="BundleWindow"/>. Pages already announced are skipped, so idempotent
    /// captures and repeated requests never duplicate an entry.
    /// </summary>
    public void NotesAdded(NotificationActor actor, string source, IEnumerable<LibraryNote> created, string? key = null)
    {
        lock (gate)
        {
            var items = Load().ToList();
            if (key is not null && items.Any(item => item.Key == key)) return;
            var announced = items.Where(item => item.Notes is not null).SelectMany(item => item.Notes!.Notes).Select(note => note.NoteId).ToHashSet();
            var notes = created.Where(note => announced.Add(note.Id)).Select(note => new NotificationNoteRef(note.Id, note.Title, note.Path)).ToList();
            if (notes.Count == 0) return;
            var now = clock();
            var bundle = key is null
                ? items.FirstOrDefault(item => item.Kind == SlateNotification.NotesKind && item.ReadAt is null && item.Key is null &&
                    item.Actor.DeviceId == actor.DeviceId && item.Actor.Name == actor.Name && item.Notes!.Source == source &&
                    now - item.UpdatedAt <= BundleWindow)
                : null;
            if (bundle is not null)
            {
                items.Remove(bundle);
                var merged = bundle.Notes!.Notes.Concat(notes).Take(MaxNotesPerNotification).ToArray();
                items.Insert(0, bundle with { UpdatedAt = now, Notes = new(source, bundle.Notes.Count + notes.Count, merged) });
            }
            else
            {
                items.Insert(0, new(Guid.NewGuid(), SlateNotification.NotesKind, now, now, null, actor,
                    Notes: new(source, notes.Count, notes.Take(MaxNotesPerNotification).ToArray()), Key: key));
            }
            Save(items);
        }
    }

    public NotificationPage List(Guid? requester, bool includeOwn, string? kind, int page)
    {
        if (page < 0 || page > 1_000) throw new ArgumentException("Invalid page.");
        if (kind is not (null or "" or SlateNotification.CommentKind or SlateNotification.NotesKind)) throw new ArgumentException("Kind must be comment or notes.");
        lock (gate)
        {
            var visible = Load().Where(item => includeOwn || !Own(item, requester)).ToArray();
            var unread = visible.Count(item => item.ReadAt is null);
            var filtered = string.IsNullOrEmpty(kind) ? visible : visible.Where(item => item.Kind == kind).ToArray();
            var offset = page * PageSize;
            return new(filtered.Skip(offset).Take(PageSize).Select(item => new NotificationItem(item, Own(item, requester), item.ReadAt is null)).ToArray(),
                unread, filtered.Length, offset + PageSize < filtered.Length ? page + 1 : null);
        }
    }

    public NotificationSummary Summary(Guid? requester)
    {
        lock (gate) return new(Load().Count(item => item.ReadAt is null && !Own(item, requester)));
    }

    public NotificationSummary MarkRead(Guid? requester, MarkNotificationsReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.All && (request.Ids is null || request.Ids.Count is 0 or > 200)) throw new ArgumentException("Name 1–200 notifications or mark all as read.");
        lock (gate)
        {
            var ids = request.Ids?.ToHashSet() ?? [];
            var now = clock();
            var items = Load().Select(item => item.ReadAt is null && (request.All || ids.Contains(item.Id)) ? item with { ReadAt = now } : item).ToList();
            Save(items);
            return new(items.Count(item => item.ReadAt is null && !Own(item, requester)));
        }
    }

    private static bool Own(SlateNotification item, Guid? requester) => requester is not null && item.Actor.DeviceId == requester;

    private IReadOnlyList<SlateNotification> Load()
    {
        if (!File.Exists(file)) return [];
        try
        {
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(file, Encoding.UTF8), Json);
            return document is { SchemaVersion: 1, Notifications: not null } ? document.Notifications : [];
        }
        catch (JsonException)
        {
            // The feed is a convenience; a damaged file must never take canonical operations down with it.
            return [];
        }
    }

    private void Save(List<SlateNotification> items)
    {
        var ordered = items.OrderByDescending(item => item.UpdatedAt).Take(MaxNotifications).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new Document(1, ordered), Json), new UTF8Encoding(false));
            File.Move(temp, file, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

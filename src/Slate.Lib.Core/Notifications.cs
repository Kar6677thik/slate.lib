namespace Slate.Lib.Core;

/// <summary>Who caused a notification: an agent comment author, or the device (and optional agent name) that made the change.</summary>
public sealed record NotificationActor(string Kind, string Name, Guid? DeviceId = null)
{
    public const string User = "user";
    public const string Agent = "agent";
    public const string Device = "device";
}

public sealed record NotificationNoteRef(Guid NoteId, string Title, string Path);

/// <summary>One comment: a reply to the owner's question, or a new thread.</summary>
public sealed record CommentNotification(
    Guid NoteId,
    string NoteTitle,
    string NotePath,
    Guid ThreadId,
    Guid CommentId,
    string AnchorKind,
    string? Quote,
    string Excerpt,
    bool IsReply,
    CommentLinkedNote? LinkedNote = null);

/// <summary>
/// Pages created by one actor. A bulk operation is always one notification; separate creations from the same actor
/// and source within a short window are merged, so hundreds of new pages never become hundreds of notifications.
/// </summary>
public sealed record NotesNotification(string Source, int Count, IReadOnlyList<NotificationNoteRef> Notes);

public sealed record SlateNotification(
    Guid Id,
    string Kind,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ReadAt,
    NotificationActor Actor,
    CommentNotification? Comment = null,
    NotesNotification? Notes = null,
    string? Key = null)
{
    public const string CommentKind = "comment";
    public const string NotesKind = "notes";
}

/// <summary>A notification as one device sees it: <c>FromThisDevice</c> marks the requester's own activity.</summary>
public sealed record NotificationItem(SlateNotification Notification, bool FromThisDevice, bool Unread);
public sealed record NotificationPage(IReadOnlyList<NotificationItem> Items, int Unread, int Total, int? NextPage);
public sealed record NotificationSummary(int Unread);
public sealed record MarkNotificationsReadRequest(IReadOnlyList<Guid>? Ids = null, bool All = false);

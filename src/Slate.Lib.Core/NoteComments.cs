using System.Text;

namespace Slate.Lib.Core;

/// <summary>Who wrote a comment: the library owner reading in a client, or an AI agent working through MCP.</summary>
public sealed record CommentAuthor(string Kind, string Name)
{
    public const string User = "user";
    public const string Agent = "agent";
}

/// <summary>
/// Where a thread is attached. <c>page</c> threads belong to the whole note. <c>text</c> threads use a W3C Web
/// Annotation TextQuoteSelector: the exact passage plus a little surrounding context, all in rendered reading text.
/// Quotes survive edits elsewhere in the note; no source offsets are stored because Markdown offsets shift.
/// </summary>
public sealed record CommentAnchor(string Kind, string? Exact = null, string? Prefix = null, string? Suffix = null)
{
    public const string Page = "page";
    public const string Text = "text";
}

/// <summary>A note created to answer a thread, snapshotted when linked and refreshed on read.</summary>
public sealed record CommentLinkedNote(Guid NoteId, string? Title = null, string? Path = null);

public sealed record NoteComment(Guid Id, CommentAuthor Author, string Body, DateTimeOffset CreatedAt, CommentLinkedNote? LinkedNote = null);

public sealed record CommentThread(
    Guid Id,
    CommentAnchor Anchor,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    IReadOnlyList<NoteComment> Comments,
    bool Hidden = false,
    DateTimeOffset? HiddenAt = null)
{
    public const string Open = "open";
    public const string Resolved = "resolved";
}

/// <summary>The persisted <c>.slate/comments/{noteId}.json</c> document.</summary>
public sealed record NoteCommentFile(int SchemaVersion, Guid NoteId, IReadOnlyList<CommentThread> Threads);

/// <summary>
/// A thread as clients see it: the stored thread plus where its passage currently sits in the note's Markdown.
/// <c>AnchorState</c> is <c>page</c>, <c>anchored</c> (the passage was found), or <c>detached</c> (the passage was
/// edited away; the original quote is still shown). <c>Line</c> is one-based and <c>Context</c> is that source line.
/// <c>AwaitingReply</c> is true for an open, visible thread whose latest comment is the owner's. Resolving never
/// hides a thread; only the owner's explicit <c>Hidden</c> flag does.
/// </summary>
public sealed record CommentThreadView(
    Guid Id,
    CommentAnchor Anchor,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ResolvedAt,
    IReadOnlyList<NoteComment> Comments,
    string AnchorState,
    int? Line,
    string? Context,
    bool AwaitingReply,
    bool Hidden = false);

public sealed record NoteCommentsView(Guid NoteId, string Title, string Path, string Revision, IReadOnlyList<CommentThreadView> Threads);

public sealed record CommentInboxItem(Guid NoteId, string NoteTitle, string NotePath, CommentThreadView Thread);
public sealed record CommentInboxPage(IReadOnlyList<CommentInboxItem> Items, int Total, int? NextPage);

public sealed record CreateCommentThreadRequest(CommentAnchor Anchor, string Body, CommentAuthor? Author = null);
public sealed record ReplyToCommentRequest(string Body, CommentAuthor? Author = null, Guid? LinkedNoteId = null);
public sealed record CommentThreadStatusRequest(string Status);
public sealed record CommentThreadVisibilityRequest(bool Hidden);

public static class CommentLimits
{
    public const int SchemaVersion = 1;
    public const int MaxFileBytes = 1024 * 1024;
    public const int MaxBodyCharacters = 10_000;
    public const int MaxQuoteCharacters = 1_000;
    public const int MaxContextCharacters = 64;
    public const int MaxAuthorNameCharacters = 60;
    public const int MaxThreadsPerNote = 500;
    public const int MaxCommentsPerThread = 200;
    public const int InboxPageSize = 50;
}

/// <summary>
/// Locates a TextQuoteSelector in a note's Markdown source. The quote was captured from rendered text, so an exact
/// search is tried first and then a search over a projection of the source with Markdown punctuation removed and
/// whitespace collapsed. Several matches are ranked by how well the stored prefix and suffix agree.
/// </summary>
public static class CommentAnchors
{
    public sealed record Location(int Start, int End, int Line, string Context);

    public static Location? Locate(string markdown, CommentAnchor anchor)
    {
        if (anchor.Kind != CommentAnchor.Text || string.IsNullOrWhiteSpace(anchor.Exact)) return null;
        var exact = anchor.Exact;
        var best = Best(markdown, exact, anchor.Prefix ?? "", anchor.Suffix ?? "");
        if (best is { } direct) return At(markdown, direct, direct + exact.Length);

        var (projection, map) = Project(markdown);
        var needle = Collapse(StripMarkup(exact));
        if (needle.Length == 0) return null;
        var projected = Best(projection, needle, Collapse(StripMarkup(anchor.Prefix ?? "")), Collapse(StripMarkup(anchor.Suffix ?? "")));
        if (projected is not { } start) return null;
        return At(markdown, map[start], map[start + needle.Length - 1] + 1);
    }

    private static int? Best(string haystack, string needle, string prefix, string suffix)
    {
        int? best = null;
        var bestScore = -1;
        for (var index = haystack.IndexOf(needle, StringComparison.Ordinal); index >= 0; index = haystack.IndexOf(needle, index + 1, StringComparison.Ordinal))
        {
            var score = Agreement(haystack[..index], prefix, fromEnd: true) + Agreement(haystack[(index + needle.Length)..], suffix, fromEnd: false);
            if (score > bestScore) { bestScore = score; best = index; }
        }
        return best;
    }

    /// <summary>Counts matching characters between context and the text next to a candidate, ignoring whitespace runs.</summary>
    private static int Agreement(string side, string context, bool fromEnd)
    {
        var a = Collapse(fromEnd ? Tail(side, 200) : Head(side, 200));
        var b = Collapse(context);
        var count = 0;
        if (fromEnd) for (int i = a.Length - 1, j = b.Length - 1; i >= 0 && j >= 0 && a[i] == b[j]; i--, j--) count++;
        else for (var i = 0; i < a.Length && i < b.Length && a[i] == b[i]; i++) count++;
        return count;
    }

    private static string Tail(string value, int length) => value.Length <= length ? value : value[^length..];
    private static string Head(string value, int length) => value.Length <= length ? value : value[..length];

    private static bool IsMarkup(char c) => c is '*' or '_' or '`' or '~' or '#' or '>' or '[' or ']' or '|' or '\\';

    private static string StripMarkup(string value) => new(value.Where(c => !IsMarkup(c)).ToArray());

    private static string Collapse(string value)
    {
        var builder = new StringBuilder(value.Length);
        var space = false;
        foreach (var c in value.Trim())
        {
            if (char.IsWhiteSpace(c)) { if (!space) builder.Append(' '); space = true; }
            else { builder.Append(c); space = false; }
        }
        return builder.ToString();
    }

    /// <summary>The source without Markdown punctuation and with collapsed whitespace, plus each character's source offset.</summary>
    private static (string Text, int[] Map) Project(string source)
    {
        var builder = new StringBuilder(source.Length);
        var map = new List<int>(source.Length);
        var space = true;
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (IsMarkup(c)) continue;
            if (char.IsWhiteSpace(c))
            {
                if (space) continue;
                builder.Append(' '); map.Add(i); space = true;
            }
            else { builder.Append(c); map.Add(i); space = false; }
        }
        return (builder.ToString(), map.ToArray());
    }

    private static Location At(string markdown, int start, int end)
    {
        var line = 1;
        for (var i = 0; i < start; i++) if (markdown[i] == '\n') line++;
        var lineStart = markdown.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        if (start == 0) lineStart = 0;
        var lineEnd = markdown.IndexOf('\n', start);
        if (lineEnd < 0) lineEnd = markdown.Length;
        var context = markdown[lineStart..lineEnd].TrimEnd('\r');
        if (context.Length > 500) context = context[..500];
        return new(start, end, line, context);
    }
}

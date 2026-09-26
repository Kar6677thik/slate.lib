namespace Slate.Lib.Core;

public sealed record LibraryEntry(string Name, string Path, bool IsDirectory, Guid? Id, string? Title);
public sealed record FolderPage(string Path, IReadOnlyList<LibraryEntry> Entries, int? NextPage);
public sealed record LibraryNote(Guid Id, string Path, string Title, string Markdown, string Revision);
public sealed record LibraryStatus(
    Guid LibraryId,
    int NoteCount,
    string? LibraryVersion = null,
    long SearchVersion = 0,
    string IndexState = "Unavailable",
    GitSyncState? Git = null,
    string ServerVersion = "Unknown",
    long LinkVersion = 0,
    string AssetState = "Unavailable",
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? LastBackupAt = null,
    string? BackupState = null);
public sealed record LibraryIdentity(int SchemaVersion, Guid LibraryId);
public sealed record CreateNoteRequest(string FolderPath, string Name, string? Title = null, Guid? Id = null, string? InitialMarkdown = null);
public sealed record CaptureNoteRequest(Guid CaptureId, string Content, string? Comment = null, string Kind = "quick-thought", DateTimeOffset? CapturedAt = null, IReadOnlyList<Guid>? AssetIds = null);
public sealed record CreateFolderRequest(string ParentPath, string Name);
public sealed record UpdateNoteRequest(string Markdown, string Revision);
public sealed record RenameItemRequest(string Path, string NewName);
public sealed record TransferItemRequest(string SourcePath, string DestinationFolderPath);
public sealed record DeleteItemRequest(string Path, bool Recursive);
public sealed record LibraryMutation(string Path, bool IsDirectory, Guid? Id = null, int AffectedItems = 1);
public sealed record ItemDetails(string Path, bool IsDirectory, int DescendantCount);
public sealed record SearchHit(Guid Id, string Title, string Path, string Snippet, float Score, string Revision);
public sealed record SearchPage(string Query, int Page, int PageSize, int Total, IReadOnlyList<SearchHit> Results, long SearchVersion);
public sealed record GitSyncState(string State, bool Pending, string? Detail = null, string? LocalHead = null, string? RemoteHead = null);
public sealed record NoteHistoryEntry(string Commit, DateTimeOffset Timestamp, string Message, string Author);
public sealed record HistoricalNote(Guid Id, string Commit, string Path, string Title, string Markdown, DateTimeOffset Timestamp, string Message);
public sealed record AssetMetadata(Guid Id, string OriginalFilename, string ContentType, long ByteSize, DateTimeOffset CreatedAt, string Sha256, string Extension, bool InlineImage);
public sealed record NoteLink(int Start, int Length, string Raw, string Target, string? Label, string? Heading, string State, Guid? TargetId, string? TargetPath, string? TargetTitle, IReadOnlyList<Guid>? Candidates = null);
public sealed record Backlink(Guid SourceId, string SourceTitle, string SourcePath, string? Heading = null);
public sealed record NoteLinks(Guid NoteId, IReadOnlyList<NoteLink> Outgoing, IReadOnlyList<Backlink> Backlinks);
public sealed record PendingAsset(Guid Id, string LocalPath, string OriginalFilename, string ContentType, long ByteSize, string Sha256);
public sealed record DeviceSummary(Guid Id, string Label, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt)
{
    public string Status => RevokedAt is null ? "Active" : "Revoked";
}
public sealed record ReleaseArtifact(string FileName, string Url, long ByteSize, string Sha256);
public sealed record ReleaseManifest(int SchemaVersion, string LatestVersion, DateTimeOffset ReleasedAt, string ReleaseNotes,
    ReleaseArtifact Windows, ReleaseArtifact Android);
public sealed record UpdateAvailability(string CurrentVersion, string LatestVersion, DateTimeOffset ReleasedAt,
    string ReleaseNotes, ReleaseArtifact Artifact, bool UpdateAvailable);

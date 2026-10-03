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
public sealed record DailyNoteRequest(DateOnly Date);
public sealed record AnswerQuestionRequest(string Answer, string Revision, DateTimeOffset AnsweredAt);
public sealed record AppendCaptureRequest(Guid CaptureId, string DestinationRevision, string CaptureRevision);
public sealed record CaptureAppendPreview(Guid CaptureId, Guid DestinationId, string DestinationPath, string DestinationRevision, string CaptureRevision, string ProposedMarkdown);
public sealed record UpdateNoteRequest(string Markdown, string Revision);
public sealed record SafeGitMergePreview(Guid Id, string LocalHead, string RemoteHead, string Candidate, IReadOnlyList<string> RemotePaths, string LibraryVersion);
public sealed record ReplayNoteRequest(Guid OperationId, Guid NoteId, string Kind, string Markdown, string? BaseRevision = null, CreateNoteRequest? Create = null, CaptureNoteRequest? Capture = null, Guid? LibraryId = null);
public sealed record RenameItemRequest(string Path, string NewName);
public sealed record TransferItemRequest(string SourcePath, string DestinationFolderPath);
public sealed record BulkOperationRequest(Guid OperationId, string Operation, IReadOnlyList<string> Paths, string DestinationFolderPath = "", string? NewName = null);
public sealed record BulkItem(string SourcePath, string? DestinationPath, bool IsDirectory);
public sealed record BulkPreview(Guid OperationId, string Operation, IReadOnlyList<BulkItem> Items, int NoteCount, string Fingerprint, IReadOnlyList<NoteTextPreview>? Repairs = null);
public sealed record BulkApplyRequest(Guid OperationId, string Fingerprint, bool RepairIncoming = false);
public sealed record BulkOperationResult(Guid OperationId, string State, IReadOnlyList<BulkItem> Items, int NoteCount);
public sealed record DeleteItemRequest(string Path, bool Recursive);
public sealed record LibraryMutation(string Path, bool IsDirectory, Guid? Id = null, int AffectedItems = 1);
public sealed record ItemDetails(string Path, bool IsDirectory, int DescendantCount);
public sealed record SearchHit(Guid Id, string Title, string Path, string Snippet, float Score, string Revision);
public sealed record SearchPage(string Query, int Page, int PageSize, int Total, IReadOnlyList<SearchHit> Results, long SearchVersion);
public sealed record GitSyncState(string State, bool Pending, string? Detail = null, string? LocalHead = null, string? RemoteHead = null);
public sealed record NoteHistoryEntry(string Commit, DateTimeOffset Timestamp, string Message, string Author);
public sealed record HistoricalNote(Guid Id, string Commit, string Path, string Title, string Markdown, DateTimeOffset Timestamp, string Message);
public sealed record RestoreNoteRequest(string Commit, string Mode, string? Revision = null, string? Folder = null, string? Name = null);
public sealed record RecoverableNote(Guid Id, string Path, string Title, string SourceCommit, string DeletionCommit, DateTimeOffset DeletedAt);
public sealed record RecoveryPage(IReadOnlyList<RecoverableNote> Notes, bool Bounded);
public sealed record AssetMetadata(Guid Id, string OriginalFilename, string ContentType, long ByteSize, DateTimeOffset CreatedAt, string Sha256, string Extension, bool InlineImage);
public sealed record AssetReference(Guid NoteId, string Title, string Path);
public sealed record AssetListing(AssetMetadata Metadata, int ReferenceCount);
public sealed record AssetPage(int Page, int Total, long TotalBytes, IReadOnlyList<AssetListing> Results);
public sealed record AssetReferencePage(int Page, int Total, IReadOnlyList<AssetReference> Results);
public sealed record AssetDerivedText(Guid AssetId, string Sha256, string Kind, string State, string Text, DateTimeOffset UpdatedAt, string? Error = null, Guid? JobId = null);
public sealed record AssetCleanupPreview(Guid Id, string Sha256, long ByteSize, string LibraryVersion, string Warning);
public sealed record AssetCleanupRequest(string Sha256, string LibraryVersion);
public sealed record AssetCleanupResult(Guid Id, long ReclaimedBytes);
public sealed record NoteLink(int Start, int Length, string Raw, string Target, string? Label, string? Heading, string State, Guid? TargetId, string? TargetPath, string? TargetTitle, IReadOnlyList<Guid>? Candidates = null, string Kind = "wiki");
public sealed record LinkReplacement(int Start, int Length, string Original, string Proposed, Guid TargetId, string TargetPath);
public sealed record NoteTextPreview(Guid Id, string Path, string Revision, string OriginalMarkdown, string ProposedMarkdown, IReadOnlyList<LinkReplacement> Changes);
public sealed record LinkIssue(Guid SourceId, string SourceTitle, string SourcePath, string SourceRevision, NoteLink Link);
public sealed record LinkIssuePage(int Page, int Total, IReadOnlyList<LinkIssue> Results);
public sealed record LinkRepairRequest(string SourceRevision, int Start, Guid TargetId, string TargetRevision);
public sealed record WikiExportRequest(string SourceRevision, string ProposedMarkdown);
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

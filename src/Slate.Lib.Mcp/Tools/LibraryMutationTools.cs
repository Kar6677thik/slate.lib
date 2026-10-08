using System.ComponentModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Slate.Lib.Core;
using Slate.Lib.Mcp.Clients;
using Slate.Lib.Mcp.Configuration;
using Slate.Lib.Mcp.Models;
using Slate.Lib.Mcp.Security;
using Slate.Lib.Mcp.Services;

namespace Slate.Lib.Mcp.Tools;

[McpServerToolType]
[Authorize]
public sealed class LibraryMutationTools(CanonicalSlateClient canonical, IntelligenceClient intelligence, ProposalProtector proposals,
    ToolExecutor executor, IOptions<SlateMcpOptions> options, IHttpContextAccessor httpContext)
{
    private readonly SlateMcpOptions settings = options.Value;

    [McpServerTool(Name = "create_note", Title = "Create note", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Write)]
    [Authorize(Policy = "scope:slate.write")]
    [Description("Create a canonical Markdown note through Slate's API. The server chooses and validates the path; no filesystem path is accepted.")]
    public Task<McpResult<LibraryNote>> CreateNote(string folderPath, string name, string? title = null, string? initialMarkdown = null, CancellationToken token = default) =>
        executor.Run<LibraryNote>("create_note", async libraryId =>
        {
            EnsureWrites(); ValidateMarkdown(initialMarkdown ?? "");
            var note = await canonical.Create(new(ToolInputs.Text(folderPath, nameof(folderPath), 1024, true), ToolInputs.Text(name, nameof(name), 200), title is null ? null : ToolInputs.Text(title, nameof(title), 300), InitialMarkdown: initialMarkdown), token);
            await intelligence.Notify(new { kind = "upsert", noteId = note.Id }, token);
            return NoteResult(libraryId, "create_note", note);
        }, token);

    [McpServerTool(Name = "update_note", Title = "Update note", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Write)]
    [Authorize(Policy = "scope:slate.write")]
    [Description("Replace a note only when the supplied canonical revision still matches. Prefer preview_note_edit then apply_note_edit for substantial edits.")]
    public Task<McpResult<LibraryNote>> UpdateNote(Guid noteId, string markdown, string revision, CancellationToken token = default) =>
        executor.Run<LibraryNote>("update_note", async libraryId =>
        {
            EnsureWrites(); ValidateMarkdown(markdown);
            var note = await canonical.Update(noteId, markdown, ToolInputs.Text(revision, nameof(revision), 256), token);
            await intelligence.Notify(new { kind = "upsert", noteId = note.Id }, token);
            return NoteResult(libraryId, "update_note", note);
        }, token);

    [McpServerTool(Name = "append_to_note", Title = "Append to note", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Write)]
    [Authorize(Policy = "scope:slate.write")]
    [Description("Append Markdown to a note with an explicit current revision. Fails safely if the note changed.")]
    public Task<McpResult<LibraryNote>> AppendToNote(Guid noteId, string markdown, string revision, CancellationToken token = default) =>
        executor.Run<LibraryNote>("append_to_note", async libraryId =>
        {
            EnsureWrites(); markdown = ToolInputs.Text(markdown, nameof(markdown), settings.MaximumMarkdownCharacters);
            var current = await canonical.Read(noteId, token);
            if (!string.Equals(current.Revision, revision, StringComparison.Ordinal)) throw new SlateUpstreamException(412, "stale_revision", "The note changed after it was read. Read it again before appending.");
            var combined = current.Markdown.TrimEnd() + Environment.NewLine + Environment.NewLine + markdown + Environment.NewLine;
            ValidateMarkdown(combined);
            var note = await canonical.Update(noteId, combined, revision, token);
            await intelligence.Notify(new { kind = "upsert", noteId = note.Id }, token);
            return NoteResult(libraryId, "append_to_note", note);
        }, token);

    [McpServerTool(Name = "preview_note_edit", Title = "Preview note edit", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Write)]
    [Authorize(Policy = "scope:slate.write")]
    [Description("Compare proposed Markdown with the current note and issue a signed, time-limited proposal token bound to the note revision and exact proposed content. This does not write.")]
    public Task<McpResult<EditPreview>> PreviewNoteEdit(Guid noteId, string proposedMarkdown, CancellationToken token = default) =>
        executor.Run<EditPreview>("preview_note_edit", async libraryId =>
        {
            EnsureWrites(); ValidateMarkdown(proposedMarkdown);
            var note = await canonical.Read(noteId, token);
            var protectedProposal = proposals.Create(noteId, note.Revision, proposedMarkdown);
            var originalLines = note.Markdown.Split('\n'); var proposedLines = proposedMarkdown.Split('\n');
            var original = originalLines.ToHashSet(StringComparer.Ordinal); var proposed = proposedLines.ToHashSet(StringComparer.Ordinal);
            var preview = new EditPreview(noteId, note.Path, note.Revision, protectedProposal.Payload.ProposedSha256,
                note.Markdown.Length, proposedMarkdown.Length, proposed.Count(line => !original.Contains(line)), original.Count(line => !proposed.Contains(line)),
                protectedProposal.Payload.ExpiresAt, protectedProposal.Token);
            return McpResult<EditPreview>.Ok(libraryId, "preview_note_edit", preview, references: [new(note.Id, note.Path, note.Title, note.Revision)]);
        }, token);

    [McpServerTool(Name = "apply_note_edit", Title = "Apply reviewed note edit", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Write)]
    [Authorize(Policy = "scope:slate.write")]
    [Description("Apply exactly the Markdown bound to a valid preview token. The write fails if the content, token, expiry, or canonical source revision differs from the reviewed preview.")]
    public Task<McpResult<LibraryNote>> ApplyNoteEdit(string proposalToken, string proposedMarkdown, CancellationToken token = default) =>
        executor.Run<LibraryNote>("apply_note_edit", async libraryId =>
        {
            EnsureWrites(); ValidateMarkdown(proposedMarkdown);
            var proposal = proposals.Validate(ToolInputs.Text(proposalToken, nameof(proposalToken), 4096), proposedMarkdown);
            var current = await canonical.Read(proposal.NoteId, token);
            if (!string.Equals(current.Revision, proposal.SourceRevision, StringComparison.Ordinal)) throw new SlateUpstreamException(412, "stale_revision", "The note changed after the preview. Create a new preview before applying.");
            var note = await canonical.Update(proposal.NoteId, proposedMarkdown, proposal.SourceRevision, token);
            await intelligence.Notify(new { kind = "upsert", noteId = note.Id }, token);
            return NoteResult(libraryId, "apply_note_edit", note);
        }, token);

    [McpServerTool(Name = "create_folder", Title = "Create folder", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Organize)]
    [Authorize(Policy = "scope:slate.organize")]
    [Description("Create a validated canonical library folder.")]
    public Task<McpResult<LibraryMutation>> CreateFolder(string parentPath, string name, CancellationToken token = default) =>
        executor.Run<LibraryMutation>("create_folder", async libraryId =>
        {
            EnsureWrites(); var result = await canonical.Folder(new(ToolInputs.Text(parentPath, nameof(parentPath), 1024, true), ToolInputs.Text(name, nameof(name), 200)), token);
            await intelligence.Notify(new { kind = "reconcile", reason = "refresh" }, token);
            return McpResult<LibraryMutation>.Ok(libraryId, "create_folder", result);
        }, token);

    [McpServerTool(Name = "capture_to_inbox", Title = "Capture to inbox", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Write)]
    [Authorize(Policy = "scope:slate.write")]
    [Description("Capture a bounded quick thought to Slate's canonical inbox using a caller-supplied idempotency ID.")]
    public Task<McpResult<LibraryNote>> CaptureToInbox(Guid captureId, string content, string? comment = null, string kind = "quick-thought", CancellationToken token = default) =>
        executor.Run<LibraryNote>("capture_to_inbox", async libraryId =>
        {
            EnsureWrites(); content = ToolInputs.Text(content, nameof(content), settings.MaximumMarkdownCharacters);
            var note = await canonical.Capture(new(captureId, content, comment is null ? null : ToolInputs.Text(comment, nameof(comment), 2000, true), ToolInputs.Text(kind, nameof(kind), 50), DateTimeOffset.UtcNow), token);
            await intelligence.Notify(new { kind = "upsert", noteId = note.Id }, token);
            return NoteResult(libraryId, "capture_to_inbox", note);
        }, token);

    [McpServerTool(Name = "create_daily_note", Title = "Create daily note", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Write)]
    [Authorize(Policy = "scope:slate.write")]
    [Description("Create or return the canonical daily note for an ISO date.")]
    public Task<McpResult<LibraryNote>> CreateDailyNote(DateOnly date, CancellationToken token = default) =>
        executor.Run<LibraryNote>("create_daily_note", async libraryId =>
        {
            EnsureWrites(); var note = await canonical.Daily(new(date), token); await intelligence.Notify(new { kind = "upsert", noteId = note.Id }, token); return NoteResult(libraryId, "create_daily_note", note);
        }, token);

    [McpServerTool(Name = "answer_open_question", Title = "Answer open question", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Write)]
    [Authorize(Policy = "scope:slate.write")]
    [Description("Append an answer to a canonical open-question note using its current revision.")]
    public Task<McpResult<LibraryNote>> AnswerOpenQuestion(Guid noteId, string answer, string revision, CancellationToken token = default) =>
        executor.Run<LibraryNote>("answer_open_question", async libraryId =>
        {
            EnsureWrites(); answer = ToolInputs.Text(answer, nameof(answer), settings.MaximumMarkdownCharacters);
            var note = await canonical.Answer(noteId, new(answer, ToolInputs.Text(revision, nameof(revision), 256), DateTimeOffset.UtcNow), token);
            await intelligence.Notify(new { kind = "upsert", noteId = note.Id }, token); return NoteResult(libraryId, "answer_open_question", note);
        }, token);

    [McpServerTool(Name = "rename_library_item", Title = "Rename library item", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Organize)]
    [Authorize(Policy = "scope:slate.organize")]
    [Description("Rename one canonical note or folder. Use bulk preview/apply when incoming-link repair needs review.")]
    public Task<McpResult<LibraryMutation>> RenameLibraryItem(string path, string newName, CancellationToken token = default) => Structural("rename_library_item", () => canonical.Rename(new(ToolInputs.Text(path, nameof(path), 1024), ToolInputs.Text(newName, nameof(newName), 200)), token), result => new { kind = "rename", path, destinationPath = result.Path }, token);

    [McpServerTool(Name = "move_library_item", Title = "Move library item", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Organize)]
    [Authorize(Policy = "scope:slate.organize")]
    [Description("Move one canonical note or folder. Use bulk preview/apply for a reviewed high-impact move.")]
    public Task<McpResult<LibraryMutation>> MoveLibraryItem(string sourcePath, string destinationFolderPath, CancellationToken token = default) => Structural("move_library_item", () => canonical.Move(new(ToolInputs.Text(sourcePath, nameof(sourcePath), 1024), ToolInputs.Text(destinationFolderPath, nameof(destinationFolderPath), 1024, true)), token), _ => new { kind = "reconcile", reason = "bulk" }, token);

    [McpServerTool(Name = "copy_library_item", Title = "Copy library item", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Organize)]
    [Authorize(Policy = "scope:slate.organize")]
    [Description("Copy one canonical note or folder through Slate's mutation API.")]
    public Task<McpResult<LibraryMutation>> CopyLibraryItem(string sourcePath, string destinationFolderPath, CancellationToken token = default) => Structural("copy_library_item", () => canonical.Copy(new(ToolInputs.Text(sourcePath, nameof(sourcePath), 1024), ToolInputs.Text(destinationFolderPath, nameof(destinationFolderPath), 1024, true)), token), _ => new { kind = "reconcile", reason = "bulk" }, token);

    [McpServerTool(Name = "duplicate_library_item", Title = "Duplicate library item", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Organize)]
    [Authorize(Policy = "scope:slate.organize")]
    [Description("Create an additive duplicate of one canonical note or folder using Slate's collision-safe copy naming and durable bulk journal.")]
    public Task<McpResult<BulkOperationResult>> DuplicateLibraryItem(string path, CancellationToken token = default) =>
        executor.Run<BulkOperationResult>("duplicate_library_item", async libraryId =>
        {
            EnsureWrites(); var operationId = Guid.NewGuid();
            var preview = await canonical.PreviewBulk(new(operationId, "duplicate", [ToolInputs.Text(path, nameof(path), 1024)]), token);
            var result = await canonical.ApplyBulk(new(operationId, preview.Fingerprint), token);
            await intelligence.Notify(new { kind = "reconcile", reason = "bulk" }, token);
            return McpResult<BulkOperationResult>.Ok(libraryId, "duplicate_library_item", result);
        }, token);

    [McpServerTool(Name = "preview_bulk_operation", Title = "Preview bulk operation", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Organize)]
    [Authorize(Policy = "scope:slate.organize")]
    [Description("Prepare a canonical, immutable bulk plan for move, copy, duplicate, rename, or delete. Delete previews require delete scope and server enablement before apply.")]
    public Task<McpResult<BulkOperationPreview>> PreviewBulkOperation(Guid operationId, string operation, string[] paths, string destinationFolderPath = "", string? newName = null, CancellationToken token = default) =>
        executor.Run<BulkOperationPreview>("preview_bulk_operation", async libraryId =>
        {
            EnsureWrites(); operation = ToolInputs.Text(operation, nameof(operation), 20).ToLowerInvariant();
            if (paths.Length is < 1 || paths.Length > settings.MaximumBulkItems) throw new ArgumentException($"Select between one and {settings.MaximumBulkItems} items.");
            if (operation == "delete") EnsureDeleteAllowed();
            var preview = await canonical.PreviewBulk(new(operationId, operation, paths.Select(path => ToolInputs.Text(path, "path", 1024)).ToArray(), ToolInputs.Text(destinationFolderPath, nameof(destinationFolderPath), 1024, true), newName), token);
            var protectedPreview = proposals.CreateBulk(operationId, operation, preview.Fingerprint);
            return McpResult<BulkOperationPreview>.Ok(libraryId, "preview_bulk_operation", new(preview, protectedPreview.Payload.ExpiresAt, protectedPreview.Token),
                warnings: operation == "delete" ? ["This plan permanently removes the selected canonical items when applied."] : [], truncated: preview.Repairs?.Count > 100);
        }, token);

    [McpServerTool(Name = "apply_bulk_operation", Title = "Apply reviewed bulk operation", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Organize)]
    [Authorize(Policy = "scope:slate.organize")]
    [Description("Apply a previously reviewed canonical bulk plan by operation ID and exact fingerprint. The canonical API rejects changed source content.")]
    public Task<McpResult<BulkOperationResult>> ApplyBulkOperation(Guid operationId, string fingerprint, string proposalToken, bool repairIncomingLinks = false, CancellationToken token = default) =>
        executor.Run<BulkOperationResult>("apply_bulk_operation", async libraryId =>
        {
            EnsureWrites();
            var reviewed = proposals.ValidateBulk(ToolInputs.Text(proposalToken, nameof(proposalToken), 4096), operationId, ToolInputs.Text(fingerprint, nameof(fingerprint), 128));
            if (reviewed.Operation == "delete") EnsureDeleteAllowed();
            var result = await canonical.ApplyBulk(new(operationId, reviewed.Fingerprint, repairIncomingLinks), token);
            await intelligence.Notify(new { kind = "reconcile", reason = "bulk" }, token);
            return McpResult<BulkOperationResult>.Ok(libraryId, "apply_bulk_operation", result);
        }, token);

    [McpServerTool(Name = "get_bulk_operation_status", Title = "Get bulk operation status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Read)]
    [Authorize(Policy = "scope:slate.read")]
    [Description("Return canonical status for a bulk operation journal.")]
    public Task<McpResult<BulkOperationResult>> GetBulkOperationStatus(Guid operationId, CancellationToken token = default) =>
        executor.Run<BulkOperationResult>("get_bulk_operation_status", async libraryId => McpResult<BulkOperationResult>.Ok(libraryId, "get_bulk_operation_status", await canonical.BulkStatus(operationId, token)), token);

    [McpServerTool(Name = "restore_note", Title = "Restore note", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Organize)]
    [Authorize(Policy = "scope:slate.organize")]
    [Description("Restore a note from canonical Git history. Mode and optional destination are validated by Slate's API.")]
    public Task<McpResult<LibraryNote>> RestoreNote(Guid noteId, string commit, string mode, string? revision = null, string? folder = null, string? name = null, CancellationToken token = default) =>
        executor.Run<LibraryNote>("restore_note", async libraryId =>
        {
            EnsureWrites(); var note = await canonical.Restore(noteId, new(ToolInputs.Text(commit, nameof(commit), 100), ToolInputs.Text(mode, nameof(mode), 30), revision, folder, name), token);
            await intelligence.Notify(new { kind = "reconcile", reason = "restore" }, token); return NoteResult(libraryId, "restore_note", note);
        }, token);

    [McpServerTool(Name = "preview_link_repair", Title = "Preview link repair", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Organize)]
    [Authorize(Policy = "scope:slate.organize")]
    [Description("Preview one canonical unresolved-link repair against exact source and target revisions.")]
    public Task<McpResult<NoteTextPreview>> PreviewLinkRepair(Guid sourceNoteId, string sourceRevision, int linkStart, Guid targetNoteId, string targetRevision, CancellationToken token = default) =>
        executor.Run<NoteTextPreview>("preview_link_repair", async libraryId => McpResult<NoteTextPreview>.Ok(libraryId, "preview_link_repair", await canonical.PreviewLinkRepair(sourceNoteId, new(sourceRevision, linkStart, targetNoteId, targetRevision), token)), token);

    [McpServerTool(Name = "apply_link_repair", Title = "Apply link repair", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Organize)]
    [Authorize(Policy = "scope:slate.organize")]
    [Description("Apply one canonical link repair only when source and target revisions still match the previewed values.")]
    public Task<McpResult<LibraryNote>> ApplyLinkRepair(Guid sourceNoteId, string sourceRevision, int linkStart, Guid targetNoteId, string targetRevision, CancellationToken token = default) =>
        executor.Run<LibraryNote>("apply_link_repair", async libraryId =>
        {
            EnsureWrites(); var note = await canonical.ApplyLinkRepair(sourceNoteId, new(sourceRevision, linkStart, targetNoteId, targetRevision), token);
            await intelligence.Notify(new { kind = "upsert", noteId = note.Id }, token); return NoteResult(libraryId, "apply_link_repair", note);
        }, token);

    private async Task<McpResult<LibraryMutation>> Structural(string operation, Func<Task<LibraryMutation>> action, Func<LibraryMutation, object> eventFactory, CancellationToken token) => await executor.Run<LibraryMutation>(operation, async libraryId =>
    {
        EnsureWrites(); var result = await action(); await intelligence.Notify(eventFactory(result), token); return McpResult<LibraryMutation>.Ok(libraryId, operation, result);
    }, token);

    private static McpResult<LibraryNote> NoteResult(Guid libraryId, string operation, LibraryNote note) => McpResult<LibraryNote>.Ok(libraryId, operation, note, references: [new(note.Id, note.Path, note.Title, note.Revision)]);
    private void EnsureWrites() { if (!settings.EnableWrites) throw new ArgumentException("Writes are disabled on this MCP server."); }
    private void EnsureDeleteAllowed()
    {
        if (!settings.EnableDestructiveOperations) throw new ArgumentException("Destructive operations are disabled on this MCP server.");
        var user = httpContext.HttpContext?.User;
        var scopes = user?.Claims.Where(claim => claim.Type is "scope" or "scp").SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (scopes is null || !scopes.Any(scope => scope is SlateScopes.Delete or SlateScopes.Admin)) throw new ArgumentException("The caller does not have slate.delete scope.");
    }
    private void ValidateMarkdown(string markdown) { if (markdown.Length > settings.MaximumMarkdownCharacters) throw new ArgumentException($"Markdown exceeds {settings.MaximumMarkdownCharacters} characters."); }
}

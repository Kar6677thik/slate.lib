using System.Text.Json.Serialization;
using System.Text.Json;

namespace Slate.Lib.Mcp.Models;

public sealed record McpReference(
    [property: JsonPropertyName("noteId")] Guid? NoteId,
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("revision")] string? Revision = null,
    [property: JsonPropertyName("heading")] string? Heading = null);

public sealed record McpPagination(
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int? PageSize,
    [property: JsonPropertyName("nextPage")] int? NextPage,
    [property: JsonPropertyName("total")] int? Total = null);

public sealed record McpResult<T>(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("libraryId")] Guid? LibraryId,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("data")] T? Data,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("truncated")] bool Truncated = false,
    [property: JsonPropertyName("pagination")] McpPagination? Pagination = null,
    [property: JsonPropertyName("degraded")] string? Degraded = null,
    [property: JsonPropertyName("references")] IReadOnlyList<McpReference>? References = null,
    [property: JsonPropertyName("error")] McpError? Error = null)
{
    public static McpResult<T> Ok(Guid libraryId, string operation, T data, IReadOnlyList<string>? warnings = null,
        bool truncated = false, McpPagination? pagination = null, string? degraded = null, IReadOnlyList<McpReference>? references = null) =>
        new(true, libraryId, operation, data, warnings ?? [], truncated, pagination, degraded, references);

    public static McpResult<T> Fail(Guid? libraryId, string operation, string code, string message, bool retryable = false) =>
        new(false, libraryId, operation, default, [], Error: new McpError(code, message, retryable));
}

public sealed record McpError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("retryable")] bool Retryable);

public sealed record CapabilityReport(
    bool CanonicalAvailable,
    bool IntelligenceConfigured,
    bool WritesEnabled,
    bool DestructiveOperationsEnabled,
    string ProtocolMode,
    string[] Scopes,
    string[] DegradedCapabilities);

public sealed record EditPreview(
    Guid NoteId,
    string Path,
    string SourceRevision,
    string ProposedSha256,
    int OriginalCharacters,
    int ProposedCharacters,
    int AddedLines,
    int RemovedLines,
    DateTimeOffset ExpiresAt,
    string ProposalToken);

public sealed record ProposalPayload(Guid NoteId, string SourceRevision, string ProposedSha256, DateTimeOffset ExpiresAt);
public sealed record BulkProposalPayload(Guid OperationId, string Operation, string Fingerprint, DateTimeOffset ExpiresAt);
public sealed record BulkOperationPreview(object Preview, DateTimeOffset ExpiresAt, string ProposalToken);

public sealed record LearningCoverageItem(string Topic, int SourceCount, IReadOnlyList<McpReference> Sources, IReadOnlyList<string> EvidenceSignals);
public sealed record LearningRecommendation(string Topic, string Reason, IReadOnlyList<McpReference> Sources, IReadOnlyList<string> SuggestedNextSteps);
public sealed record LearningPathStep(int Order, string Topic, string Purpose, IReadOnlyList<McpReference> Sources);
public sealed record SelfTestQuestion(string Question, string EvidenceHint, IReadOnlyList<McpReference> Sources);
public sealed record SearchNotesResult(string Query, string Mode, string EffectiveMode, int Page, int PageSize, int Total, JsonElement Results, string? Degraded = null);

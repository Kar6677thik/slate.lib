using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Slate.Lib.Core;
using Slate.Lib.Mcp.Configuration;

namespace Slate.Lib.Mcp.Clients;

public sealed class CanonicalSlateClient(HttpClient http, IOptions<SlateMcpOptions> options, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SlateMcpOptions settings = options.Value;
    private readonly string token = configuration["Secrets:CanonicalDeviceToken"] ?? "";

    public async Task<LibraryStatus> Status(CancellationToken token) => await Get<LibraryStatus>("v1/status", token);
    public async Task<FolderPage> Browse(string path, int page, CancellationToken token) => await Get<FolderPage>($"v1/library?path={Uri.EscapeDataString(path)}&page={page}", token);
    public async Task<LibraryNote> Read(Guid id, CancellationToken token) => await Get<LibraryNote>($"v1/notes/{id:D}", token);
    public async Task<LibraryNote> ReadPath(string path, CancellationToken token) => await Get<LibraryNote>($"v1/notes/by-path?path={Uri.EscapeDataString(path)}", token);
    public async Task<NoteLinks> Links(Guid id, CancellationToken token) => await Get<NoteLinks>($"v1/notes/{id:D}/links", token);
    public async Task<IReadOnlyList<RelatedNote>> Related(Guid id, CancellationToken token) => await Get<IReadOnlyList<RelatedNote>>($"v1/notes/{id:D}/related", token);
    public async Task<IReadOnlyList<NoteHistoryEntry>> History(Guid id, CancellationToken token) => await Get<IReadOnlyList<NoteHistoryEntry>>($"v1/notes/{id:D}/history", token);
    public async Task<KnowledgeGraph> Graph(Guid id, int depth, int limit, string? folder, string? type, CancellationToken token) =>
        await Get<KnowledgeGraph>($"v1/notes/{id:D}/graph?depth={depth}&limit={limit}&folder={Uri.EscapeDataString(folder ?? "")}&type={Uri.EscapeDataString(type ?? "")}", token);
    public async Task<SearchPage> Search(string query, int page, int pageSize, CancellationToken token) => await Get<SearchPage>($"v1/search?q={Uri.EscapeDataString(query)}&page={page}&pageSize={pageSize}", token);
    public async Task<SearchPage> View(string view, int page, int pageSize, CancellationToken token) => await Get<SearchPage>($"v1/views/{Uri.EscapeDataString(view)}?page={page}&pageSize={pageSize}", token);
    public async Task<LinkIssuePage> LinkIssues(int page, CancellationToken token) => await Get<LinkIssuePage>($"v1/links/issues?page={page}", token);
    public async Task<RecoveryPage> Deleted(CancellationToken token) => await Get<RecoveryPage>("v1/history/deleted", token);
    public async Task<BulkOperationResult> BulkStatus(Guid operationId, CancellationToken token) => await Get<BulkOperationResult>($"v1/library/bulk/{operationId:D}", token);

    public async Task<LibraryNote> Create(CreateNoteRequest request, CancellationToken token) => await Send<LibraryNote>(HttpMethod.Post, "v1/notes", request, null, token);
    public async Task<LibraryNote> Update(Guid id, string markdown, string revision, CancellationToken token) => await Send<LibraryNote>(HttpMethod.Put, $"v1/notes/{id:D}", new UpdateNoteRequest(markdown, revision), revision, token);
    public async Task<LibraryNote> Capture(CaptureNoteRequest request, CancellationToken token) => await Send<LibraryNote>(HttpMethod.Post, "v1/captures", request, null, token);
    public async Task<LibraryMutation> Folder(CreateFolderRequest request, CancellationToken token) => await Send<LibraryMutation>(HttpMethod.Post, "v1/folders", request, null, token);
    public async Task<LibraryNote> Daily(DailyNoteRequest request, CancellationToken token) => await Send<LibraryNote>(HttpMethod.Post, "v1/workflows/daily", request, null, token);
    public async Task<LibraryNote> Answer(Guid id, AnswerQuestionRequest request, CancellationToken token) => await Send<LibraryNote>(HttpMethod.Post, $"v1/notes/{id:D}/answer", request, null, token);
    public async Task<LibraryMutation> Rename(RenameItemRequest request, CancellationToken token) => await Send<LibraryMutation>(HttpMethod.Post, "v1/library/rename", request, null, token);
    public async Task<LibraryMutation> Move(TransferItemRequest request, CancellationToken token) => await Send<LibraryMutation>(HttpMethod.Post, "v1/library/move", request, null, token);
    public async Task<LibraryMutation> Copy(TransferItemRequest request, CancellationToken token) => await Send<LibraryMutation>(HttpMethod.Post, "v1/library/copy", request, null, token);
    public async Task<BulkPreview> PreviewBulk(BulkOperationRequest request, CancellationToken token) => await Send<BulkPreview>(HttpMethod.Post, "v1/library/bulk/preview", request, null, token);
    public async Task<BulkOperationResult> ApplyBulk(BulkApplyRequest request, CancellationToken token) => await Send<BulkOperationResult>(HttpMethod.Post, "v1/library/bulk/apply", request, null, token);
    public async Task<LibraryNote> Restore(Guid id, RestoreNoteRequest request, CancellationToken token) => await Send<LibraryNote>(HttpMethod.Post, $"v1/notes/{id:D}/restore", request, null, token);
    public async Task<NoteTextPreview> PreviewLinkRepair(Guid id, LinkRepairRequest request, CancellationToken token) => await Send<NoteTextPreview>(HttpMethod.Post, $"v1/notes/{id:D}/link-repair/preview", request, null, token);
    public async Task<LibraryNote> ApplyLinkRepair(Guid id, LinkRepairRequest request, CancellationToken token) => await Send<LibraryNote>(HttpMethod.Post, $"v1/notes/{id:D}/link-repair/apply", request, null, token);
    public async Task<LibraryMutation> Delete(DeleteItemRequest request, CancellationToken token) => await Send<LibraryMutation>(HttpMethod.Post, "v1/library/delete", request, null, token);

    private async Task<T> Get<T>(string path, CancellationToken cancellationToken) => await Send<T>(HttpMethod.Get, path, null, null, cancellationToken);

    private async Task<T> Send<T>(HttpMethod method, string path, object? body, string? ifMatch, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new SlateUpstreamException(503, "canonical_auth_missing", "The MCP server has no canonical Slate device token.");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var bytes = await BoundedHttpContent.ReadAsync(response.Content, settings.MaximumResponseBytes, cancellationToken);
        if (!response.IsSuccessStatusCode) throw ToException(response.StatusCode);
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new SlateUpstreamException(502, "empty_upstream_response", "The canonical Slate API returned an empty response.", true);
    }

    private static SlateUpstreamException ToException(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => new(502, "canonical_auth_failed", "The canonical Slate API rejected its server-held device token."),
        HttpStatusCode.NotFound => new(404, "not_found", "The requested note or folder does not exist."),
        HttpStatusCode.Conflict => new(409, "conflict", "The requested name or identity already exists."),
        HttpStatusCode.PreconditionFailed => new(412, "stale_revision", "The note changed after it was reviewed. Read it again before applying the edit."),
        HttpStatusCode.TooManyRequests => new(429, "upstream_rate_limited", "The canonical Slate API is rate limiting requests.", true),
        _ when (int)status >= 500 => new(503, "canonical_unavailable", "The canonical Slate API is unavailable.", true),
        _ => new((int)status, "canonical_rejected", "The canonical Slate API rejected the request.")
    };
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Slate.Lib.Core;

public sealed class LibraryApiClient(HttpClient http)
{
    public void Connect(string address, string token)
    {
        if (!Uri.TryCreate(address.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Use HTTPS, or HTTP on localhost for development.");
        BaseUri = uri;
        Token = token.Trim();
    }

    private Uri? BaseUri { get; set; }
    private string Token { get; set; } = "";
    public Task<LibraryStatus> StatusAsync(CancellationToken cancellationToken) => GetAsync<LibraryStatus>("v1/status", cancellationToken);
    public Task<FolderPage> ListAsync(string path, int page, CancellationToken cancellationToken) =>
        GetAsync<FolderPage>($"v1/library?path={Uri.EscapeDataString(path)}&page={page}", cancellationToken);
    public Task<LibraryNote> ReadAsync(Guid id, CancellationToken cancellationToken) => GetAsync<LibraryNote>($"v1/notes/{id:D}", cancellationToken);
    public Task<LibraryNote> ReadPathAsync(string path, CancellationToken cancellationToken) =>
        GetAsync<LibraryNote>($"v1/notes/by-path?path={Uri.EscapeDataString(path)}", cancellationToken);
    public Task<NoteLinks> LinksAsync(Guid id, CancellationToken cancellationToken) =>
        GetAsync<NoteLinks>($"v1/notes/{id:D}/links", cancellationToken);
    public Task<LinkIssuePage> LinkIssuesAsync(int page, CancellationToken token = default) => GetAsync<LinkIssuePage>($"v1/links/issues?page={page}", token);
    public Task<NoteTextPreview> WikiExportAsync(Guid id, CancellationToken token = default) => GetAsync<NoteTextPreview>($"v1/notes/{id:D}/wiki-export", token);
    public Task<LibraryNote> ApplyWikiExportAsync(Guid id, WikiExportRequest request, CancellationToken token = default) => SendAsync<LibraryNote>(HttpMethod.Post, $"v1/notes/{id:D}/wiki-export", request, token);
    public Task<NoteTextPreview> PreviewLinkRepairAsync(Guid id, LinkRepairRequest request, CancellationToken token = default) => SendAsync<NoteTextPreview>(HttpMethod.Post, $"v1/notes/{id:D}/link-repair/preview", request, token);
    public Task<LibraryNote> ApplyLinkRepairAsync(Guid id, LinkRepairRequest request, CancellationToken token = default) => SendAsync<LibraryNote>(HttpMethod.Post, $"v1/notes/{id:D}/link-repair/apply", request, token);
    public Task<AssetMetadata> AssetMetadataAsync(Guid id, CancellationToken cancellationToken) =>
        GetAsync<AssetMetadata>($"v1/assets/{id:D}/metadata", cancellationToken);
    public Task<AssetPage> AssetsAsync(int page, bool unreferenced = false, CancellationToken token = default) => GetAsync<AssetPage>($"v1/assets?page={page}&unreferenced={unreferenced}", token);
    public Task<AssetReferencePage> AssetReferencesAsync(Guid id, int page, CancellationToken token = default) => GetAsync<AssetReferencePage>($"v1/assets/{id:D}/references?page={page}", token);
    public Task<AssetDerivedText> AssetTextAsync(Guid id, CancellationToken token = default) => GetAsync<AssetDerivedText>($"v1/assets/{id:D}/text", token);
    public async Task<AssetDerivedText> ExtractAssetAsync(Guid id, CancellationToken token = default)
    {
        var started = await SendAsync<AssetDerivedText>(HttpMethod.Post, $"v1/assets/{id:D}/extract", new { }, token);
        for (var i = 0; i < 80; i++)
        {
            await Task.Delay(750, token);
            var state = await AssetTextAsync(id, token);
            if (state.State is "ready" or "failed" && state.JobId == started.JobId) return state;
        }
        return started with { Error = "Extraction is still finishing. Choose View extracted text shortly." };
    }
    public Task<AssetCleanupPreview> PreviewAssetCleanupAsync(Guid id, CancellationToken token = default) => GetAsync<AssetCleanupPreview>($"v1/assets/{id:D}/cleanup", token);
    public Task<AssetCleanupResult> CleanupAssetAsync(Guid id, AssetCleanupRequest request, CancellationToken token = default) => SendAsync<AssetCleanupResult>(HttpMethod.Post, $"v1/assets/{id:D}/cleanup", request, token);
    public async Task<byte[]> AssetThumbnailAsync(Guid id, CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUri ?? throw new InvalidOperationException("Connect first."), $"v1/assets/{id:D}/thumbnail"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        using var response = await http.SendAsync(request, token); EnsureSuccess(response);
        return await response.Content.ReadAsByteArrayAsync(token);
    }
    public Task<UpdateAvailability> CheckForUpdateAsync(string platform, string currentVersion, CancellationToken cancellationToken) =>
        GetAsync<UpdateAvailability>($"v1/updates?platform={Uri.EscapeDataString(platform)}&currentVersion={Uri.EscapeDataString(currentVersion)}", cancellationToken);
    public Task<SearchPage> SearchAsync(string query, int page, CancellationToken cancellationToken) =>
        GetAsync<SearchPage>($"v1/search?q={Uri.EscapeDataString(query)}&page={page}&pageSize=20", cancellationToken);
    public Task<SearchPage> SmartViewAsync(string view, int page, CancellationToken token = default) => GetAsync<SearchPage>($"v1/views/{Uri.EscapeDataString(view)}?page={page}&pageSize=20", token);
    public Task<IReadOnlyList<NoteHistoryEntry>> HistoryAsync(Guid id, CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<NoteHistoryEntry>>($"v1/notes/{id:D}/history", cancellationToken);
    public Task<HistoricalNote> HistoricalNoteAsync(Guid id, string commit, CancellationToken cancellationToken) =>
        GetAsync<HistoricalNote>($"v1/notes/{id:D}/history/{Uri.EscapeDataString(commit)}", cancellationToken);
    public Task<RecoveryPage> RecoverableAsync(CancellationToken token = default) => GetAsync<RecoveryPage>("v1/history/deleted", token);
    public Task<KnowledgeGraph> GraphAsync(Guid id, int depth = 1, int limit = 40, string? folder = null, string? type = null) => GetAsync<KnowledgeGraph>($"v1/notes/{id}/graph?depth={depth}&limit={limit}&folder={Uri.EscapeDataString(folder ?? "")}&type={Uri.EscapeDataString(type ?? "")}", default);
    public Task<IReadOnlyList<RelatedNote>> RelatedAsync(Guid id) => GetAsync<IReadOnlyList<RelatedNote>>($"v1/notes/{id}/related", default);
    public Task<RediscoveryPage> RediscoverAsync(string view, DateOnly today, int page = 0, CancellationToken token = default) => GetAsync<RediscoveryPage>($"v1/rediscovery/{Uri.EscapeDataString(view)}?today={today:yyyy-MM-dd}&page={page}", token);
    public Task<LibraryNote> RestoreAsync(Guid id, RestoreNoteRequest request, CancellationToken token = default) => SendAsync<LibraryNote>(HttpMethod.Post, $"v1/notes/{id:D}/restore", request, token);
    public Task<LibraryNote> CreateNoteAsync(CreateNoteRequest request, CancellationToken cancellationToken) => SendAsync<LibraryNote>(HttpMethod.Post, "v1/notes", request, cancellationToken);
    public Task<LibraryNote> ReplayAsync(ReplayNoteRequest request, CancellationToken token = default) => SendAsync<LibraryNote>(HttpMethod.Post, "v1/offline/replay", request, token);
    public Task<SafeGitMergePreview> PreviewSyncMergeAsync(CancellationToken token = default) => SendAsync<SafeGitMergePreview>(HttpMethod.Post, "v1/sync/merge-preview", new { }, token);
    public Task<GitSyncState> ApplySyncMergeAsync(Guid id, CancellationToken token = default) => SendAsync<GitSyncState>(HttpMethod.Post, $"v1/sync/merge/{id:D}", new { }, token);
    public Task<BulkPreview> PreviewBulkAsync(BulkOperationRequest request, CancellationToken token = default) => SendAsync<BulkPreview>(HttpMethod.Post, "v1/library/bulk/preview", request, token);
    public async Task<BulkOperationResult> ApplyBulkAsync(BulkApplyRequest request, CancellationToken token = default)
    {
        try { return await SendAsync<BulkOperationResult>(HttpMethod.Post, "v1/library/bulk/apply", request, token); }
        catch (HttpRequestException exception) when (exception.StatusCode is null)
        {
            // A lost acknowledgement must never turn into a second structural operation.
            var receipt = await BulkStatusAsync(request.OperationId, token);
            if (receipt.State == "completed") return receipt;
            throw new HttpRequestException($"Operation {request.OperationId:D} is {receipt.State}. Refresh before reviewing another operation.", exception);
        }
    }
    public Task<BulkOperationResult> BulkStatusAsync(Guid id, CancellationToken token = default) => GetAsync<BulkOperationResult>($"v1/library/bulk/{id:D}", token);
    public Task<LibraryNote> CaptureAsync(CaptureNoteRequest request, CancellationToken cancellationToken) => SendAsync<LibraryNote>(HttpMethod.Post, "v1/captures", request, cancellationToken);
    public Task<LibraryNote> DailyAsync(DateOnly date, CancellationToken token = default) => SendAsync<LibraryNote>(HttpMethod.Post, "v1/workflows/daily", new DailyNoteRequest(date), token);
    public Task<LibraryNote> AnswerAsync(Guid id, AnswerQuestionRequest request, CancellationToken token = default) => SendAsync<LibraryNote>(HttpMethod.Post, $"v1/notes/{id:D}/answer", request, token);
    public Task<CaptureAppendPreview> PreviewAppendAsync(Guid destination, Guid capture, CancellationToken token = default) => GetAsync<CaptureAppendPreview>($"v1/notes/{destination:D}/append-preview?captureId={capture:D}", token);
    public Task<LibraryNote> AppendCaptureAsync(Guid destination, AppendCaptureRequest request, CancellationToken token = default) => SendAsync<LibraryNote>(HttpMethod.Post, $"v1/notes/{destination:D}/append-capture", request, token);
    public async Task<AssetMetadata> UploadAssetAsync(Guid id, string filename, string contentType, Stream content, long length, string sha256, CancellationToken cancellationToken)
    {
        var relative = $"v1/assets?id={id:D}&filename={Uri.EscapeDataString(filename)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri ?? throw new InvalidOperationException("Connect first."), relative));
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentLength = length;
        request.Content.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var mediaType) ? mediaType : new MediaTypeHeaderValue("application/octet-stream");
        request.Headers.TryAddWithoutValidation("X-Slate-Sha256", sha256);
        return await SendAsync<AssetMetadata>(request, cancellationToken);
    }

    public async Task DownloadAssetAsync(Guid id, string destination, CancellationToken cancellationToken, long? maximumBytes = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUri ?? throw new InvalidOperationException("Connect first."), $"v1/assets/{id:D}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSuccess(response);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        var buffer = new byte[81920]; long copied = 0;
        while (true)
        {
            var count = await input.ReadAsync(buffer, cancellationToken); if (count == 0) break;
            copied += count; if (maximumBytes is { } maximum && copied > maximum) throw new InvalidDataException("Attachment exceeds the reserved download budget.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
    }

    public async Task<byte[]> ReadAssetBytesAsync(Guid id, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUri ?? throw new InvalidOperationException("Connect first."), $"v1/assets/{id:D}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSuccess(response);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task DownloadReleaseAsync(UpdateAvailability update, string destination, CancellationToken cancellationToken)
    {
        var baseUri = BaseUri ?? throw new InvalidOperationException("Connect first.");
        var uri = new Uri(baseUri, update.Artifact.Url);
        if (uri.Scheme != baseUri.Scheme || uri.Host != baseUri.Host || uri.Port != baseUri.Port)
            throw new InvalidDataException("Release downloads must use the configured Slate server.");
        var temporary = destination + ".tmp";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            EnsureSuccess(response);
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken); output.Flush(true);
            }
            await ReleaseVersions.VerifyFileAsync(temporary, update.Artifact, cancellationToken);
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public Task<LibraryMutation> CreateFolderAsync(CreateFolderRequest request, CancellationToken cancellationToken) => SendAsync<LibraryMutation>(HttpMethod.Post, "v1/folders", request, cancellationToken);
    public async Task<LibraryNote> UpdateNoteAsync(Guid id, UpdateNoteRequest update, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(BaseUri ?? throw new InvalidOperationException("Connect first."), $"v1/notes/{id:D}"))
        { Content = JsonContent.Create(update) };
        request.Headers.TryAddWithoutValidation("If-Match", update.Revision);
        return await SendAsync<LibraryNote>(request, cancellationToken);
    }
    public Task<LibraryMutation> RenameAsync(RenameItemRequest request, CancellationToken cancellationToken) => SendAsync<LibraryMutation>(HttpMethod.Post, "v1/library/rename", request, cancellationToken);
    public Task<LibraryMutation> MoveAsync(TransferItemRequest request, CancellationToken cancellationToken) => SendAsync<LibraryMutation>(HttpMethod.Post, "v1/library/move", request, cancellationToken);
    public Task<LibraryMutation> CopyAsync(TransferItemRequest request, CancellationToken cancellationToken) => SendAsync<LibraryMutation>(HttpMethod.Post, "v1/library/copy", request, cancellationToken);
    public Task<LibraryMutation> DeleteAsync(DeleteItemRequest request, CancellationToken cancellationToken) => SendAsync<LibraryMutation>(HttpMethod.Post, "v1/library/delete", request, cancellationToken);
    public Task<ItemDetails> DetailsAsync(string path, CancellationToken cancellationToken) => GetAsync<ItemDetails>($"v1/library/item?path={Uri.EscapeDataString(path)}", cancellationToken);
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri ?? throw new InvalidOperationException("Connect first."), "v1/library/refresh"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("The library could not be refreshed.", null, response.StatusCode);
    }

    public Task<GitSyncState> SyncAsync(CancellationToken cancellationToken) =>
        SendWithoutBodyAsync<GitSyncState>("v1/sync", cancellationToken);

    public Task<GitSyncState> FlushGitAsync(CancellationToken cancellationToken) =>
        SendWithoutBodyAsync<GitSyncState>("v1/git/flush", cancellationToken);

    private async Task<T> SendWithoutBodyAsync<T>(string relative, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri ?? throw new InvalidOperationException("Connect first."), relative));
        return await SendAsync<T>(request, cancellationToken);
    }

    private async Task<T> GetAsync<T>(string relative, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUri ?? throw new InvalidOperationException("Connect first."), relative));
        return await SendAsync<T>(request, cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string relative, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(BaseUri ?? throw new InvalidOperationException("Connect first."), relative))
        { Content = JsonContent.Create(body) };
        return await SendAsync<T>(request, cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        using var response = await http.SendAsync(request, cancellationToken);
        EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken) ?? throw new HttpRequestException("The server returned an empty response.");
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "The device token is missing or invalid. Check connection settings.",
            HttpStatusCode.NotFound => "This note, folder, or attachment no longer exists. Refresh the library.",
            HttpStatusCode.BadRequest => "The requested library path is invalid.",
            HttpStatusCode.Conflict => "That name, note identity, or attachment identity already exists.",
            HttpStatusCode.PreconditionFailed => "This note changed elsewhere. Your draft was kept; reopen the latest version before saving again.",
            HttpStatusCode.RequestEntityTooLarge => "This attachment exceeds the server size limit.",
            HttpStatusCode.UnprocessableEntity => "This Markdown file or attachment has invalid metadata. Check the server log.",
            _ => "The server could not read the library. Check its log and try again."
        };
        throw new HttpRequestException(message, null, response.StatusCode);
    }
}

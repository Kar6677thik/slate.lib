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
    public Task<AssetMetadata> AssetMetadataAsync(Guid id, CancellationToken cancellationToken) =>
        GetAsync<AssetMetadata>($"v1/assets/{id:D}/metadata", cancellationToken);
    public Task<UpdateAvailability> CheckForUpdateAsync(string platform, string currentVersion, CancellationToken cancellationToken) =>
        GetAsync<UpdateAvailability>($"v1/updates?platform={Uri.EscapeDataString(platform)}&currentVersion={Uri.EscapeDataString(currentVersion)}", cancellationToken);
    public Task<SearchPage> SearchAsync(string query, int page, CancellationToken cancellationToken) =>
        GetAsync<SearchPage>($"v1/search?q={Uri.EscapeDataString(query)}&page={page}&pageSize=20", cancellationToken);
    public Task<IReadOnlyList<NoteHistoryEntry>> HistoryAsync(Guid id, CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<NoteHistoryEntry>>($"v1/notes/{id:D}/history", cancellationToken);
    public Task<HistoricalNote> HistoricalNoteAsync(Guid id, string commit, CancellationToken cancellationToken) =>
        GetAsync<HistoricalNote>($"v1/notes/{id:D}/history/{Uri.EscapeDataString(commit)}", cancellationToken);
    public Task<LibraryNote> CreateNoteAsync(CreateNoteRequest request, CancellationToken cancellationToken) => SendAsync<LibraryNote>(HttpMethod.Post, "v1/notes", request, cancellationToken);
    public Task<LibraryNote> CaptureAsync(CaptureNoteRequest request, CancellationToken cancellationToken) => SendAsync<LibraryNote>(HttpMethod.Post, "v1/captures", request, cancellationToken);
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

    public async Task DownloadAssetAsync(Guid id, string destination, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUri ?? throw new InvalidOperationException("Connect first."), $"v1/assets/{id:D}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSuccess(response);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await input.CopyToAsync(output, cancellationToken);
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

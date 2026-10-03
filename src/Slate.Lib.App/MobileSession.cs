using System.Text.Json;
using System.Security.Cryptography;
using Slate.Lib.Core;

namespace Slate.Lib.App;

public sealed record MobileReadResult(LibraryNote Note, bool Offline, DateTimeOffset? FetchedAt = null);


public sealed class MobileSession(LibraryApiClient api, MarkdownReader renderer)
{
    private readonly SemaphoreSlim connectGate = new(1, 1);
    public LibraryApiClient Api { get; } = api;
    public MarkdownReader Renderer { get; } = renderer;
    public ClientStateStore? State { get; private set; }
    public PendingAssetStore? Assets { get; private set; }
    public LibraryStatus? Status { get; private set; }
    public bool IsConnected { get; private set; }
    public string Server => Preferences.Get("server", "http://localhost:5188");

    public bool LoadKnownLocalState()
    {
        if (State is not null) return true;
        if (!Guid.TryParse(Preferences.Get("library-id", ""), out var libraryId) || libraryId == Guid.Empty) return false;
        State = new ClientStateStore(FileSystem.AppDataDirectory, FileSystem.CacheDirectory, libraryId,
            DeviceInfo.Platform == DevicePlatform.Android ? 100L * 1024 * 1024 : 500L * 1024 * 1024);
        Assets = new PendingAssetStore(FileSystem.AppDataDirectory, libraryId);
        OfflineReplayPump.Start(State, Api);
        return true;
    }

    public async Task<LibraryStatus> ConnectAsync(string? server = null, string? token = null, CancellationToken cancellationToken = default)
    {
        await connectGate.WaitAsync(cancellationToken);
        try
        {
            if (server is null && token is null && IsConnected && Status is not null) return Status;
            return await ConnectCoreAsync(server, token, cancellationToken);
        }
        finally { connectGate.Release(); }
    }

    private async Task<LibraryStatus> ConnectCoreAsync(string? server, string? token, CancellationToken cancellationToken)
    {
        server ??= Server;
        token ??= await SecureStorage.GetAsync("device-token") ?? "";
        Api.Connect(server, token);
        var status = await Api.StatusAsync(cancellationToken);
        Preferences.Set("server", server);
        Preferences.Set("library-id", status.LibraryId.ToString("D"));
        await SecureStorage.SetAsync("device-token", token);
        Status = status;
        State = new ClientStateStore(FileSystem.AppDataDirectory, FileSystem.CacheDirectory, status.LibraryId,
            DeviceInfo.Platform == DevicePlatform.Android ? 100L * 1024 * 1024 : 500L * 1024 * 1024);
        Assets = new PendingAssetStore(FileSystem.AppDataDirectory, status.LibraryId);
        OfflineReplayPump.Start(State, Api);
        await WorkspaceNotes.MigrateBookmarksAsync(State, server);
        IsConnected = true;
        await State.ReconcileOfflineAsync(Api, cancellationToken);
        await RetryPendingCapturesAsync(cancellationToken);
        return status;
    }

    public async Task<MobileReadResult> ReadAsync(Guid id, CancellationToken token = default)
    {
        if (!IsConnected && State is not null && await State.Offline.ReadAsync(id) is { } downloaded) return new(downloaded.Note, true, downloaded.FetchedAt);
        try
        {
            var note = await Api.ReadAsync(id, token);
            IsConnected = true;
            if (State is not null) await State.CacheNoteAsync(note, cancellationToken: token);
            return new(note, false);
        }
        catch (HttpRequestException) when (State is not null)
        {
            IsConnected = false;
            var cached = await State.ReadCachedNoteAsync(id, token);
            if (cached is null) throw;
            return new(cached.Note, true, cached.FetchedAt);
        }
    }

    public async Task<int> RetryPendingCapturesAsync(CancellationToken token = default)
    {
        if (State is null) return 0;
        await State.ReconcileOfflineAsync(Api, token);
        var count = await new PendingCaptureProcessor(State).RetryAsync(
            (asset, cancellation) => PendingAssetStore.UploadAsync(Api, asset, cancellation), Api.CaptureAsync, token);
        return count;
    }

    public async Task EnsureConnectedAsync(CancellationToken token = default)
    {
        if (State is null || !IsConnected) await ConnectAsync(cancellationToken: token);
    }
}

public static class ShareSignals
{
    public static event EventHandler? Received;
    public static void Notify() => Received?.Invoke(null, EventArgs.Empty);
}

public static class IncomingShareStore
{
    private static readonly ShareInboxStore Store = new(FileSystem.AppDataDirectory);
    public static ShareInboxStore Inbox => Store;
    public static Task<IReadOnlyList<IncomingShare>> TakeAllAsync(CancellationToken token = default) => Store.ReadAsync(token);
    public static Task PromoteAsync(IncomingShare share, ClientStateStore state) => Store.PromoteAsync(share, state);
    public static async Task SaveAsync(SharedCaptureInput payload, CancellationToken token = default)
    {
        var share = await Store.BeginAsync(payload); await Store.SaveAsync(share with { Complete = true }, token);
    }
    public static async Task SaveAssetAsync(Stream source, string filename, string contentType, CancellationToken token = default)
    {
        var share = await Store.BeginAsync(); share = await Store.AddFileAsync(share, source, filename, contentType, token); await Store.SaveAsync(share with { Complete = true }, token);
    }
}

using System.Text.Json;
using System.Security.Cryptography;
using Slate.Lib.Core;

namespace Slate.Lib.App;

public sealed record MobileReadResult(LibraryNote Note, bool Offline, DateTimeOffset? FetchedAt = null);
public sealed record IncomingShare(Guid Id, SharedCaptureInput? Payload, PendingAsset? Asset, DateTimeOffset ReceivedAt);

public sealed class MobileSession(LibraryApiClient api, MarkdownReader renderer)
{
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
        return true;
    }

    public async Task<LibraryStatus> ConnectAsync(string? server = null, string? token = null, CancellationToken cancellationToken = default)
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
        IsConnected = true;
        await RetryPendingCapturesAsync(cancellationToken);
        return status;
    }

    public async Task<MobileReadResult> ReadAsync(Guid id, CancellationToken token = default)
    {
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
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string DirectoryPath => Path.Combine(FileSystem.AppDataDirectory, "incoming-shares");

    public static async Task SaveAsync(SharedCaptureInput payload, CancellationToken token = default)
    {
        var item = new IncomingShare(Guid.NewGuid(), payload, null, DateTimeOffset.UtcNow);
        await Gate.WaitAsync(token);
        try { await AtomicJson.WriteAsync(Path.Combine(DirectoryPath, item.Id.ToString("D") + ".json"), item, Json, token); }
        finally { Gate.Release(); }
    }

    public static async Task SaveAssetAsync(Stream source, string filename, string contentType, CancellationToken token = default)
    {
        var id = Guid.NewGuid();
        var assetDirectory = Path.Combine(FileSystem.AppDataDirectory, "incoming-assets");
        Directory.CreateDirectory(assetDirectory);
        var path = Path.Combine(assetDirectory, id.ToString("D") + ".bin");
        var temporary = path + ".tmp";
        long length = 0;
        try
        {
            string sha;
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, token);
                    if (read == 0) break;
                    length += read;
                    if (length > 25L * 1024 * 1024) throw new InvalidDataException("The shared attachment exceeds 25 MiB.");
                    hash.AppendData(buffer.AsSpan(0, read));
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                }
                await output.FlushAsync(token); output.Flush(true);
                sha = Convert.ToHexStringLower(hash.GetHashAndReset());
            }
            File.Move(temporary, path);
            var asset = new PendingAsset(id, path, Path.GetFileName(filename), contentType, length, sha);
            var item = new IncomingShare(Guid.NewGuid(), null, asset, DateTimeOffset.UtcNow);
            await Gate.WaitAsync(token);
            try { await AtomicJson.WriteAsync(Path.Combine(DirectoryPath, item.Id.ToString("D") + ".json"), item, Json, token); }
            finally { Gate.Release(); }
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    public static async Task<IReadOnlyList<IncomingShare>> TakeAllAsync(CancellationToken token = default)
    {
        await Gate.WaitAsync(token);
        try
        {
            if (!Directory.Exists(DirectoryPath)) return [];
            var result = new List<IncomingShare>();
            foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.json").OrderBy(x => x, StringComparer.Ordinal))
            {
                await using var stream = File.OpenRead(path);
                if (await JsonSerializer.DeserializeAsync<IncomingShare>(stream, Json, token) is { } item) result.Add(item);
                File.Delete(path);
            }
            return result;
        }
        finally { Gate.Release(); }
    }
}

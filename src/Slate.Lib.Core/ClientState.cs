using System.Text.Json;
using System.Security.Cryptography;

namespace Slate.Lib.Core;

public enum DraftKind { ExistingNote, NewNote, QuickThought, Share }

public sealed record DraftRecord(
    Guid DraftId,
    DraftKind Kind,
    string Markdown,
    DateTimeOffset UpdatedAt,
    Guid? NoteId = null,
    string? TargetPath = null,
    string? BaseRevision = null,
    string? BaseMarkdown = null,
    CaptureNoteRequest? PendingCapture = null,
    IReadOnlyList<PendingAsset>? PendingAssets = null);

public sealed record CachedNoteRecord(LibraryNote Note, DateTimeOffset FetchedAt, DateTimeOffset LastAccessedAt);
public sealed record RecentNote(Guid Id, string Path, string Title, DateTimeOffset LastAccessedAt);

public sealed class ClientStateStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string draftsDirectory;
    private readonly string cacheDirectory;
    private readonly string preferencesPath;
    private readonly long cacheLimitBytes;
    private readonly int recentLimit;
    private readonly SemaphoreSlim gate = new(1, 1);

    public ClientStateStore(string appDataRoot, string cacheRoot, Guid libraryId, long cacheLimitBytes, int recentLimit = 100)
    {
        if (libraryId == Guid.Empty) throw new ArgumentException("A library ID is required.", nameof(libraryId));
        if (cacheLimitBytes < 1024) throw new ArgumentOutOfRangeException(nameof(cacheLimitBytes));
        if (recentLimit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(recentLimit));
        draftsDirectory = Path.Combine(appDataRoot, "libraries", libraryId.ToString("D"), "drafts");
        cacheDirectory = Path.Combine(cacheRoot, "slate.lib", libraryId.ToString("D"), "notes");
        preferencesPath = Path.Combine(appDataRoot, "libraries", libraryId.ToString("D"), "preferences.json");
        this.cacheLimitBytes = cacheLimitBytes;
        this.recentLimit = recentLimit;
    }

    public async Task SaveDraftAsync(DraftRecord draft, CancellationToken cancellationToken = default)
    {
        if (draft.DraftId == Guid.Empty) throw new ArgumentException("A draft ID is required.", nameof(draft));
        await gate.WaitAsync(cancellationToken);
        try { await AtomicJson.WriteAsync(Path.Combine(draftsDirectory, draft.DraftId.ToString("D") + ".json"), draft, Json, cancellationToken); }
        finally { gate.Release(); }
    }

    public async Task<DraftRecord?> ReadDraftAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await ReadJsonAsync<DraftRecord>(Path.Combine(draftsDirectory, draftId.ToString("D") + ".json"), cancellationToken); }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<DraftRecord>> ReadDraftsAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(draftsDirectory)) return [];
            var drafts = new List<DraftRecord>();
            foreach (var path in Directory.EnumerateFiles(draftsDirectory, "*.json").OrderBy(x => x, StringComparer.Ordinal))
                if (await ReadJsonAsync<DraftRecord>(path, cancellationToken) is { } draft) drafts.Add(draft);
            return drafts.OrderByDescending(x => x.UpdatedAt).ToArray();
        }
        finally { gate.Release(); }
    }

    public async Task DeleteDraftAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try { var path = Path.Combine(draftsDirectory, draftId.ToString("D") + ".json"); if (File.Exists(path)) File.Delete(path); }
        finally { gate.Release(); }
    }

    public async Task CacheNoteAsync(LibraryNote note, DateTimeOffset? now = null, CancellationToken cancellationToken = default)
    {
        var instant = now ?? DateTimeOffset.UtcNow;
        await gate.WaitAsync(cancellationToken);
        try
        {
            await AtomicJson.WriteAsync(Path.Combine(cacheDirectory, note.Id.ToString("D") + ".json"), new CachedNoteRecord(note, instant, instant), Json, cancellationToken);
            var recents = (await ReadRecentsUnsafe(cancellationToken)).Where(x => x.Id != note.Id).Prepend(new RecentNote(note.Id, note.Path, note.Title, instant)).Take(recentLimit).ToArray();
            await AtomicJson.WriteAsync(preferencesPath, recents, Json, cancellationToken);
            EvictCacheUnsafe(recents.Select(x => x.Id).ToArray());
        }
        finally { gate.Release(); }
    }

    public async Task<CachedNoteRecord?> ReadCachedNoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = Path.Combine(cacheDirectory, id.ToString("D") + ".json");
            var cached = await ReadJsonAsync<CachedNoteRecord>(path, cancellationToken);
            if (cached is null) return null;
            cached = cached with { LastAccessedAt = DateTimeOffset.UtcNow };
            await AtomicJson.WriteAsync(path, cached, Json, cancellationToken);
            return cached;
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<RecentNote>> ReadRecentsAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await ReadRecentsUnsafe(cancellationToken); }
        finally { gate.Release(); }
    }

    private async Task<IReadOnlyList<RecentNote>> ReadRecentsUnsafe(CancellationToken token) =>
        (await ReadJsonAsync<RecentNote[]>(preferencesPath, token) ?? []).Take(recentLimit).ToArray();

    private void EvictCacheUnsafe(IReadOnlyList<Guid> recentIds)
    {
        if (!Directory.Exists(cacheDirectory)) return;
        var files = Directory.EnumerateFiles(cacheDirectory, "*.json")
            .Select(path => new FileInfo(path)).OrderByDescending(file =>
            {
                var stem = Path.GetFileNameWithoutExtension(file.Name);
                var id = Guid.TryParse(stem, out var value) ? value : Guid.Empty;
                var index = recentIds.IndexOf(id);
                return index < 0 ? int.MinValue : -index;
            }).ToList();
        long total = files.Sum(x => x.Length);
        foreach (var file in files.OrderBy(x => x.LastWriteTimeUtc))
        {
            if (total <= cacheLimitBytes) break;
            total -= file.Length;
            file.Delete();
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken token)
    {
        if (!File.Exists(path)) return default;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await JsonSerializer.DeserializeAsync<T>(stream, Json, token);
    }
}

public sealed class DraftDebouncer(ClientStateStore store, TimeSpan? delay = null) : IAsyncDisposable
{
    private readonly TimeSpan delay = delay ?? TimeSpan.FromSeconds(1);
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource? pending;
    private DraftRecord? latest;

    public void Schedule(DraftRecord draft)
    {
        latest = draft;
        pending?.Cancel(); pending?.Dispose(); pending = new();
        _ = PersistAfterDelayAsync(draft, pending.Token);
    }

    private async Task PersistAfterDelayAsync(DraftRecord draft, CancellationToken token)
    {
        try { await Task.Delay(delay, token); await PersistAsync(draft, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task PersistAsync(DraftRecord draft, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try { await store.SaveDraftAsync(draft, token); }
        finally { gate.Release(); }
    }

    public async Task FlushAsync(CancellationToken token = default)
    {
        pending?.Cancel();
        if (latest is { } draft) await PersistAsync(draft, token);
    }

    public void Clear()
    {
        pending?.Cancel(); pending?.Dispose(); pending = null; latest = null;
    }

    public async ValueTask DisposeAsync()
    {
        await FlushAsync(); pending?.Dispose(); gate.Dispose();
    }
}

public sealed class PendingCaptureProcessor(ClientStateStore store)
{
    public async Task<int> RetryAsync(Func<CaptureNoteRequest, CancellationToken, Task<LibraryNote>> submit, CancellationToken token = default)
    {
        var completed = 0;
        foreach (var draft in (await store.ReadDraftsAsync(token)).Where(x => x.PendingCapture is not null).OrderBy(x => x.UpdatedAt))
        {
            try
            {
                await submit(draft.PendingCapture!, token);
                await store.DeleteDraftAsync(draft.DraftId, token);
                completed++;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (HttpRequestException) { }
        }
        return completed;
    }

    public async Task<int> RetryAsync(
        Func<PendingAsset, CancellationToken, Task<AssetMetadata>> upload,
        Func<CaptureNoteRequest, CancellationToken, Task<LibraryNote>> submit,
        CancellationToken token = default)
    {
        var completed = 0;
        foreach (var draft in (await store.ReadDraftsAsync(token)).Where(x => x.PendingCapture is not null).OrderBy(x => x.UpdatedAt))
        {
            try
            {
                var ids = new List<Guid>(draft.PendingCapture!.AssetIds ?? []);
                foreach (var asset in draft.PendingAssets ?? [])
                {
                    await upload(asset, token);
                    if (!ids.Contains(asset.Id)) ids.Add(asset.Id);
                }
                await submit(draft.PendingCapture with { AssetIds = ids }, token);
                await store.DeleteDraftAsync(draft.DraftId, token);
                foreach (var asset in draft.PendingAssets ?? []) if (File.Exists(asset.LocalPath)) File.Delete(asset.LocalPath);
                completed++;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException) { }
        }
        return completed;
    }
}

public sealed class PendingAssetStore
{
    private readonly string directory;
    private readonly long maximumBytes;

    public PendingAssetStore(string appDataRoot, Guid libraryId, long maximumBytes = 25L * 1024 * 1024)
    {
        if (libraryId == Guid.Empty) throw new ArgumentException("A library ID is required.", nameof(libraryId));
        directory = Path.Combine(appDataRoot, "libraries", libraryId.ToString("D"), "pending-assets");
        this.maximumBytes = maximumBytes;
    }

    public async Task<PendingAsset> StageAsync(Stream source, string? filename, string? contentType, CancellationToken token = default)
    {
        var id = Guid.NewGuid();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, id.ToString("D") + ".bin");
        var temporary = path + ".tmp";
        long length = 0;
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, token);
                    if (read == 0) break;
                    length += read;
                    if (length > maximumBytes) throw new InvalidDataException("The attachment exceeds the 25 MiB client limit.");
                    hash.AppendData(buffer.AsSpan(0, read));
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                }
                await output.FlushAsync(token); output.Flush(true);
            }
            File.Move(temporary, path);
            var safeName = Path.GetFileName(filename ?? "attachment.bin");
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "attachment.bin";
            return new(id, path, safeName, string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType, length,
                Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        catch { if (File.Exists(temporary)) File.Delete(temporary); throw; }
    }

    public static async Task<AssetMetadata> UploadAsync(LibraryApiClient api, PendingAsset asset, CancellationToken token = default)
    {
        await using var stream = new FileStream(asset.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await api.UploadAssetAsync(asset.Id, asset.OriginalFilename, asset.ContentType, stream, asset.ByteSize, asset.Sha256, token);
    }

    public static void Delete(PendingAsset asset) { if (File.Exists(asset.LocalPath)) File.Delete(asset.LocalPath); }
}

public static class AtomicJson
{
    public static async Task WriteAsync<T>(string path, T value, JsonSerializerOptions options, CancellationToken token = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, ".slate-tmp-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, options, token);
                await stream.FlushAsync(token);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

internal static class CollectionExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> source, T value)
    {
        for (var index = 0; index < source.Count; index++) if (EqualityComparer<T>.Default.Equals(source[index], value)) return index;
        return -1;
    }
}

public sealed record SharedCaptureInput(string Text, bool IsUrl)
{
    public static SharedCaptureInput Parse(string? action, string? mimeType, string? text)
    {
        if (!string.Equals(action, "android.intent.action.SEND", StringComparison.Ordinal) ||
            !string.Equals(mimeType, "text/plain", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Slate accepts one plain-text or URL share.");
        var value = text?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 * 1024) throw new ArgumentException("Shared text is empty or too large.");
        var isUrl = Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
        return new(value, isUrl);
    }
}

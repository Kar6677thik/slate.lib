using System.Text.Json;

namespace Slate.Lib.Core;

public sealed record IncomingShare(Guid Id, SharedCaptureInput? Payload, PendingAsset? Asset, DateTimeOffset ReceivedAt,
    IReadOnlyList<PendingAsset>? Assets = null, string? Error = null, bool Complete = true);

public sealed class ShareInboxStore(string appDataRoot)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HashSet<Guid> active = [];
    private string DirectoryPath => Path.Combine(appDataRoot, "incoming-shares");
    public async Task<IncomingShare> BeginAsync(SharedCaptureInput? payload = null)
    {
        var share = new IncomingShare(Guid.NewGuid(), payload, null, DateTimeOffset.UtcNow, [], Complete: false);
        await SaveAsync(share); return share;
    }
    public async Task<IncomingShare> AddFileAsync(IncomingShare share, Stream source, string filename, string contentType, CancellationToken token = default)
    {
        var assets = (share.Assets ?? (share.Asset is null ? [] : [share.Asset])).ToList();
        if (assets.Count >= 8) throw new ArgumentException("Share at most eight files in one capture.");
        var remaining = 64L * 1024 * 1024 - assets.Sum(x => x.ByteSize);
        if (remaining <= 0) throw new InvalidDataException("A shared capture can contain at most 64 MiB.");
        var file = await PendingAssetStore.ForShareStaging(appDataRoot, Math.Min(25L * 1024 * 1024, remaining)).StageAsync(source, filename, contentType, token);
        assets.Add(file); share = share with { Assets = assets.ToArray() }; await SaveAsync(share, token); return share;
    }
    public async Task SaveAsync(IncomingShare share, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            await AtomicJson.WriteAsync(Path.Combine(DirectoryPath, share.Id.ToString("D") + ".json"), share, Json, token);
            if (share.Complete || share.Error is not null) active.Remove(share.Id); else active.Add(share.Id);
        }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<IncomingShare>> ReadAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            if (!Directory.Exists(DirectoryPath)) return [];
            var result = new List<IncomingShare>();
            foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.json").Order(StringComparer.Ordinal))
            {
                await using var stream = File.OpenRead(file);
                var share = await JsonSerializer.DeserializeAsync<IncomingShare>(stream, Json, token);
                await stream.DisposeAsync();
                if (share is null || active.Contains(share.Id)) continue;
                if (!share.Complete && share.Error is null)
                {
                    share = share with { Complete = true, Error = "Sharing was interrupted. Review the files already saved; share any missing files again." };
                    await AtomicJson.WriteAsync(file, share, Json, token);
                }
                result.Add(share);
            }
            return result;
        }
        finally { gate.Release(); }
    }
    public async Task PromoteAsync(IncomingShare share, ClientStateStore state, CancellationToken token = default)
    {
        var assets = share.Assets ?? (share.Asset is null ? [] : [share.Asset]);
        var request = new CaptureNoteRequest(share.Id, share.Payload?.Text ?? "", Kind: "share", CapturedAt: share.ReceivedAt, AssetIds: assets.Select(x => x.Id).ToArray());
        if (await state.ReadDraftAsync(share.Id, token) is null)
            await state.SaveDraftAsync(new(share.Id, DraftKind.Share, share.Payload?.Text ?? string.Join(", ", assets.Select(x => x.OriginalFilename)), DateTimeOffset.UtcNow,
                TargetPath: "inbox", PendingCapture: request, PendingAssets: assets, ReadyToSubmit: false), token);
        // Acknowledge only after the durable draft exists; retain the old handoff for recovery.
        await gate.WaitAsync(token);
        try
        {
            var source = Path.Combine(DirectoryPath, share.Id.ToString("D") + ".json"); var handled = Path.Combine(DirectoryPath, "handled"); Directory.CreateDirectory(handled);
            var target = Path.Combine(handled, Path.GetFileName(source));
            if (File.Exists(source) && !File.Exists(target)) File.Move(source, target, false);
        }
        finally { gate.Release(); }
    }
}

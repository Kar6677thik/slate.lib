using System.Collections.Concurrent;
using System.Text.Json;

namespace Slate.Lib.Core;

public sealed record KnowledgeSummary(Guid Id, string Path, string Title, IReadOnlyList<string> Tags, IReadOnlyList<string> Headings, string? Type, string? Status, DateTimeOffset? Created, DateTimeOffset? Modified);
public sealed record RediscoveryHit(Guid Id, string Path, string Title, string Reason, DateTimeOffset? Date);
public sealed record RediscoveryPage(string View, int Page, int Total, IReadOnlyList<RediscoveryHit> Results);
public sealed record ReadingActivity(Guid NoteId, DateTimeOffset LastOpenedAt);
public sealed record ReadingActivityState(int Schema, bool Enabled, DateTimeOffset? StartedAt, IReadOnlyList<ReadingActivity> Notes);

public sealed class ReadingActivityStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string path; private readonly SemaphoreSlim gate;
    public ReadingActivityStore(string appData, Guid libraryId)
    { path = Path.Combine(appData, "libraries", libraryId.ToString(), "reading-activity.json"); gate = Gates.GetOrAdd(Path.GetFullPath(path), _ => new(1, 1)); }
    public async Task<ReadingActivityState> ReadAsync(DateTimeOffset? now = null)
    {
        var state = File.Exists(path) ? JsonSerializer.Deserialize<ReadingActivityState>(await File.ReadAllTextAsync(path), Json) : null;
        state ??= new(1, false, null, []);
        if (state.Schema != 1) throw new InvalidDataException("Unsupported reading-activity version. Existing data was preserved.");
        return state with { Notes = state.Notes.Where(x => x.LastOpenedAt >= (now ?? DateTimeOffset.UtcNow).AddDays(-180)).OrderByDescending(x => x.LastOpenedAt).Take(1000).ToArray() };
    }
    public async Task ConfigureAsync(bool enabled, bool clear = false)
    {
        await gate.WaitAsync();
        try { var state = await ReadAsync(); await AtomicJson.WriteAsync(path, state with { Enabled = enabled, StartedAt = clear || state.StartedAt is null ? DateTimeOffset.UtcNow : state.StartedAt, Notes = clear ? [] : state.Notes }, Json); }
        finally { gate.Release(); }
    }
    public async Task OpenedAsync(Guid id, DateTimeOffset? now = null)
    {
        await gate.WaitAsync();
        try
        {
            var instant = now ?? DateTimeOffset.UtcNow; var state = await ReadAsync(instant); if (!state.Enabled) return;
            if (state.Notes.Any(x => x.NoteId == id && instant - x.LastOpenedAt < TimeSpan.FromMinutes(1))) return;
            await AtomicJson.WriteAsync(path, state with { Notes = state.Notes.Where(x => x.NoteId != id).Prepend(new(id, instant)).Take(1000).ToArray() }, Json);
        }
        finally { gate.Release(); }
    }
}

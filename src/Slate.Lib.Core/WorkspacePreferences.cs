using System.Collections.Concurrent;
using System.Text.Json;

namespace Slate.Lib.Core;

public sealed record FavoriteNote(Guid Id, string Title);
public sealed record FolderPin(string Path, string Label);
public sealed record SavedSearch(Guid Id, string Name, string Query);
public sealed record WorkspacePreferences(int SchemaVersion, IReadOnlyList<FavoriteNote> Favorites,
    IReadOnlyList<FolderPin> Pins, IReadOnlyList<SavedSearch> Searches, bool LegacyBookmarksMigrated = false)
{
    public static WorkspacePreferences Empty => new(1, [], [], []);
}

/// <summary>Device-local, library-scoped preferences, independent of content, credentials and disposable caches.</summary>
public sealed class WorkspacePreferenceStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string path;
    private readonly SemaphoreSlim gate;
    public WorkspacePreferenceStore(string appDataRoot, Guid libraryId)
    {
        if (libraryId == Guid.Empty) throw new ArgumentException("A library identity is required.");
        path = Path.Combine(appDataRoot, "libraries", libraryId.ToString("D"), "workspace-preferences.json");
        gate = Gates.GetOrAdd(Path.GetFullPath(path), _ => new(1, 1));
    }
    public async Task<WorkspacePreferences> ReadAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try { return await ReadCoreAsync(token); } finally { gate.Release(); }
    }
    private async Task<WorkspacePreferences> ReadCoreAsync(CancellationToken token)
    {
        if (!File.Exists(path)) return WorkspacePreferences.Empty;
        await using var stream = File.OpenRead(path);
        var value = await JsonSerializer.DeserializeAsync<WorkspacePreferences>(stream, Json, token);
        if (value?.SchemaVersion != 1 || value.Favorites is null || value.Pins is null || value.Searches is null)
            throw new InvalidDataException("These preferences need a newer version of Slate. The original file was preserved.");
        return value;
    }
    private async Task ChangeAsync(Func<WorkspacePreferences, WorkspacePreferences> change, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var next = change(await ReadCoreAsync(token));
            if (next.Favorites.Count > 1000 || next.Pins.Count > 200 || next.Searches.Count > 100) throw new InvalidOperationException("The local preference limit was reached.");
            await AtomicJson.WriteAsync(path, next, Json, token);
        }
        finally { gate.Release(); }
    }
    public Task ImportFavoritesAsync(IEnumerable<FavoriteNote> favorites, CancellationToken token = default) => ChangeAsync(p =>
        p with { Favorites = p.Favorites.Concat(favorites).Where(f => f.Id != Guid.Empty).DistinctBy(f => f.Id).ToArray() }, token);
    public Task ImportLegacyFavoritesAsync(IEnumerable<FavoriteNote> favorites) => ChangeAsync(p => p.LegacyBookmarksMigrated ? p :
        p with { Favorites = p.Favorites.Concat(favorites).Where(f => f.Id != Guid.Empty).DistinctBy(f => f.Id).ToArray(), LegacyBookmarksMigrated = true });
    public Task ToggleFavoriteAsync(Guid id, string title, CancellationToken token = default)
    {
        if (id == Guid.Empty) throw new ArgumentException("A note identity is required.");
        return ChangeAsync(p => p with { Favorites = p.Favorites.Any(f => f.Id == id) ? p.Favorites.Where(f => f.Id != id).ToArray() : [.. p.Favorites, new(id, title)] }, token);
    }
    public Task RemoveFavoriteAsync(Guid id) => ChangeAsync(p => p with { Favorites = p.Favorites.Where(f => f.Id != id).ToArray() });
    public Task UpdateTitleAsync(Guid id, string title) => ChangeAsync(p => p with { Favorites = p.Favorites.Select(f => f.Id == id ? f with { Title = title } : f).ToArray() });
    public Task TogglePinAsync(string folder, string? label = null)
    {
        ValidatePath(folder);
        return ChangeAsync(p => p with { Pins = p.Pins.Any(f => Same(f.Path, folder)) ? p.Pins.Where(f => !Same(f.Path, folder)).ToArray() : [.. p.Pins, new(folder, label ?? Path.GetFileName(folder))] });
    }
    public Task RemovePinAsync(string folder) => ChangeAsync(p => p with { Pins = p.Pins.Where(f => !Same(f.Path, folder)).ToArray() });
    public Task RepinAsync(string previous, string folder)
    {
        ValidatePath(folder);
        return ChangeAsync(p => p with { Pins = p.Pins.Select(f => Same(f.Path, previous) ? new FolderPin(folder, Path.GetFileName(folder)) : f).DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray() });
    }
    public Task RemapFoldersAsync(IEnumerable<BulkItem> changes)
    {
        var moved = changes.Where(x => x.IsDirectory && x.DestinationPath is not null).OrderByDescending(x => x.SourcePath.Length).ToArray();
        return ChangeAsync(p => p with { Pins = p.Pins.Select(pin =>
        {
            var item = moved.FirstOrDefault(m => Same(pin.Path, m.SourcePath) || pin.Path.StartsWith(m.SourcePath + "/", StringComparison.OrdinalIgnoreCase));
            var target = item is null ? pin.Path : item.DestinationPath + pin.Path[item.SourcePath.Length..];
            return pin with { Path = target!, Label = item is null ? pin.Label : Path.GetFileName(target) };
        }).DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray() });
    }
    private static bool Same(string first, string second) => first.Equals(second, StringComparison.OrdinalIgnoreCase);
    public Task SaveSearchAsync(Guid id, string name, string query)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80 || string.IsNullOrWhiteSpace(query) || query.Length > 512)
            throw new ArgumentException("Give the search a name (up to 80 characters) and a query (up to 512 characters).");
        return ChangeAsync(p => p with { Searches = p.Searches.Where(s => s.Id != id).Append(new SavedSearch(id, name.Trim(), query)).ToArray() });
    }
    public Task DeleteSearchAsync(Guid id) => ChangeAsync(p => p with { Searches = p.Searches.Where(s => s.Id != id).ToArray() });
    private static void ValidatePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 220 || !path.IsNormalized() || path.Split('/').Any(p => p.Length is 0 or > 100 || p.StartsWith('.') || p.EndsWith(' ') || p.EndsWith('.') || p.Any(c => char.IsControl(c) || "<>:\"\\|?*".Contains(c))))
            throw new ArgumentException("Choose a canonical library folder path.");
    }
}

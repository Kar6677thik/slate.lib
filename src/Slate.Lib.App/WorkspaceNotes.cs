using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Slate.Lib.Core;

namespace Slate.Lib.App;

internal static class WorkspaceNotes
{
    internal sealed record Bookmark(Guid Id, string Title);
    internal static string[] ParseHeadings(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "null") return [];
        if (json.StartsWith('"')) json = JsonSerializer.Deserialize<string>(json) ?? "[]";
        if (json.StartsWith('%')) json = Uri.UnescapeDataString(json);
        return JsonSerializer.Deserialize<string[]>(json) ?? [];
    }
    private static string Key(string server) => "bookmarks-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(server.TrimEnd('/').ToLowerInvariant())));
    internal static List<Bookmark> Bookmarks(string server)
    {
        try { return JsonSerializer.Deserialize<List<Bookmark>>(Preferences.Get(Key(server), "[]")) ?? []; }
        catch (JsonException) { return []; }
    }
    internal static void ToggleBookmark(string server, LibraryNote note)
    {
        var items = Bookmarks(server);
        if (items.RemoveAll(x => x.Id == note.Id) == 0) items.Add(new(note.Id, note.Title));
        Preferences.Set(Key(server), JsonSerializer.Serialize(items));
    }
    internal static async Task MigrateBookmarksAsync(ClientStateStore state, string server)
    {
        // Leave the legacy platform preference intact, including on a failed migration.
        await state.Workspace.ImportLegacyFavoritesAsync(Bookmarks(server).Select(x => new FavoriteNote(x.Id, x.Title)));
    }
    internal static async Task<LibraryNote> DailyAsync(LibraryApiClient api)
    {
        return await api.DailyAsync(DateOnly.FromDateTime(DateTime.Today));
    }
}

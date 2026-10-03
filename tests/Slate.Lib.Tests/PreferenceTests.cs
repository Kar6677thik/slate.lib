using Slate.Lib.Api;
using Slate.Lib.Core;
using Xunit;

namespace Slate.Lib.Tests;

public sealed class PreferenceTests
{
    [Fact]
    public async Task FavoritesFollowIdentityThroughMoveAndUpdateTitle()
    {
        using var fixture = new TestLibrary(); var library = new LibraryStore(new(fixture.Root));
        var state = new ClientStateStore(fixture.DerivedRoot, fixture.DerivedRoot, library.LibraryId, 1024 * 1024);
        var note = library.Read(fixture.NoteId); await state.Workspace.ToggleFavoriteAsync(note.Id, note.Title);
        library.Move(new(note.Path, "Empty")); var moved = library.Read(note.Id); await state.CacheNoteAsync(moved with { Title = "New title" });
        var favorite = Assert.Single((await state.Workspace.ReadAsync()).Favorites);
        Assert.Equal(note.Id, favorite.Id); Assert.Equal("Empty/Note.md", library.Read(favorite.Id).Path); Assert.Equal("New title", favorite.Title);
        var reopened = new WorkspacePreferenceStore(fixture.DerivedRoot, library.LibraryId);
        Assert.Equal(favorite, Assert.Single((await reopened.ReadAsync()).Favorites));
    }
    [Fact]
    public async Task FolderMoveUpdatesNestedPinsWhileExternalMoveLeavesMissingPinForExplicitRecovery()
    {
        using var fixture = new TestLibrary(); var library = new LibraryStore(new(fixture.Root));
        var preferences = new WorkspacePreferenceStore(fixture.DerivedRoot, library.LibraryId);
        await preferences.TogglePinAsync("Science/Databases");
        var changed = library.Move(new("Science", "Empty"));
        await preferences.RemapFoldersAsync([new("Science", changed.Path, true)]);
        var pin = Assert.Single((await preferences.ReadAsync()).Pins); Assert.Equal("Empty/Science/Databases", pin.Path);
        Directory.Move(Path.Combine(fixture.Root, "Empty", "Science"), Path.Combine(fixture.Root, "Outside rename"));
        Assert.Equal(pin, Assert.Single((await preferences.ReadAsync()).Pins)); Assert.False(Directory.Exists(Path.Combine(fixture.Root, pin.Path)));
        await preferences.RepinAsync(pin.Path, "Outside rename/Databases");
        Assert.Equal("Outside rename/Databases", Assert.Single((await preferences.ReadAsync()).Pins).Path);
        await preferences.RemovePinAsync("Outside rename/Databases"); Assert.Empty((await preferences.ReadAsync()).Pins);
    }
    [Fact]
    public async Task LegacyMigrationIsOneTimeAndKeepsExistingRecentsAndDrafts()
    {
        using var fixture = new TestLibrary(); var id = Guid.NewGuid();
        var directory = Path.Combine(fixture.DerivedRoot, "libraries", id.ToString("D")); Directory.CreateDirectory(directory);
        var oldPreferences = Path.Combine(directory, "preferences.json"); File.WriteAllText(oldPreferences, "[]");
        var state = new ClientStateStore(fixture.DerivedRoot, fixture.DerivedRoot, id, 1024 * 1024);
        var draft = new DraftRecord(Guid.NewGuid(), DraftKind.NewNote, "unsent", DateTimeOffset.UtcNow); await state.SaveDraftAsync(draft);
        var favorite = new FavoriteNote(Guid.NewGuid(), "Old bookmark");
        await state.Workspace.ImportLegacyFavoritesAsync([favorite]); await state.Workspace.ToggleFavoriteAsync(favorite.Id, favorite.Title);
        await state.Workspace.ImportLegacyFavoritesAsync([favorite]); Assert.Empty((await state.Workspace.ReadAsync()).Favorites);
        Assert.Equal("[]", File.ReadAllText(oldPreferences)); Assert.Equal(draft, await state.ReadDraftAsync(draft.DraftId));
    }
    [Fact]
    public async Task UnsupportedSchemaNeverGetsOverwritten()
    {
        using var fixture = new TestLibrary(); var id = Guid.NewGuid();
        var path = Path.Combine(fixture.DerivedRoot, "libraries", id.ToString("D"), "workspace-preferences.json"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string source = "{\"schemaVersion\":99,\"favorites\":[],\"pins\":[],\"searches\":[]}"; File.WriteAllText(path, source);
        var state = new WorkspacePreferenceStore(fixture.DerivedRoot, id);
        await Assert.ThrowsAsync<InvalidDataException>(() => state.TogglePinAsync("folder")); Assert.Equal(source, File.ReadAllText(path));
    }
    [Fact]
    public async Task ConcurrentPreferenceInstancesDoNotLoseFavorites()
    {
        using var fixture = new TestLibrary(); var id = Guid.NewGuid();
        var first = new WorkspacePreferenceStore(fixture.DerivedRoot, id); var second = new WorkspacePreferenceStore(fixture.DerivedRoot, id);
        await Task.WhenAll(first.ToggleFavoriteAsync(Guid.NewGuid(), "First"), second.ToggleFavoriteAsync(Guid.NewGuid(), "Second"));
        Assert.Equal(2, (await first.ReadAsync()).Favorites.Count);
        await Assert.ThrowsAsync<ArgumentException>(() => first.TogglePinAsync("../outside"));
    }
}

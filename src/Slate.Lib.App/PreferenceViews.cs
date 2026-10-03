using System.Net;
using Slate.Lib.Core;

namespace Slate.Lib.App;

public partial class MainPage
{
    private async Task ToggleCurrentFavoriteAsync()
    {
        if (currentNote is not { } note || clientState is null) return;
        await clientState.Workspace.ToggleFavoriteAsync(note.Id, note.Title);
        Status.Text = (await clientState.Workspace.ReadAsync()).Favorites.Any(x => x.Id == note.Id) ? "Added to Favorites" : "Removed from Favorites";
    }
    private async Task ShowFavoritesAsync()
    {
        if (clientState is null) { Status.Text = "Connect to open Favorites."; return; }
        var favorites = (await clientState.Workspace.ReadAsync()).Favorites;
        ShowResultView("Favorites · on this device", favorites.Select(x => new SearchHit(x.Id, x.Title, "", "Favorite", 0, "")).ToArray(), favorites.Count == 0 ? "Favorite a note from its menu." : "");
    }
    private async Task ToggleFolderPinAsync(string path)
    {
        if (clientState is null) return;
        var details = await api.DetailsAsync(path, connection.Token); if (!details.IsDirectory) return;
        await clientState.Workspace.TogglePinAsync(path); await RefreshPinnedFoldersAsync();
    }
    private async Task RefreshPinnedFoldersAsync()
    {
        PinnedFolders.Clear(); if (clientState is null) return;
        var pins = (await clientState.Workspace.ReadAsync()).Pins; PinnedFolders.IsVisible = pins.Count > 0;
        if (pins.Count == 0) return;
        PinnedFolders.Add(MobileTheme.Section("Pinned"));
        foreach (var pin in pins.Take(5)) PinnedFolders.Add(ClientControls.NavigationRow(pin.Label, () => OpenPinAsync(pin), "folder_20_regular.png"));
        PinnedFolders.Add(ClientControls.NavigationRow("Manage pins…", ManagePinsAsync));
    }
    private async Task ManagePinsAsync()
    {
        if (clientState is null) return;
        var pins = (await clientState.Workspace.ReadAsync()).Pins;
        var chosen = await SlateDialogs.ChooseAsync(this, "Pinned folders", "Cancel", null, pins.Select(x => x.Path).ToArray());
        if (pins.FirstOrDefault(x => x.Path == chosen) is { } pin)
        {
            var action = await SlateDialogs.ChooseAsync(this, pin.Path, "Cancel", null, "Open", "Repin", "Remove pin");
            if (action == "Open") await OpenPinAsync(pin); else await RecoverPinAsync(pin, action);
        }
    }
    private async Task OpenPinAsync(FolderPin pin)
    {
        try
        {
            var details = await api.DetailsAsync(pin.Path, connection.Token);
            if (!details.IsDirectory) throw new HttpRequestException("The pinned folder is missing.", null, HttpStatusCode.NotFound);
            if (!leftSidebarOpen) ToggleLeftClicked(this, EventArgs.Empty);
            await ReloadTreeAsync(pin.Path); var row = rows.FirstOrDefault(x => x.Entry.Path == pin.Path);
            if (row is not null) { if (!row.Loaded) await LoadChildren(row, 0, connection.Token); row.Expanded = true; RebuildRows(pin.Path); Tree.ScrollTo(row); }
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            var action = await SlateDialogs.ChooseAsync(this, "Missing pin · " + pin.Label, "Cancel", null, "Locate", "Repin", "Remove pin");
            await RecoverPinAsync(pin, action);
        }
    }
    private async Task RecoverPinAsync(FolderPin pin, string action)
    {
        if (clientState is null) return;
        if (action == "Remove pin") await clientState.Workspace.RemovePinAsync(pin.Path);
        else if (action is "Locate" or "Repin")
        {
            // Location is explicitly chosen; never guess an external move from a similar folder name.
            var folder = await SlateDialogs.PromptAsync(this, action + " folder", "Current folder path in Library", initialValue: pin.Path);
            if (string.IsNullOrWhiteSpace(folder)) return;
            var details = await api.DetailsAsync(folder, connection.Token); if (!details.IsDirectory) return;
            await clientState.Workspace.RepinAsync(pin.Path, folder);
        }
        await RefreshPinnedFoldersAsync();
    }
    private async void ContextPin(object? sender, EventArgs e)
    {
        try { var row = RowFrom(sender); if (row.Entry.IsDirectory) await ToggleFolderPinAsync(row.Entry.Path); }
        catch (Exception exception) { ShowOperationError(exception, "The folder pin could not be changed."); }
    }
}

public sealed class MobileFavoritesPage : MobilePage
{
    private readonly VerticalStackLayout items = new() { Spacing = 2, Padding = 12 };
    public MobileFavoritesPage(MobileSession session) : base(session, "Favorites") => Content = new ScrollView { Content = items };
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            items.Clear(); if (Session.State is null) Session.LoadKnownLocalState(); if (Session.State is null) return;
            var favorites = (await Session.State.Workspace.ReadAsync()).Favorites;
            foreach (var favorite in favorites) items.Add(ClientControls.NavigationRow(favorite.Title, async () =>
            {
                try { await Session.ReadAsync(favorite.Id); await Navigation.PushAsync(new MobileNotePage(Session, favorite.Id)); }
                catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
                { if (await SlateDialogs.AlertAsync(this, "Note missing", "Remove this favorite?", "Remove", "Keep")) { await Session.State.Workspace.RemoveFavoriteAsync(favorite.Id); OnAppearing(); } }
            }, "bookmark.png"));
            if (favorites.Count == 0) items.Add(MobileTheme.Label("Favorite a note from its menu.", 14, MobileTheme.Secondary));
        }
        catch (Exception exception) { await ShowErrorAsync(exception, "Favorites could not be opened."); }
    }
}

public sealed partial class MobileFolderPage
{
    private readonly VerticalStackLayout pinnedFolders = new() { Spacing = 2 };
    private async Task RefreshPinsAsync()
    {
        pinnedFolders.Clear(); var localState = Session.State; if (path.Length != 0 || localState is null) return;
        var pins = (await localState.Workspace.ReadAsync()).Pins; if (pins.Count == 0) return;
        pinnedFolders.Add(MobileTheme.Section("Pinned folders"));
        foreach (var pin in pins.Take(3)) pinnedFolders.Add(ClientControls.NavigationRow(pin.Label, () => OpenPinAsync(pin), "folder_20_regular.png"));
        if (pins.Count > 3) pinnedFolders.Add(ClientControls.NavigationRow("All pins…", async () =>
        {
            var path = await SlateDialogs.ChooseAsync(this, "Pinned folders", "Cancel", null, pins.Select(x => x.Path).ToArray());
            if (pins.FirstOrDefault(x => x.Path == path) is { } pin) await OpenPinAsync(pin);
        }));
    }
    private async Task OpenPinAsync(FolderPin pin)
    {
        if (Session.State is null) return;
        var missing = false;
        try { var details = await Session.Api.DetailsAsync(pin.Path, default); missing = !details.IsDirectory; }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound) { missing = true; }
        var action = await SlateDialogs.ChooseAsync(this, (missing ? "Missing pin · " : "") + pin.Label, "Cancel", null,
            missing ? ["Locate", "Repin", "Remove pin"] : ["Open", "Repin", "Remove pin"]);
        if (action == "Open") await Navigation.PushAsync(new MobileFolderPage(Session, pin.Path, pin.Label));
        else if (action == "Remove pin") await Session.State.Workspace.RemovePinAsync(pin.Path);
        else if (action is "Locate" or "Repin")
        { var folder = await MobileFolderPickerPage.PickAsync(Navigation, Session); if (!string.IsNullOrEmpty(folder)) await Session.State.Workspace.RepinAsync(pin.Path, folder); }
        await RefreshPinsAsync();
    }
}

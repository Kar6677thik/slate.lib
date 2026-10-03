using System.Collections.ObjectModel;
using System.Net;
using Slate.Lib.Core;

namespace Slate.Lib.App;

public sealed class MobileRootPage : TabbedPage
{
    private readonly MobileSession session;
    private bool initialized;
    private readonly SemaphoreSlim shareGate = new(1, 1);

    public MobileRootPage(MobileSession session)
    {
        this.session = session;
        Title = "Slate";
        BackgroundColor = MobileTheme.Canvas;
        BarBackgroundColor = MobileTheme.Surface;
        BarTextColor = MobileTheme.Secondary;
        SelectedTabColor = MobileTheme.Accent;
        UnselectedTabColor = MobileTheme.Muted;
#if ANDROID
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.TabbedPage.SetToolbarPlacement(this,
            Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.ToolbarPlacement.Bottom);
#endif
        Children.Add(Tab(new MobileFolderPage(session, "", "Library"), "Library", "folder_open_20_regular.png"));
        Children.Add(Tab(new MobileSearchPage(session), "Search", "search_20_regular.png"));
        Children.Add(Tab(new MobileFolderPage(session, "inbox", "Inbox", ensureFolder: true), "Inbox", "mail_inbox_20_regular.png"));
        Children.Add(Tab(new MobileRecentPage(session), "Recent", "history_20_regular.png"));
        ShareSignals.Received += ShareReceived;
    }

    private async void ShareReceived(object? sender, EventArgs e)
    {
        try { await PresentSharesAsync(); }
        catch (Exception) { await SlateDialogs.AlertAsync(this, "Share saved", "The share remains on this device. Reopen Slate to resume it.", "OK"); }
    }
    private async Task PresentSharesAsync()
    {
        await shareGate.WaitAsync();
        try
        {
            if (session.State is null) session.LoadKnownLocalState(); if (session.State is null) return;
            IncomingShare? first = null;
            foreach (var share in await IncomingShareStore.TakeAllAsync())
            {
                if (!share.Complete && share.Error is null) continue;
                await IncomingShareStore.PromoteAsync(share, session.State); first ??= share;
            }
            // Additional captures remain in Recent drafts, instead of stacking many pages.
            if (first is not null) await CurrentPage.Navigation.PushAsync(new MobileCapturePage(session, first));
        }
        finally { shareGate.Release(); }
    }

    private static NavigationPage Tab(Page page, string title, string icon) => new(page)
    {
        Title = title,
        IconImageSource = icon,
        BarBackgroundColor = MobileTheme.Surface,
        BarTextColor = MobileTheme.Primary
    };

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!initialized)
        {
            initialized = true;
            session.LoadKnownLocalState();
            try { await session.ConnectAsync(); _ = UpdateCoordinator.CheckAsync(this, session.Api, false); }
            catch { await SlateDialogs.AlertAsync(this, "Connect Slate", "Open Settings to enter your server URL and device token.", "OK"); }
        }
        if (session.State is not null) await PresentSharesAsync();
    }
}

public abstract class MobilePage : ContentPage
{
    protected override bool OnBackButtonPressed() => SlateDialogs.TryDismiss(this) || base.OnBackButtonPressed();
    protected MobileSession Session { get; }

    protected MobilePage(MobileSession session, string title)
    {
        Session = session; Title = title;
        MobileTheme.Apply(this);
        ToolbarItems.Add(new ToolbarItem("Capture", "flash_20_regular.png", async () => await Navigation.PushAsync(new MobileCapturePage(session))));
        ToolbarItems.Add(new ToolbarItem("Commands", "command.png", async () => await ShowCommandsAsync()));
        ToolbarItems.Add(new ToolbarItem("Settings", "settings_20_regular.png", async () => await Navigation.PushAsync(new MobileSettingsPage(session))));
    }

    protected async Task ShowCommandsAsync()
    {
        var action = await SlateDialogs.ChooseAsync(this, "Commands", "Cancel", null, "Search", "Rediscover", "Offline work", "Smart views", "Attachments", "Check links", "New note", "Question", "Add Link", "Daily note", "Quick Thought", "Favorites", "Recent notes", "Recover deleted notes", "Sync", "Review sync conflict", "Settings");
        try
        {
            switch (action)
            {
                case "Search": await Navigation.PushAsync(new MobileSearchPage(Session)); break;
                case "Offline work":
                    Session.LoadKnownLocalState();
                    if (Session.State is not null && await OfflineActions.OpenAsync(this, Session.State, Session.Api) is { } downloadedNote) await Navigation.PushAsync(new MobileNotePage(Session, downloadedNote)); break;
                case "Attachments":
                    await Session.EnsureConnectedAsync();
                    if (await AttachmentBrowser.OpenAsync(this, Session.Api) is { } assetNote) await Navigation.PushAsync(new MobileNotePage(Session, assetNote)); break;
                case "New note": await Navigation.PushAsync(new MobileNewNotePage(Session, "")); break;
                case "Daily note":
                    await Session.EnsureConnectedAsync();
                    var note = await WorkspaceNotes.DailyAsync(Session.Api);
                    await Navigation.PushAsync(new MobileNotePage(Session, note.Id)); break;
                case "Quick Thought": await Navigation.PushAsync(new MobileCapturePage(Session)); break;
                case "Recent notes": await Navigation.PushAsync(new MobileRecentPage(Session)); break;
                case "Favorites": await Navigation.PushAsync(new MobileFavoritesPage(Session)); break;
                case "Smart views":
                    await Session.EnsureConnectedAsync();
                    if (Session.State is not null && await SmartViewBrowser.OpenAsync(this, Session.Api, Session.State.Workspace) is { } hit)
                        await Navigation.PushAsync(new MobileNotePage(Session, hit.Id)); break;
                case "Question": await Navigation.PushAsync(new MobileKnowledgePage(Session, "question")); break;
                case "Check links":
                    await Session.EnsureConnectedAsync();
                    if (await LinkManagementViews.IssuesAsync(this, Session.Api) is { } repaired) await Navigation.PushAsync(new MobileNotePage(Session, repaired.Id)); break;
                case "Add Link": await Navigation.PushAsync(new MobileKnowledgePage(Session, "link")); break;
                case "Sync":
                    await Session.EnsureConnectedAsync();
                    var result = await Session.Api.SyncAsync(default);
                    await SlateDialogs.AlertAsync(this, "Sync", result.Detail ?? result.State, "OK"); break;
                case "Settings": await Navigation.PushAsync(new MobileSettingsPage(Session)); break;
                case "Review sync conflict": await MergeWorkbench.ReviewSyncAsync(this, Session.Api); break;
                case "Rediscover":
                    if (await RediscoveryBrowser.OpenAsync(this, Session.Api, Session.State) is { } rediscovered) await Navigation.PushAsync(new MobileNotePage(Session, rediscovered)); break;
                case "Recover deleted notes":
                    if (await HistoryBrowser.RecoverAsync(this, Session.Api) is { } recovered) await Navigation.PushAsync(new MobileNotePage(Session, recovered.Id)); break;
            }
        }
        catch (Exception exception) { await ShowErrorAsync(exception, "This action could not be completed."); }
    }

    protected async Task ShowErrorAsync(Exception exception, string fallback)
    {
        var text = exception is HttpRequestException ? exception.Message : fallback;
        await SlateDialogs.AlertAsync(this, "Slate needs attention", text, "OK");
    }

    protected async Task InsertWikiLinkAsync(Editor editor)
    {
        var query = await SlateDialogs.PromptAsync(this, "Insert wiki link", "Search by note title", placeholder: "PostgreSQL");
        if (string.IsNullOrWhiteSpace(query)) return;
        try
        {
            await Session.EnsureConnectedAsync();
            var hits = (await Session.Api.SearchAsync(query, 0, default)).Results.Take(8).ToArray();
            if (hits.Length == 0) { await SlateDialogs.AlertAsync(this, "No matches", "No note matched that title.", "OK"); return; }
            var labels = hits.Select(x => x.Title + " — " + x.Path).ToArray();
            var selected = await SlateDialogs.ChooseAsync(this, "Choose a note", "Cancel", null, labels);
            var index = Array.IndexOf(labels, selected); if (index < 0) return;
            var markdown = $"[[id:{hits[index].Id:D}|{hits[index].Title.Replace("]", "", StringComparison.Ordinal)}]]";
            var source = editor.Text ?? ""; var position = Math.Clamp(editor.CursorPosition, 0, source.Length);
            editor.Text = source.Insert(position, markdown); editor.CursorPosition = position + markdown.Length;
        }
        catch (Exception exception) { await ShowErrorAsync(exception, "Links could not be searched."); }
    }
}

public sealed partial class MobileFolderPage : MobilePage
{
    private readonly string path;
    private readonly bool ensureFolder;
    private readonly ObservableCollection<LibraryEntry> entries = [];
    private readonly CollectionView list;
    private readonly Label state = new() { Margin = new Thickness(2, 8), FontSize = 12, TextColor = MobileTheme.Secondary };
    private readonly Label connection = new() { Text = "○  CONNECTING", FontSize = 12, TextColor = MobileTheme.Secondary, FontAttributes = FontAttributes.Bold };
    private readonly RefreshView refresh;
    private readonly HashSet<string> expandedFolders = new(StringComparer.Ordinal);
    private bool expandingFolder;

    public MobileFolderPage(MobileSession session, string path, string title, bool ensureFolder = false) : base(session, title)
    {
        this.path = path; this.ensureFolder = ensureFolder;
        var isRoot = path.Length == 0;
        var breadcrumb = MobileTheme.Label(path.Length == 0 ? "Files" : "Library  /  " + path.Replace("/", "  /  "), 13, MobileTheme.Secondary);
        breadcrumb.VerticalOptions = LayoutOptions.Center;
        list = new CollectionView { ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepScrollOffset, ItemsSource = entries, SelectionMode = SelectionMode.Single, BackgroundColor = Colors.Transparent };
        list.SelectionChanged += EntrySelected;
                list.EmptyView = new VerticalStackLayout
        {
            Padding = 24, Spacing = 10, VerticalOptions = LayoutOptions.Center,
            Children = { new Image { Source = "mail_inbox_20_regular.png", WidthRequest = 28, HeightRequest = 28 },
                new Label { Text = title == "Inbox" ? "Inbox is clear" : "This folder is empty", FontSize = 16,
                    TextColor = MobileTheme.Primary, HorizontalTextAlignment = TextAlignment.Center },
                new Label { Text = "Create a note or capture a thought to get started.", FontSize = 13,
                    TextColor = MobileTheme.Secondary, HorizontalTextAlignment = TextAlignment.Center } }
        };
        list.ItemTemplate = new DataTemplate(() =>
        {
            var icon = new Image { WidthRequest = 20, HeightRequest = 20, VerticalOptions = LayoutOptions.Center };
            icon.SetBinding(Image.SourceProperty, new Binding(nameof(LibraryEntry.IsDirectory), converter: new LibraryEntryIconConverter()));
            var name = new Label { FontSize = 15, TextColor = MobileTheme.Primary, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation };
            name.SetBinding(Label.TextProperty, nameof(LibraryEntry.Name));
            var pathLabel = new Label { FontFamily = "monospace", FontSize = 10, TextColor = MobileTheme.Muted, LineBreakMode = LineBreakMode.TailTruncation };
            pathLabel.SetBinding(Label.TextProperty, nameof(LibraryEntry.Path));
            var text = new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center, Children = { name } };
            var actions = MobileTheme.IconButton("more_horizontal_20_regular.png", "More actions", false, 38);
            actions.BackgroundColor = Colors.Transparent; actions.BorderWidth = 0;
            actions.SetBinding(BindableObject.BindingContextProperty, ".");
            actions.Clicked += async (sender, _) => { if (((ImageButton)sender!).BindingContext is LibraryEntry item) await ShowActionsAsync(item); };
            var chevron = new Image { WidthRequest = 12, HeightRequest = 12, VerticalOptions = LayoutOptions.Center };
            var grid = new Grid { HeightRequest = 56, Padding = new Thickness(12, 4), ColumnDefinitions = { new(12), new(20), new(GridLength.Star), new(44) }, ColumnSpacing = 9 };
            grid.Add(chevron); grid.Add(icon, 1); grid.Add(text, 2); grid.Add(actions, 3);
            InstallSelectionGesture(grid);
            grid.BindingContextChanged += (_, _) =>
            {
                if (grid.BindingContext is not LibraryEntry item) return;
                grid.BackgroundColor = mobileSelection.Paths.Contains(item.Path) ? MobileTheme.Raised : Colors.Transparent;
                var depth = Math.Max(0, item.Path.Count(c => c == '/') - (path.Length == 0 ? 0 : path.Count(c => c == '/') + 1));
                grid.Padding = new Thickness(12 + depth * 18, 4, 6, 4);
                chevron.IsVisible = item.IsDirectory;
                if (item.IsDirectory)
                {
                    chevron.Source = expandedFolders.Contains(item.Path) ? "chevron_down_16_regular.png" : "chevron_right_16_regular.png";
                    icon.Source = expandedFolders.Contains(item.Path) ? "folder_open_20_regular.png" : "folder_20_regular.png";
                }
                name.Text = item.Title ?? Path.GetFileNameWithoutExtension(item.Name);
            };
            return grid;
        });
        refresh = new RefreshView { Content = list, Command = new Command(async () => await LoadAsync()) };
        var header = isRoot ? BuildVaultHeader() : BuildFolderActions();
        var section = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, Margin = new Thickness(2, 4, 2, 4) };
        section.Add(breadcrumb);
        var collapse = MobileTheme.IconButton("collapse.png", "Collapse all folders", size: 44);
        collapse.Clicked += async (_, _) => await LoadAsync(); section.Add(collapse, 1);
        var content = new Grid { Padding = new Thickness(16, 14, 16, 12), RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        content.Add(header); content.Add(BuildSelectionBar(), 0, 1); content.Add(section, 0, 2); content.Add(refresh, 0, 3); content.Add(state, 0, 4);
        var fab = MobileTheme.IconButton("add_20_regular.png", "Create", true, 58);
        fab.Margin = new Thickness(0, 0, 6, 18); fab.HorizontalOptions = LayoutOptions.End; fab.VerticalOptions = LayoutOptions.End;
        fab.Clicked += async (_, _) => await ShowCreateMenuAsync();
        var shell = new Grid(); shell.Add(content); shell.Add(fab);
        Content = shell;
    }

    private View BuildVaultHeader()
    {
        var note = MobileTheme.IconButton("edit_20_regular.png", "New note", size: 44);
        note.Clicked += async (_, _) => await Navigation.PushAsync(new MobileNewNotePage(Session, path));
        var folder = MobileTheme.IconButton("folder_add.png", "New folder", size: 44);
        folder.Clicked += async (_, _) => await CreateFolderAsync();
        var daily = MobileTheme.IconButton("calendar.png", "Daily note", size: 44);
        daily.Clicked += async (_, _) =>
        {
            try { await Session.EnsureConnectedAsync(); var item = await WorkspaceNotes.DailyAsync(Session.Api); await Navigation.PushAsync(new MobileNotePage(Session, item.Id)); }
            catch (Exception exception) { await ShowErrorAsync(exception, "The daily note could not be opened."); }
        };
        var bookmarks = MobileTheme.IconButton("bookmark.png", "Favorites", size: 44);
        bookmarks.Clicked += async (_, _) =>
        {
            await Navigation.PushAsync(new MobileFavoritesPage(Session));
        };
        var tools = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        var buttons = new[] { note, folder, daily, bookmarks };
        var captions = new[] { "Note", "Folder", "Daily", "Saved" };
        for (var i = 0; i < buttons.Length; i++)
        {
            buttons[i].HorizontalOptions = LayoutOptions.Center;
            var label = MobileTheme.Label(captions[i], 11, MobileTheme.Secondary); label.HorizontalTextAlignment = TextAlignment.Center;
            tools.Add(MobileTheme.Frame(new VerticalStackLayout { Spacing = 2, Children = { buttons[i], label } }, new Thickness(4, 6), 14), i);
        }
        return new VerticalStackLayout { Spacing = 4, Children = { tools, pinnedFolders } };
    }

    private View BuildFolderActions()
    {
        var newNote = MobileTheme.IconButton("edit_20_regular.png", "New note");
        newNote.Clicked += async (_, _) => await Navigation.PushAsync(new MobileNewNotePage(Session, path));
        var newFolder = MobileTheme.IconButton("folder_add.png", "New folder");
        newFolder.Clicked += async (_, _) => await CreateFolderAsync();
        return new HorizontalStackLayout { Spacing = 10, Children = { newNote, newFolder } };
    }
    private async Task ShowCreateMenuAsync()
    {
        var action = await SlateDialogs.ChooseAsync(this, "Create in Slate", "Cancel", null, "New note", "New folder", "Quick Thought", "Question", "Add Link");
        if (action == "New note") await Navigation.PushAsync(new MobileNewNotePage(Session, path));
        else if (action == "New folder") await CreateFolderAsync();
        else if (action == "Quick Thought") await Navigation.PushAsync(new MobileCapturePage(Session));
        else if (action == "Question") await Navigation.PushAsync(new MobileKnowledgePage(Session, "question", path));
        else if (action == "Add Link") await Navigation.PushAsync(new MobileKnowledgePage(Session, "link", path));
    }

    protected override async void OnAppearing() { base.OnAppearing(); await LoadAsync(); }

    private async Task LoadAsync()
    {
        try
        {
            await Session.EnsureConnectedAsync();
            if (ensureFolder)
            {
                try { await Session.Api.CreateFolderAsync(new("", path), default); }
                catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Conflict) { }
            }
            entries.Clear(); expandedFolders.Clear();
            var page = 0;
            do
            {
                var result = await Session.Api.ListAsync(path, page, default);
                foreach (var entry in result.Entries) entries.Add(entry);
                if (result.NextPage is null) break; page = result.NextPage.Value;
            } while (true);
            state.Text = $"{entries.Count} item{(entries.Count == 1 ? "" : "s")}";
            await RefreshPinsAsync();
            connection.Text = "●  LIVE"; connection.TextColor = MobileTheme.Success;
        }
        catch (Exception exception) { state.Text = "Server unavailable"; connection.Text = "○  OFFLINE"; connection.TextColor = MobileTheme.Muted; if (entries.Count == 0) await ShowErrorAsync(exception, "Unable to load this folder."); }
        finally { mobileSelection.Retain(entries.Select(x => x.Path)); RefreshSelected(); refresh.IsRefreshing = false; }
    }

    private async void EntrySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not LibraryEntry item) return;
        list.SelectedItem = null;
        if (mobileSelection.Count > 0) { ToggleSelected(item); return; }
        if (item.IsDirectory) await ToggleFolderAsync(item);
        else if (item.Id is { } id) await Navigation.PushAsync(new MobileNotePage(Session, id));
    }

    private async Task ToggleFolderAsync(LibraryEntry item)
    {
        if (expandingFolder) return;
        expandingFolder = true;
        try
        {
            var index = entries.IndexOf(item);
            if (expandedFolders.Contains(item.Path))
            {

                for (var i = entries.Count - 1; i > index; i--)
                    if (entries[i].Path.StartsWith(item.Path + "/", StringComparison.Ordinal)) entries.RemoveAt(i);
                expandedFolders.RemoveWhere(x => x == item.Path || x.StartsWith(item.Path + "/", StringComparison.Ordinal));
            }
            else
            {
                var children = new List<LibraryEntry>();
                var page = 0;
                do
                {
                    var result = await Session.Api.ListAsync(item.Path, page, default);
                    children.AddRange(result.Entries);
                    if (result.NextPage is null) break;
                    page = result.NextPage.Value;
                } while (true);

                expandedFolders.Add(item.Path);
                foreach (var child in children) entries.Insert(++index, child);
            }
            var refreshedIndex = entries.IndexOf(item); if (refreshedIndex >= 0) entries[refreshedIndex] = item with { };
            state.Text = $"{entries.Count} visible items";
        }
        catch (Exception exception) { list.Opacity = 1; await ShowErrorAsync(exception, "This folder could not be opened."); }
        finally { expandingFolder = false; }
    }

    private async Task ShowActionsAsync(LibraryEntry item)
    {
        if (mobileSelection.Count > 0) { ToggleSelected(item); return; }
        var actions = new List<string> { "Select", "Open", "Rename", "Move", "Copy", "Duplicate" };
        actions.Add(item.IsDirectory ? "Pin / unpin folder" : "Favorite / unfavorite");
        var action = await SlateDialogs.ChooseAsync(this, item.Title ?? item.Name, "Cancel", item.IsDirectory ? "Delete folder" : "Delete note", actions.ToArray());
        try
        {
            switch (action)
            {
                case "Select": ToggleSelected(item); return;
                case "Pin / unpin folder": if (Session.State is not null) await Session.State.Workspace.TogglePinAsync(item.Path); break;
                case "Favorite / unfavorite": if (Session.State is not null && item.Id is { } favoriteId) await Session.State.Workspace.ToggleFavoriteAsync(favoriteId, item.Title ?? item.Name); break;
                case "Open":
                    if (item.IsDirectory) await Navigation.PushAsync(new MobileFolderPage(Session, item.Path, item.Name));
                    else await Navigation.PushAsync(new MobileNotePage(Session, item.Id!.Value));
                    return;
                case "Rename":
                    var name = await SlateDialogs.PromptAsync(this, "Rename", "New name", initialValue: item.Name);
                    if (!string.IsNullOrWhiteSpace(name))
                    { var renamed = await LinkManagementViews.MoveAsync(this, Session.Api, item.Path, "", name); if (renamed is not null && Session.State is not null) await Session.State.Workspace.RemapFoldersAsync(renamed.Items); }
                    break;
                case "Move":
                case "Copy":
                    var destination = await MobileFolderPickerPage.PickAsync(Navigation, Session);
                    if (destination is null) return;
                    if (action == "Move") { var moved = await LinkManagementViews.MoveAsync(this, Session.Api, item.Path, destination); if (moved is not null && Session.State is not null) await Session.State.Workspace.RemapFoldersAsync(moved.Items); }
                    else await Session.Api.CopyAsync(new(item.Path, destination), default);
                    break;
                case "Duplicate":
                    var parent = item.Path.Contains('/') ? item.Path[..item.Path.LastIndexOf('/')] : "";
                    await Session.Api.CopyAsync(new(item.Path, parent), default);
                    break;
                case "Delete note": case "Delete folder":
                    var details = await Session.Api.DetailsAsync(item.Path, default);
                    if (await SlateDialogs.AlertAsync(this, "Delete", $"Delete {item.Name}" + (details.DescendantCount > 0 ? $" and {details.DescendantCount} descendant item(s)" : "") + "?", "Delete", "Cancel"))
                        await Session.Api.DeleteAsync(new(item.Path, item.IsDirectory), default);
                    break;
                default: return;
            }
            await LoadAsync();
        }
        catch (Exception exception) { await ShowErrorAsync(exception, "The item could not be changed."); }
    }

    private async Task CreateFolderAsync()
    {
        var name = await SlateDialogs.PromptAsync(this, "New folder", "Folder name", placeholder: "New folder");
        if (string.IsNullOrWhiteSpace(name)) return;
        try { await Session.Api.CreateFolderAsync(new(path, name), default); await LoadAsync(); }
        catch (Exception exception) { await ShowErrorAsync(exception, "The folder could not be created."); }
    }
}

public sealed class MobileFolderPickerPage : ContentPage
{
    private readonly MobileSession session;
    private readonly TaskCompletionSource<string?> completion;
    private string path = "";
    private readonly ObservableCollection<LibraryEntry> folders = [];
    private readonly Label breadcrumb = new();

    private MobileFolderPickerPage(MobileSession session, TaskCompletionSource<string?> completion)
    {
        this.session = session; this.completion = completion; Title = "Choose folder";
        MobileTheme.Apply(this);
        breadcrumb.TextColor = MobileTheme.Secondary; breadcrumb.FontFamily = "monospace"; breadcrumb.FontSize = 11;
        ToolbarItems.Add(new ToolbarItem("Up", "arrow_left_20_regular.png", async () => { if (path.Length == 0) return; path = path.Contains('/') ? path[..path.LastIndexOf('/')] : ""; await LoadAsync(); }));
        var choose = MobileTheme.Button("Choose folder", null, true, true); choose.Clicked += async (_, _) => { completion.TrySetResult(path); await Navigation.PopModalAsync(); };
        var cancel = MobileTheme.Button("Cancel", null, false, true); cancel.Clicked += async (_, _) => { completion.TrySetResult(null); await Navigation.PopModalAsync(); };
        var list = new CollectionView { ItemsSource = folders, BackgroundColor = Colors.Transparent, ItemTemplate = new DataTemplate(() => { var label = MobileTheme.Label("", 15); label.Padding = 14; label.SetBinding(Label.TextProperty, nameof(LibraryEntry.Name)); return label; }), SelectionMode = SelectionMode.Single };
        list.SelectionChanged += async (_, e) => { if (e.CurrentSelection.FirstOrDefault() is LibraryEntry folder) { path = folder.Path; list.SelectedItem = null; await LoadAsync(); } };
        var newFolder = MobileTheme.Button("New folder", "folder_20_regular.png", false, true); newFolder.Clicked += async (_, _) => { var name = await SlateDialogs.PromptAsync(this, "New folder", "Name"); if (!string.IsNullOrWhiteSpace(name)) { await session.Api.CreateFolderAsync(new(path, name), default); await LoadAsync(); } };
        Content = new Grid { Padding = 16, RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) }, RowSpacing = 12, Children = { breadcrumb } };
        var buttons = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
        buttons.Add(choose); buttons.Add(newFolder, 1); buttons.Add(cancel, 2);
        ((Grid)Content).Add(buttons, 0, 1); ((Grid)Content).Add(MobileTheme.Frame(list), 0, 2);
    }

    protected override void OnDisappearing() { completion.TrySetResult(null); base.OnDisappearing(); }

    public static async Task<string?> PickAsync(INavigation navigation, MobileSession session)
    {
        var completion = new TaskCompletionSource<string?>();
        var page = new MobileFolderPickerPage(session, completion);
        await navigation.PushModalAsync(new NavigationPage(page));
        await page.LoadAsync();
        return await completion.Task;
    }

    private async Task LoadAsync()
    {
        breadcrumb.Text = path.Length == 0 ? "Library" : "Library  ›  " + path.Replace("/", "  ›  ");
        folders.Clear();
        var result = await session.Api.ListAsync(path, 0, default);
        foreach (var folder in result.Entries.Where(x => x.IsDirectory)) folders.Add(folder);
    }
}

public sealed partial class MobileNotePage : MobilePage
{
    private readonly Guid noteId;
    private readonly Editor editor = new() { AutoSize = EditorAutoSizeOption.Disabled, FontFamily = "monospace", FontSize = 15, Placeholder = "Write Markdown…", Margin = 10 };
    private readonly WebView reader = new() { BackgroundColor = MobileTheme.Canvas };
    private readonly Label status = new() { FontSize = 12, TextColor = MobileTheme.Secondary, VerticalOptions = LayoutOptions.Center };
    private readonly ImageButton toggle = MobileTheme.IconButton("edit_20_regular.png", "Edit note");
    private readonly View markdownTools;
    private LibraryNote? note;
    private DraftDebouncer? drafts;
    private CancellationTokenSource? autosave;
    private bool editing;
    private bool settingText;
    private bool dirty;
    private bool offline;
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private readonly SemaphoreSlim previewGate = new(1, 1);
    private int previewGeneration;
    private string? renderedPreviewKey;
    private readonly List<PendingAsset> pendingAssets = [];
    private string? pendingHeading;

    public MobileNotePage(MobileSession session, Guid noteId, string? heading = null) : base(session, "Note")
    {
        this.noteId = noteId; pendingHeading = heading;
        MobileTheme.Input(editor);
        var save = MobileTheme.IconButton("save_20_regular.png", "Save note"); save.Clicked += async (_, _) => await SaveAsync();
        toggle.Clicked += async (_, _) => await ToggleAsync();
        var image = MobileTheme.IconButton("image_20_regular.png", "Insert image", false, 38); image.Clicked += async (_, _) => await PickAssetAsync(true);
        var file = MobileTheme.IconButton("attach_20_regular.png", "Attach file", false, 38); file.Clicked += async (_, _) => await PickAssetAsync(false);
        var link = MobileTheme.IconButton("link_20_regular.png", "Insert wiki link", false, 38);
        link.Clicked += async (_, _) =>
        {
            var before = editor.Text;
            await InsertWikiLinkAsync(editor);
            if (!editing && editor.Text != before) await ToggleAsync();
        };
        var actions = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 7 };
        actions.Add(image); actions.Add(file, 1); actions.Add(link, 2); actions.Add(toggle, 4); actions.Add(save, 5);
        markdownTools = MarkdownToolbar.Create(editor, () => PickAssetAsync(true), () => PickAssetAsync(false), () => InsertWikiLinkAsync(editor));
        markdownTools.IsVisible = false;
        var bar = new Grid { Padding = new Thickness(0, 0, 0, 10), RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) }, RowSpacing = 4, Children = { status } };
        bar.Add(actions, 0, 1);
        bar.Add(markdownTools, 0, 2);
        editor.TextChanged += EditorChanged;
        reader.Navigating += ReaderNavigating;
        var body = new Grid(); body.Add(reader); body.Add(editor); editor.IsVisible = false;
        Content = new Grid { Padding = new Thickness(12, 10, 12, 14), RowSpacing = 10, RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, Children = { MobileTheme.Frame(bar, new Thickness(12), 14) } };
        ((Grid)Content).Add(MobileTheme.Frame(body, new Thickness(0), 14), 0, 1);
        InitializeWorkspace();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (note is not null) return;
        try
        {
            var result = await Session.ReadAsync(noteId);
            note = result.Note; offline = result.Offline; Title = note.Title;
            settingText = true; editor.Text = note.Markdown; settingText = false;
            if (Session.State is not null)
            {
                drafts = new DraftDebouncer(Session.State);
                if (await Session.State.ReadDraftAsync(note.Id) is { } draft)
                {
                    if (draft.Markdown == note.Markdown) await Session.State.DeleteDraftAsync(draft.DraftId);
                    else if (await SlateDialogs.AlertAsync(this, "Unsaved changes recovered", "Restore the local draft?", "Restore Draft", "Discard"))
                    {
                        settingText = true; editor.Text = draft.Markdown; settingText = false; dirty = true;
                        pendingAssets.AddRange(draft.PendingAssets ?? []);
                        if (!string.IsNullOrWhiteSpace(draft.BaseRevision)) note = note with { Revision = draft.BaseRevision, Markdown = draft.BaseMarkdown ?? note.Markdown };
                    }
                    else await Session.State.DeleteDraftAsync(draft.DraftId);
                }
            }
            Render(); UpdateState();
            await RediscoveryBrowser.RecordAsync(Session.State, note.Id);
        }
        catch (Exception exception) { await ShowErrorAsync(exception, "This note could not be opened."); await Navigation.PopAsync(); }
    }

    protected override void OnDisappearing()
    {
        if (dirty && drafts is not null) _ = drafts.FlushAsync();
        base.OnDisappearing();
    }

    private void EditorChanged(object? sender, TextChangedEventArgs e)
    {
        if (settingText || note is null) return;
        if (!dirty && NoteDocument.NormalizeLineEndings(e.NewTextValue ?? "") == NoteDocument.NormalizeLineEndings(note.Markdown)) return;
        dirty = true; UpdateState();
        drafts?.Schedule(new(note.Id, DraftKind.ExistingNote, e.NewTextValue ?? "", DateTimeOffset.UtcNow, note.Id, note.Path, note.Revision, note.Markdown, PendingAssets: pendingAssets.ToArray()));
        autosave?.Cancel(); autosave?.Dispose(); autosave = new(); _ = AutosaveAsync(autosave.Token);
    }

    private async Task AutosaveAsync(CancellationToken token)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(2), token); if (!offline && dirty) await SaveAsync(); }
        catch (OperationCanceledException) { }
    }

    private async Task SaveAsync()
    {
        await saveGate.WaitAsync();
        try { await SaveCoreAsync(); }
        finally { saveGate.Release(); }
    }

    private async Task SaveCoreAsync()
    {
        if (note is null || !dirty) return;
        status.Text = "Saving…";
        try
        {
            var sent = editor.Text ?? "";
            foreach (var asset in Session.State is null ? pendingAssets : [])
            {
                var metadata = await PendingAssetStore.UploadAsync(Session.Api, asset);
                sent = sent.Replace($"asset-pending://{asset.Id:D}", AssetReferences.PathForNote(note.Path, asset.Id, metadata.Extension), StringComparison.Ordinal);
            }
            if (sent != editor.Text) { settingText = true; editor.Text = sent; settingText = false; }
            var saved = Session.State is null ? await Session.Api.UpdateNoteAsync(note.Id, new(sent, note.Revision), default)
                : await OfflineActions.EditAsync(Session.State, Session.Api, note, sent, pendingAssets.ToArray());
            if (sent == editor.Text) { settingText = true; editor.Text = saved.Markdown; settingText = false; sent = saved.Markdown; }
            note = saved; offline = false;
            if ((editor.Text ?? "") == sent)
            {
                dirty = false; drafts?.Clear();
                if (Session.State is not null) await Session.State.DeleteDraftAsync(note.Id);
                foreach (var asset in pendingAssets) PendingAssetStore.Delete(asset);
                pendingAssets.Clear();
            }
            else
            {
                dirty = true; drafts?.Schedule(new(note.Id, DraftKind.ExistingNote, editor.Text ?? "", DateTimeOffset.UtcNow, note.Id, note.Path, note.Revision, note.Markdown, PendingAssets: pendingAssets.ToArray()));
            }
            if (Session.State is not null) await Session.State.CacheNoteAsync(note);
            Render(); UpdateState();
            await RediscoveryBrowser.RecordAsync(Session.State, note.Id);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            status.Text = "Needs attention";
            try
            {
                var merged = await MergeWorkbench.OpenAsync(this, Session.Api, Session.State, note!, editor.Text ?? "");
                if (merged is null) return;
                note = merged; settingText = true; editor.Text = merged.Markdown; settingText = false; dirty = false; drafts?.Clear(); Render(); UpdateState();
            }
            catch (Exception failure) { await ShowErrorAsync(failure, "The server changed again. Both versions remain preserved; reopen the merge workbench."); }
        }
        catch (Exception exception)
        {
            if (drafts is not null) await drafts.FlushAsync();
            status.Text = "Saved on this device"; offline = true;
            await ShowErrorAsync(exception, "The server is unavailable. Your draft remains on this device.");
        }
    }

    private async Task PickAssetAsync(bool images)
    {
        try
        {
            if (note is null) return;
            if (Session.Assets is null) { Session.LoadKnownLocalState(); if (Session.Assets is null) await Session.EnsureConnectedAsync(); }
            var picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = images ? "Choose an image" : "Choose a file", FileTypes = images ? FilePickerFileType.Images : null });
            if (picked is null || Session.Assets is null) return;
            await using var stream = await picked.OpenReadAsync();
            var pending = await Session.Assets.StageAsync(stream, picked.FileName, picked.ContentType);
            pendingAssets.Add(pending);
            var label = Path.GetFileNameWithoutExtension(picked.FileName);
            var markdown = (images ? "!" : "") + $"[{label}](asset-pending://{pending.Id:D})";
            var source = editor.Text ?? ""; var position = Math.Clamp(editor.CursorPosition, 0, source.Length);
            settingText = true; editor.Text = source.Insert(position, markdown); editor.CursorPosition = position + markdown.Length; settingText = false;
            dirty = true; UpdateState();
            drafts?.Schedule(new(note.Id, DraftKind.ExistingNote, editor.Text ?? "", DateTimeOffset.UtcNow, note.Id, note.Path, note.Revision, note.Markdown, PendingAssets: pendingAssets.ToArray()));
            if (!editing) await ToggleAsync();
        }
        catch (Exception exception) { await ShowErrorAsync(exception, "The attachment could not be added."); }
    }

    private async Task SaveConflictAsNewAsync()
    {
        if (note is null) return;
        var id = Guid.NewGuid();
        var folder = note.Path.Contains('/') ? note.Path[..note.Path.LastIndexOf('/')] : "";
        var name = Path.GetFileNameWithoutExtension(note.Path) + " recovered " + id.ToString("N")[..8];
        var created = await Session.Api.CreateNoteAsync(new(folder, name, note.Title + " recovered", id, editor.Text ?? ""), default);
        drafts?.Clear();
        if (Session.State is not null) { await Session.State.DeleteDraftAsync(note.Id); await Session.State.CacheNoteAsync(created); }
        note = created; settingText = true; editor.Text = created.Markdown; settingText = false; dirty = offline = false; Title = created.Title; Render(); UpdateState();
    }

    private async Task ToggleAsync()
    {
        editing = !editing; editor.IsVisible = editing; reader.IsVisible = !editing; markdownTools.IsVisible = editing;
        toggle.Source = editing ? "eye_20_regular.png" : "edit_20_regular.png";
        SemanticProperties.SetDescription(toggle, editing ? "Reading view" : "Source mode");
        if (!editing) Render(); else { await Task.Delay(100); editor.Focus(); }
    }

    private async void Render()
    {
        if (note is null || editing) return;
        var generation = ++previewGeneration;
        var draft = note with { Markdown = editor.Text ?? "" };
        var key = $"{draft.Path}/{draft.Revision}/{draft.Markdown}";
        await previewGate.WaitAsync();
        try
        {
            if (generation != previewGeneration || (key == renderedPreviewKey && pendingHeading is null)) return;
            NoteLinks? links = null; try { if (!offline) links = await Session.Api.LinksAsync(draft.Id, default); } catch (HttpRequestException) { }
            var images = new Dictionary<Guid, string>();
            foreach (var id in AssetReferences.Extract(draft.Path, draft.Markdown))
            {
                if (Session.State is not null && await Session.State.Offline.ReadAssetAsync(id) is { } localAsset)
                {
                    if (localAsset.Metadata.InlineImage) images[id] = $"data:{localAsset.Metadata.ContentType};base64,{Convert.ToBase64String(localAsset.Bytes)}";
                    continue;
                }
                if (offline) continue;
                try
                {
                    var metadata = await Session.Api.AssetMetadataAsync(id, default); if (!metadata.InlineImage) continue;
                    images[id] = $"data:{metadata.ContentType};base64,{Convert.ToBase64String(await Session.Api.ReadAssetBytesAsync(id, default))}";
                }
                catch (HttpRequestException) { }
            }
            var path = await Session.Renderer.WriteDocumentAsync(FileSystem.CacheDirectory, draft, links, images);
            if (generation != previewGeneration || editing) return;
            renderedPreviewKey = key;
#if ANDROID
            AndroidNoteReader.Load(reader, path, pendingHeading, url => ReaderNavigating(reader,
                new WebNavigatingEventArgs(WebNavigationEvent.NewPage, new UrlWebViewSource { Url = url }, url)));
            pendingHeading = null;
#else
            reader.Source = new UrlWebViewSource { Url = new Uri(path).AbsoluteUri + (pendingHeading ?? "") }; pendingHeading = null;
#endif
        }
        catch (Exception exception) { status.Text = "Preview unavailable: " + exception.Message; }
        finally { previewGate.Release(); }
    }

    private void UpdateState() => status.Text = offline ? "Offline copy · may be stale" : dirty ? "Unsaved" : "Saved";

    private async void ReaderNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (e.Url is "about:blank" || e.Url.StartsWith("data:text/html", StringComparison.Ordinal)) return;
        e.Cancel = true;
        if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme == "file") { e.Cancel = false; return; }
        if (uri.Scheme == "slate-note" && Guid.TryParse(uri.Host, out var id))
        {
            await Navigation.PushAsync(new MobileNotePage(Session, id, uri.Fragment));
        }
        else if (uri.Scheme == "slate-asset" && Guid.TryParse(uri.Host, out var assetId))
        {
            try
            {
                var local = Session.State is null ? null : await Session.State.Offline.ReadAssetAsync(assetId);
                var metadata = local?.Metadata ?? await Session.Api.AssetMetadataAsync(assetId, default);
                var directory = Path.Combine(FileSystem.CacheDirectory, "slate.lib", "open-assets"); Directory.CreateDirectory(directory);
                var destination = Path.Combine(directory, assetId.ToString("D") + metadata.Extension);
                if (local is { } downloaded) await File.WriteAllBytesAsync(destination, downloaded.Bytes);
                else await Session.Api.DownloadAssetAsync(assetId, destination, default);
                await Launcher.Default.OpenAsync(new OpenFileRequest(metadata.OriginalFilename, new ReadOnlyFile(destination)));
            }
            catch (Exception exception) { await ShowErrorAsync(exception, "The attachment could not be opened."); }
        }
        else if (uri.Scheme == "https" && uri.Host == "slate.invalid")
        {
            try
            {
                var target = await Session.Api.ReadPathAsync(Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')), default);
                await Navigation.PushAsync(new MobileNotePage(Session, target.Id));
            }
            catch (Exception exception) { await ShowErrorAsync(exception, "The linked note could not be opened."); }
        }
        else if (uri.Scheme is "http" or "https") await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
    }
}

public sealed class MobileNewNotePage : MobilePage
{
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private readonly string folder;
    private readonly Guid id;
    private readonly Entry name = new() { Placeholder = "Filename or title" };
    private readonly Editor editor = new() { Placeholder = "Write Markdown…", AutoSize = EditorAutoSizeOption.Disabled, FontFamily = "monospace", FontSize = 15 };
    private readonly Label state = new() { FontSize = 12, Opacity = 0.7 };
    private DraftDebouncer? drafts;
    private readonly List<PendingAsset> pendingAssets = [];

    public MobileNewNotePage(MobileSession session, string folder) : this(session, folder, Guid.NewGuid(), null, null) { }

    public MobileNewNotePage(MobileSession session, DraftRecord draft) : this(session,
        draft.TargetPath is { } target && target.Contains('/') ? target[..target.LastIndexOf('/')] : "",
        draft.DraftId,
        draft.TargetPath is { } path ? Path.GetFileName(path) : null,
        draft.Markdown) { }

    private MobileNewNotePage(MobileSession session, string folder, Guid id, string? initialName, string? initialMarkdown) : base(session, "New note")
    {
        this.folder = folder; this.id = id; name.Text = initialName; editor.Text = initialMarkdown;
        ToolbarItems.Add(new ToolbarItem("Template", "document_20_regular.png", async () =>
        {
            if (!string.IsNullOrWhiteSpace(editor.Text)) { await SlateDialogs.AlertAsync(this, "Template", "Start with a blank draft to apply a template. Your existing text is preserved.", "OK"); return; }
            var selected = await KnowledgeDialogs.TemplateAsync(this, Session.Api, this.id, name.Text ?? "New note");
            if (!selected.Cancelled && selected.Markdown is not null) { editor.Text = selected.Markdown; Persist(); }
        }));
        MobileTheme.Input(name); MobileTheme.Input(editor);
        name.HeightRequest = 48; name.Margin = new Thickness(10, 0);
        editor.TextChanged += (_, _) => Persist(); name.TextChanged += (_, _) => Persist();
        state.TextColor = MobileTheme.Secondary;
        var save = MobileTheme.Button("Save note", "save_20_regular.png", true, true); save.Clicked += async (_, _) => await SaveAsync();
        var image = MobileTheme.IconButton("image_20_regular.png", "Insert image"); image.Clicked += async (_, _) => await PickAssetAsync(true);
        var file = MobileTheme.IconButton("attach_20_regular.png", "Attach file"); file.Clicked += async (_, _) => await PickAssetAsync(false);
        var link = MobileTheme.IconButton("link_20_regular.png", "Insert wiki link"); link.Clicked += async (_, _) => await InsertWikiLinkAsync(editor);
        var location = MobileTheme.Label(folder.Length == 0 ? "Library  /  New note" : "Library  /  " + folder, 12, MobileTheme.Secondary);
        var tools = MarkdownToolbar.Create(editor, () => PickAssetAsync(true), () => PickAssetAsync(false), () => InsertWikiLinkAsync(editor));
        Content = new Grid { Padding = 16, RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, RowSpacing = 10, Children = { location } };
        ((Grid)Content).Add(MobileTheme.Frame(name, new Thickness(0), 14), 0, 1); ((Grid)Content).Add(tools, 0, 2); ((Grid)Content).Add(MobileTheme.Frame(editor, new Thickness(0), 14), 0, 3);
        var footer = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } }; footer.Add(state); footer.Add(save, 1); ((Grid)Content).Add(footer, 0, 4);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (Session.State is null) try { await Session.EnsureConnectedAsync(); } catch { }
        if (Session.State is not null)
        {
            drafts ??= new DraftDebouncer(Session.State);
            if (await Session.State.ReadDraftAsync(id) is { } draft) { editor.Text = draft.Markdown; pendingAssets.AddRange(draft.PendingAssets ?? []); }
        }
        name.Focus();
    }

    protected override void OnDisappearing() { if (!string.IsNullOrWhiteSpace(editor.Text)) _ = drafts?.FlushAsync(); base.OnDisappearing(); }

    private void Persist()
    {
        var target = folder.Length == 0 ? name.Text : folder + "/" + name.Text;
        drafts?.Schedule(new(id, DraftKind.NewNote, editor.Text ?? "", DateTimeOffset.UtcNow, TargetPath: target, PendingAssets: pendingAssets.ToArray())); state.Text = "Saved on this device";
    }

    private async Task SaveAsync()
    {
        if (!await saveGate.WaitAsync(0)) return;
        try { await SaveCoreAsync(); }
        finally { saveGate.Release(); }
    }

    private async Task SaveCoreAsync()
    {
        if (string.IsNullOrWhiteSpace(name.Text)) { await SlateDialogs.AlertAsync(this, "Name required", "Enter a filename or title.", "OK"); return; }
        try
        {
            var targetName = name.Text.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? name.Text : name.Text + ".md";
            var targetPath = folder.Length == 0 ? targetName : folder + "/" + targetName;
            var markdown = editor.Text ?? "";
            foreach (var asset in Session.State is null ? pendingAssets : [])
            {
                var metadata = await PendingAssetStore.UploadAsync(Session.Api, asset);
                markdown = markdown.Replace($"asset-pending://{asset.Id:D}", AssetReferences.PathForNote(targetPath, asset.Id, metadata.Extension), StringComparison.Ordinal);
            }
            var request = new CreateNoteRequest(folder, name.Text, Path.GetFileNameWithoutExtension(name.Text), id, markdown);
            var note = Session.State is null ? await Session.Api.CreateNoteAsync(request, default) : await OfflineActions.CreateAsync(Session.State, Session.Api, request, pendingAssets.ToArray());
            drafts?.Clear();
            if (Session.State is not null) { await Session.State.DeleteDraftAsync(id); await Session.State.CacheNoteAsync(note); }
            foreach (var asset in pendingAssets) PendingAssetStore.Delete(asset);
            pendingAssets.Clear();
            // Keep the attached navigation stack before this page is removed.
            var navigation = (Parent as NavigationPage)?.Navigation ?? Navigation;
            await navigation.PopAsync();
            await navigation.PushAsync(new MobileNotePage(Session, note.Id));
        }
        catch (Exception exception) { if (drafts is not null) await drafts.FlushAsync(); await ShowErrorAsync(exception, "The note remains saved on this device."); }
    }

    private async Task PickAssetAsync(bool images)
    {
        try
        {
            if (Session.Assets is null) { Session.LoadKnownLocalState(); if (Session.Assets is null) await Session.EnsureConnectedAsync(); }
            var picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = images ? "Choose an image" : "Choose a file", FileTypes = images ? FilePickerFileType.Images : null });
            if (picked is null || Session.Assets is null) return;
            await using var stream = await picked.OpenReadAsync();
            var pending = await Session.Assets.StageAsync(stream, picked.FileName, picked.ContentType);
            pendingAssets.Add(pending);
            var label = Path.GetFileNameWithoutExtension(picked.FileName);
            var markdown = (images ? "!" : "") + $"[{label}](asset-pending://{pending.Id:D})";
            var source = editor.Text ?? ""; var position = Math.Clamp(editor.CursorPosition, 0, source.Length);
            editor.Text = source.Insert(position, markdown); editor.CursorPosition = position + markdown.Length; Persist();
        }
        catch (Exception exception) { await ShowErrorAsync(exception, "The attachment could not be added."); }
    }
}

public sealed class MobileCapturePage : MobilePage
{
    private readonly Guid id;
    private readonly Editor content = new() { Placeholder = "What are you thinking?", AutoSize = EditorAutoSizeOption.TextChanges, FontSize = 17 };
    private DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
    private readonly Editor comment = new() { Placeholder = "Optional thought or context", AutoSize = EditorAutoSizeOption.TextChanges };
    private readonly Label state = new() { FontSize = 12, Opacity = 0.7 };
    private readonly DraftKind kind;
    private readonly List<PendingAsset> pendingAssets = [];
    private readonly Label attachments = new() { FontSize = 12, Opacity = 0.75 };
    private DraftDebouncer? drafts;

    public MobileCapturePage(MobileSession session, SharedCaptureInput? shared = null) : this(session, Guid.NewGuid(), shared, null, null) { }

    public MobileCapturePage(MobileSession session, IncomingShare share) : this(session, share.Id, share.Payload, null,
        share.Assets ?? (share.Asset is null ? null : [share.Asset])) { capturedAt = share.ReceivedAt; state.Text = share.Error ?? ""; }

    public MobileCapturePage(MobileSession session, DraftRecord draft) : this(session, draft.DraftId,
        draft.PendingCapture is null ? null : new SharedCaptureInput(draft.PendingCapture.Content, Uri.TryCreate(draft.PendingCapture.Content, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"),
        draft.PendingCapture?.Comment, draft.PendingAssets) { capturedAt = draft.PendingCapture?.CapturedAt ?? draft.UpdatedAt; }

    private MobileCapturePage(MobileSession session, Guid id, SharedCaptureInput? shared, string? existingComment, IReadOnlyList<PendingAsset>? assets) : base(session, shared is null && assets is null ? "Quick Thought" : "Save to Slate")
    {
        this.id = id;
        if (assets is not null) pendingAssets.AddRange(assets);
        kind = shared is null && pendingAssets.Count == 0 ? DraftKind.QuickThought : DraftKind.Share;
        if (shared is not null) content.Text = shared.Text;
        if (existingComment is not null) comment.Text = existingComment;
        MobileTheme.Input(content); MobileTheme.Input(comment);
        content.MinimumHeightRequest = 150; content.Margin = 10; comment.Margin = 10;
        state.TextColor = MobileTheme.Secondary;
        attachments.TextColor = MobileTheme.Secondary;
        content.TextChanged += (_, _) => Persist(); comment.TextChanged += (_, _) => Persist();
        var save = MobileTheme.Button("Save to Inbox", "save_20_regular.png", true, true); save.Clicked += async (_, _) => await SaveAsync();
        var append = MobileTheme.IconButton("link_20_regular.png", "Append capture to an existing note", size: 44);
        append.Clicked += async (_, _) =>
        {
            try
            {
                Persist(); if (drafts is not null) await drafts.FlushAsync(); if (Session.State is null) return;
                var saved = await KnowledgeDialogs.AppendDraftAsync(this, Session.Api, Session.State, Request(), pendingAssets);
                if (saved is null) return;
                drafts?.Clear(); await Session.State.DeleteDraftAsync(id);
                await Navigation.PushAsync(new MobileNotePage(Session, saved.Id));
            }
            catch (Exception exception) { await ShowErrorAsync(exception, "The append was not confirmed. The capture and any merged draft remain on this device; review Recent before retrying."); }
        };
        var image = MobileTheme.IconButton("image_20_regular.png", "Insert image"); image.Clicked += async (_, _) => await PickAsync(true);
        var file = MobileTheme.IconButton("attach_20_regular.png", "Attach file"); file.Clicked += async (_, _) => await PickAsync(false);
        var attachmentBar = new HorizontalStackLayout { Spacing = 8, Children = { image, file, append, attachments } };
        Content = new Grid { Padding = 16, RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) }, RowSpacing = 10,
            Children = { MobileTheme.Section("Inbox / Capture"), MobileTheme.Label(kind == DraftKind.QuickThought ? "Quick Thought" : "Shared content", 22, MobileTheme.Primary, FontAttributes.Bold) } };
        ((Grid)Content).SetRow(((Grid)Content).Children[1], 1); ((Grid)Content).Add(MobileTheme.Frame(content, new Thickness(0), 14), 0, 2);
        ((Grid)Content).Add(attachmentBar, 0, 3);
        ((Grid)Content).Add(MobileTheme.Section("Context"), 0, 4); ((Grid)Content).Add(MobileTheme.Frame(comment, new Thickness(0), 14), 0, 5);
        var footer = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } }; footer.Add(state); footer.Add(save, 1); ((Grid)Content).Add(footer, 0, 6);
        UpdateAttachments();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (Session.State is null) { Session.LoadKnownLocalState(); try { await Session.EnsureConnectedAsync(); } catch { } }
        if (Session.State is not null) { drafts ??= new DraftDebouncer(Session.State); Persist(); await drafts.FlushAsync(); }
        if (string.IsNullOrWhiteSpace(content.Text)) content.Focus(); else comment.Focus();
    }

    protected override void OnDisappearing() { if (!string.IsNullOrWhiteSpace(content.Text) || pendingAssets.Count > 0) _ = drafts?.FlushAsync(); base.OnDisappearing(); }

    private CaptureNoteRequest Request() => new(id, content.Text ?? "", string.IsNullOrWhiteSpace(comment.Text) ? null : comment.Text, kind == DraftKind.Share ? "share" : "quick-thought", capturedAt, pendingAssets.Select(x => x.Id).ToArray());
    private void Persist()
    {
        if (string.IsNullOrWhiteSpace(content.Text) && string.IsNullOrWhiteSpace(comment.Text) && pendingAssets.Count == 0) return;
        var request = Request();
        drafts?.Schedule(new(id, kind, content.Text ?? attachments.Text, DateTimeOffset.UtcNow, TargetPath: "inbox", PendingCapture: request, PendingAssets: pendingAssets.ToArray(), ReadyToSubmit: false));
        state.Text = "Saved on this device";
    }

    private async Task PickAsync(bool images)
    {
        try
        {
            if (Session.Assets is null) { Session.LoadKnownLocalState(); if (Session.Assets is null) await Session.EnsureConnectedAsync(); }
            var picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = images ? "Choose an image" : "Choose a file", FileTypes = images ? FilePickerFileType.Images : null });
            if (picked is null || Session.Assets is null) return;
            await using var input = await picked.OpenReadAsync();
            pendingAssets.Add(await Session.Assets.StageAsync(input, picked.FileName, picked.ContentType));
            UpdateAttachments(); Persist(); if (drafts is not null) await drafts.FlushAsync();
        }
        catch (Exception exception) { await ShowErrorAsync(exception, "The attachment could not be added."); }
    }

    private void UpdateAttachments() => attachments.Text = pendingAssets.Count == 0 ? "" : string.Join(", ", pendingAssets.Select(x => x.OriginalFilename));

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(content.Text) && string.IsNullOrWhiteSpace(comment.Text) && pendingAssets.Count == 0) { await SlateDialogs.AlertAsync(this, "Nothing to save", "Type a thought or attach a file first.", "OK"); return; }
        Persist(); if (drafts is not null) await drafts.FlushAsync();
        try
        {
            if (Session.State is null) { await SlateDialogs.AlertAsync(this, "Configure Slate", "Open Settings and connect once before saving an offline capture.", "OK"); return; }
            var draft = (await Session.State.ReadDraftAsync(id))!;
            await Session.State.SaveDraftAsync(draft with { ReadyToSubmit = true });
            var request = draft.PendingCapture! with { AssetIds = (draft.PendingAssets ?? []).Select(x => x.Id).ToArray() };
            var note = await OfflineActions.SaveAsync(Session.State, Session.Api, draft, new(Guid.NewGuid(), request.CaptureId, "capture", draft.Markdown, Capture: request), draft.TargetPath ?? "inbox/capture.md");
            drafts?.Clear(); await Session.State.DeleteDraftAsync(id); await Session.State.CacheNoteAsync(note);
            foreach (var asset in draft.PendingAssets ?? []) PendingAssetStore.Delete(asset);
            await SlateDialogs.AlertAsync(this, "Saved", "Captured in Inbox.", "Done"); await Navigation.PopAsync();
        }
        catch (Exception)
        {
            state.Text = "Saved locally · will retry when Slate can reach your server";
            await SlateDialogs.AlertAsync(this, "Saved on this device", "The capture is safe locally. Slate will retry when it can reach your server.", "OK");
        }
    }
}

public sealed class MobileSearchPage : MobilePage
{
    private readonly Entry query = new() { Placeholder = "Search notes or type:question…", ReturnType = ReturnType.Search };
    private readonly ObservableCollection<SearchHit> results = [];
    private readonly Label state = new() { Margin = new Thickness(2, 8), FontSize = 12, TextColor = MobileTheme.Secondary };
    private CancellationTokenSource? delay;
    private readonly Button previous = MobileTheme.Button("Previous", "arrow_left_20_regular.png");
    private readonly Button next = MobileTheme.Button("Next", "arrow_right.png");
    private int pageNumber;
    private int requestNumber;

    public MobileSearchPage(MobileSession session) : base(session, "Search")
    {
        ToolbarItems.Insert(0, new ToolbarItem("Smart views and saved searches", "bookmark.png", async () =>
        {
            try
            {
                await Session.EnsureConnectedAsync();
                if (Session.State is not null && await SmartViewBrowser.OpenAsync(this, Session.Api, Session.State.Workspace, query.Text) is { } hit)
                    await Navigation.PushAsync(new MobileNotePage(Session, hit.Id));
            }
            catch (Exception e) { await ShowErrorAsync(e, "Smart views could not be opened."); }
        }));
        MobileTheme.Input(query); query.HeightRequest = 48; query.Margin = new Thickness(10, 0);
        query.TextChanged += (_, e) => { delay?.Cancel(); delay?.Dispose(); delay = new(); _ = SearchAfterDelayAsync(e.NewTextValue ?? "", delay.Token); };
        query.Completed += async (_, _) => await SearchAsync(query.Text ?? "", default);
        var filter = MobileTheme.IconButton("add_20_regular.png", "Add search filter");
        filter.Clicked += async (_, _) => { if (await SearchFilters.AddAsync(this, query.Text ?? "") is { } text) query.Text = text; };
        var input = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        input.Add(query); input.Add(filter, 1);
        var list = new CollectionView { ItemsSource = results, BackgroundColor = Colors.Transparent, SelectionMode = SelectionMode.Single, EmptyView = MobileTheme.Label("Search titles, paths, tags, and note text.", 13, MobileTheme.Secondary) };
        list.ItemTemplate = new DataTemplate(() =>
        {
            var title = new Label { FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = MobileTheme.Primary }; title.SetBinding(Label.TextProperty, nameof(SearchHit.Title));
            var path = new Label { FontFamily = "monospace", FontSize = 10, TextColor = MobileTheme.Accent }; path.SetBinding(Label.TextProperty, nameof(SearchHit.Path));
            var snippet = new Label { FontSize = 13, TextColor = MobileTheme.Secondary, MaxLines = 3 }; snippet.SetBinding(Label.TextProperty, nameof(SearchHit.Snippet));
            return new VerticalStackLayout { Padding = new Thickness(14, 11), Spacing = 4, Children = { title, path, snippet } };
        });
        list.SelectionChanged += async (_, e) => { if (e.CurrentSelection.FirstOrDefault() is SearchHit hit) { list.SelectedItem = null; await Navigation.PushAsync(new MobileNotePage(Session, hit.Id)); } };
        Content = new Grid { Padding = 16, RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, Children = { MobileTheme.Frame(input, new Thickness(0), 14) } };
        ((Grid)Content).Add(state, 0, 1); ((Grid)Content).Add(list, 0, 2);
        var paging = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 12 };
        paging.Add(previous); paging.Add(next, 1); previous.IsVisible = next.IsVisible = false; ((Grid)Content).Add(paging, 0, 3);
        previous.Clicked += async (_, _) => await SearchAsync(query.Text ?? "", default, Math.Max(0, pageNumber - 1));
        next.Clicked += async (_, _) => await SearchAsync(query.Text ?? "", default, pageNumber + 1);
    }

    private async Task SearchAfterDelayAsync(string value, CancellationToken token)
    {
        try { await Task.Delay(200, token); await SearchAsync(value, token); } catch (OperationCanceledException) { }
    }

    private async Task SearchAsync(string value, CancellationToken token, int requestedPage = 0)
    {
        var request = ++requestNumber; previous.IsVisible = next.IsVisible = false;
        results.Clear();
        if (string.IsNullOrWhiteSpace(value)) { state.Text = "Search"; return; }
        state.Text = "Searching…";
        try
        {
            await Session.EnsureConnectedAsync(token);
            var page = await Session.Api.SearchAsync(value, requestedPage, token);
            if (request != requestNumber || token.IsCancellationRequested) return;
            pageNumber = requestedPage; previous.IsVisible = next.IsVisible = page.Total > 20;
            previous.IsEnabled = pageNumber > 0; next.IsEnabled = (pageNumber + 1) * 20 < page.Total && pageNumber < 1999;
            foreach (var hit in page.Results) results.Add(hit);
            state.Text = page.Total == 0 ? "No results" : $"{page.Total} result{(page.Total == 1 ? "" : "s")}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { if (request == requestNumber) state.Text = exception is HttpRequestException { StatusCode: HttpStatusCode.BadRequest } ? exception.Message : "Server unavailable"; }
    }
}

public sealed class MobileRecentPage : MobilePage
{
    private readonly ObservableCollection<RecentNote> recents = [];
    private readonly ObservableCollection<DraftRecord> drafts = [];

    public MobileRecentPage(MobileSession session) : base(session, "Recent")
    {
        var recentList = new CollectionView { ItemsSource = recents, BackgroundColor = Colors.Transparent, SelectionMode = SelectionMode.Single };
        recentList.ItemTemplate = new DataTemplate(() => { var title = MobileTheme.Label("", 15); title.Padding = 14; title.SetBinding(Label.TextProperty, nameof(RecentNote.Title)); return title; });
        recentList.SelectionChanged += async (_, e) => { if (e.CurrentSelection.FirstOrDefault() is RecentNote recent) { recentList.SelectedItem = null; await Navigation.PushAsync(new MobileNotePage(Session, recent.Id)); } };
        var draftList = new CollectionView { ItemsSource = drafts, BackgroundColor = Colors.Transparent, HeightRequest = 160, SelectionMode = SelectionMode.Single };
        draftList.ItemTemplate = new DataTemplate(() => { var text = MobileTheme.Label("", 13, MobileTheme.Secondary); text.Padding = 14; text.MaxLines = 2; text.SetBinding(Label.TextProperty, nameof(DraftRecord.Markdown)); return text; });
        draftList.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not DraftRecord draft) return; draftList.SelectedItem = null;
            var action = await SlateDialogs.ChooseAsync(this, "Unsaved draft", "Cancel", "Discard", "Resume");
            if (action == "Discard") { foreach (var asset in draft.PendingAssets ?? []) PendingAssetStore.Delete(asset); if (Session.State is not null) await Session.State.DeleteDraftAsync(draft.DraftId); drafts.Remove(draft); return; }
            if (action != "Resume") return;
            if (draft.PendingCapture is not null) await Navigation.PushAsync(new MobileCapturePage(Session, draft));
            else if (draft.NoteId is { } id) await Navigation.PushAsync(new MobileNotePage(Session, id));
            else await Navigation.PushAsync(new MobileNewNotePage(Session, draft));
        };
        var draftsHeading = MobileTheme.Section("Unsaved drafts");
        var recentHeading = MobileTheme.Section("Recently opened"); recentHeading.Margin = new Thickness(0, 16, 0, 8);
        Content = new Grid { Padding = 16, RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) }, RowSpacing = 8, Children = { draftsHeading } };
        ((Grid)Content).Add(MobileTheme.Frame(draftList), 0, 1); ((Grid)Content).Add(recentHeading, 0, 2); ((Grid)Content).Add(MobileTheme.Frame(recentList), 0, 3);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing(); recents.Clear(); drafts.Clear();
        if (Session.State is null) return;
        foreach (var draft in await Session.State.ReadDraftsAsync()) drafts.Add(draft);
        foreach (var recent in await Session.State.ReadRecentsAsync()) recents.Add(recent);
    }
}

public sealed class MobileSettingsPage : ContentPage
{
    private readonly MobileSession session;
    private readonly Entry server = new() { Placeholder = "https://slate.example.ts.net" };
    private readonly Entry token = new() { Placeholder = "Device token", IsPassword = true };
    private readonly Label state = new() { FontSize = 12, Opacity = 0.7 };

    public MobileSettingsPage(MobileSession session)
    {
        this.session = session; Title = "Settings"; server.Text = session.Server;
        MobileTheme.Apply(this); MobileTheme.Input(server); MobileTheme.Input(token);
        server.HeightRequest = token.HeightRequest = 48; server.Margin = token.Margin = new Thickness(10, 0);
        state.TextColor = MobileTheme.Secondary;
        var connect = MobileTheme.Button("Save and test", "arrow_sync_20_regular.png", true); connect.Clicked += async (_, _) => await ConnectAsync();
        var update = MobileTheme.Button("Check for update", "history_20_regular.png"); update.Clicked += async (_, _) => await UpdateCoordinator.CheckAsync(this, session.Api, true);
        Content = new VerticalStackLayout { Padding = 20, Spacing = 12, Children = { MobileTheme.Section("Connection"), MobileTheme.Label("Server URL", 13, MobileTheme.Secondary), MobileTheme.Frame(server, new Thickness(0), 14), MobileTheme.Label("Device token", 13, MobileTheme.Secondary), MobileTheme.Frame(token, new Thickness(0), 14), connect, update, state,
            MobileTheme.Label("Create or rotate device tokens from the Slate server CLI.", 11, MobileTheme.Muted) } };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try { token.Text = await SecureStorage.GetAsync("device-token") ?? ""; } catch { state.Text = "The saved token could not be read."; }
    }

    private async Task ConnectAsync()
    {
        state.Text = "Testing…";
        try { var status = await session.ConnectAsync(server.Text, token.Text); state.Text = $"Connected · server {status.ServerVersion} · search {status.IndexState} · Git {status.Git?.State ?? "unavailable"}"; }
        catch (Exception exception) { state.Text = exception is HttpRequestException ? exception.Message : "Connection failed. Check the URL and token."; }
    }
}

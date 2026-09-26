using System.Collections.ObjectModel;
using System.Net;
using Slate.Lib.Core;

namespace Slate.Lib.App;

public sealed class MobileRootPage : TabbedPage
{
    private readonly MobileSession session;
    private bool initialized;

    public MobileRootPage(MobileSession session)
    {
        this.session = session;
        Title = "slate.lib";
        Children.Add(Tab(new MobileFolderPage(session, "", "Library"), "Library"));
        Children.Add(Tab(new MobileSearchPage(session), "Search"));
        Children.Add(Tab(new MobileFolderPage(session, "inbox", "Inbox", ensureFolder: true), "Inbox"));
        Children.Add(Tab(new MobileRecentPage(session), "Recent"));
        ShareSignals.Received += ShareReceived;
    }

    private async void ShareReceived(object? sender, EventArgs e)
    {
        if (session.State is null) session.LoadKnownLocalState();
        if (session.State is null) return;
        foreach (var share in await IncomingShareStore.TakeAllAsync())
            await CurrentPage.Navigation.PushAsync(new MobileCapturePage(session, share));
    }

    private static NavigationPage Tab(Page page, string title) => new(page) { Title = title };

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!initialized)
        {
            initialized = true;
            session.LoadKnownLocalState();
            try { await session.ConnectAsync(); _ = UpdateCoordinator.CheckAsync(this, session.Api, false); }
            catch { await DisplayAlertAsync("Connect Slate", "Open Settings to enter your server URL and device token.", "OK"); }
        }
        if (session.State is not null)
            foreach (var share in await IncomingShareStore.TakeAllAsync())
                await CurrentPage.Navigation.PushAsync(new MobileCapturePage(session, share));
    }
}

public abstract class MobilePage : ContentPage
{
    protected MobileSession Session { get; }

    protected MobilePage(MobileSession session, string title)
    {
        Session = session; Title = title;
        ToolbarItems.Add(new ToolbarItem("Capture", null, async () => await Navigation.PushAsync(new MobileCapturePage(session))));
        ToolbarItems.Add(new ToolbarItem("Settings", null, async () => await Navigation.PushAsync(new MobileSettingsPage(session))));
    }

    protected async Task ShowErrorAsync(Exception exception, string fallback)
    {
        var text = exception is HttpRequestException ? exception.Message : fallback;
        await DisplayAlertAsync("Slate needs attention", text, "OK");
    }

    protected async Task InsertWikiLinkAsync(Editor editor)
    {
        var query = await DisplayPromptAsync("Insert wiki link", "Search by note title", placeholder: "PostgreSQL");
        if (string.IsNullOrWhiteSpace(query)) return;
        try
        {
            await Session.EnsureConnectedAsync();
            var hits = (await Session.Api.SearchAsync(query, 0, default)).Results.Take(8).ToArray();
            if (hits.Length == 0) { await DisplayAlertAsync("No matches", "No note matched that title.", "OK"); return; }
            var labels = hits.Select(x => x.Title + " — " + x.Path).ToArray();
            var selected = await DisplayActionSheetAsync("Choose a note", "Cancel", null, labels);
            var index = Array.IndexOf(labels, selected); if (index < 0) return;
            var markdown = $"[[id:{hits[index].Id:D}|{hits[index].Title.Replace("]", "", StringComparison.Ordinal)}]]";
            var source = editor.Text ?? ""; var position = Math.Clamp(editor.CursorPosition, 0, source.Length);
            editor.Text = source.Insert(position, markdown); editor.CursorPosition = position + markdown.Length;
        }
        catch (Exception exception) { await ShowErrorAsync(exception, "Links could not be searched."); }
    }
}

public sealed class MobileFolderPage : MobilePage
{
    private readonly string path;
    private readonly bool ensureFolder;
    private readonly ObservableCollection<LibraryEntry> entries = [];
    private readonly CollectionView list;
    private readonly Label state = new() { Margin = new Thickness(16, 8), Opacity = 0.7 };
    private readonly RefreshView refresh;

    public MobileFolderPage(MobileSession session, string path, string title, bool ensureFolder = false) : base(session, title)
    {
        this.path = path; this.ensureFolder = ensureFolder;
        var breadcrumb = new Label { Text = path.Length == 0 ? "Library" : "Library  ›  " + path.Replace("/", "  ›  "), Margin = new Thickness(16, 12, 16, 6), FontSize = 13, Opacity = 0.7 };
        list = new CollectionView { ItemsSource = entries, SelectionMode = SelectionMode.Single };
        list.SelectionChanged += EntrySelected;
        list.EmptyView = new Label { Text = title == "Inbox" ? "Inbox is empty. Capture a thought." : "This folder is empty.", Margin = 24, Opacity = 0.65 };
        list.ItemTemplate = new DataTemplate(() =>
        {
            var name = new Label { FontSize = 16, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation };
            name.SetBinding(Label.TextProperty, nameof(LibraryEntry.Name));
            var pathLabel = new Label { FontSize = 11, Opacity = 0.55, LineBreakMode = LineBreakMode.TailTruncation };
            pathLabel.SetBinding(Label.TextProperty, nameof(LibraryEntry.Path));
            var text = new VerticalStackLayout { Spacing = 2, Children = { name, pathLabel } };
            var actions = new Button { Text = "⋯", WidthRequest = 48, BackgroundColor = Colors.Transparent };
            actions.SetBinding(BindableObject.BindingContextProperty, ".");
            actions.Clicked += async (sender, _) => { if (((Button)sender!).BindingContext is LibraryEntry item) await ShowActionsAsync(item); };
            var grid = new Grid { Padding = new Thickness(16, 10), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            grid.Add(text); grid.Add(actions, 1);
            return grid;
        });
        refresh = new RefreshView { Content = list, Command = new Command(async () => await LoadAsync()) };
        var newNote = new Button { Text = "+ Note" }; newNote.Clicked += async (_, _) => await Navigation.PushAsync(new MobileNewNotePage(Session, path));
        var newFolder = new Button { Text = "+ Folder" }; newFolder.Clicked += async (_, _) => await CreateFolderAsync();
        var controls = new Grid { Padding = new Thickness(16, 8), ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 8 };
        controls.Add(newNote); controls.Add(newFolder, 1);
        Content = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, Children = { breadcrumb } };
        ((Grid)Content).Add(controls, 0, 1); ((Grid)Content).Add(refresh, 0, 2); ((Grid)Content).Add(state, 0, 3);
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
            entries.Clear();
            var page = 0;
            do
            {
                var result = await Session.Api.ListAsync(path, page, default);
                foreach (var entry in result.Entries) entries.Add(entry);
                if (result.NextPage is null) break; page = result.NextPage.Value;
            } while (true);
            state.Text = $"{entries.Count} item{(entries.Count == 1 ? "" : "s")}";
        }
        catch (Exception exception) { state.Text = "Server unavailable"; if (entries.Count == 0) await ShowErrorAsync(exception, "Unable to load this folder."); }
        finally { refresh.IsRefreshing = false; }
    }

    private async void EntrySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not LibraryEntry item) return;
        list.SelectedItem = null;
        if (item.IsDirectory) await Navigation.PushAsync(new MobileFolderPage(Session, item.Path, item.Name));
        else if (item.Id is { } id) await Navigation.PushAsync(new MobileNotePage(Session, id));
    }

    private async Task ShowActionsAsync(LibraryEntry item)
    {
        var action = await DisplayActionSheetAsync(item.Title ?? item.Name, "Cancel", item.IsDirectory ? "Delete folder" : "Delete note", "Open", "Rename", "Move", "Copy", "Duplicate");
        try
        {
            switch (action)
            {
                case "Open":
                    if (item.IsDirectory) await Navigation.PushAsync(new MobileFolderPage(Session, item.Path, item.Name));
                    else await Navigation.PushAsync(new MobileNotePage(Session, item.Id!.Value));
                    return;
                case "Rename":
                    var name = await DisplayPromptAsync("Rename", "New name", initialValue: item.Name);
                    if (!string.IsNullOrWhiteSpace(name)) await Session.Api.RenameAsync(new(item.Path, name), default);
                    break;
                case "Move":
                case "Copy":
                    var destination = await MobileFolderPickerPage.PickAsync(Navigation, Session);
                    if (destination is null) return;
                    if (action == "Move") await Session.Api.MoveAsync(new(item.Path, destination), default);
                    else await Session.Api.CopyAsync(new(item.Path, destination), default);
                    break;
                case "Duplicate":
                    var parent = item.Path.Contains('/') ? item.Path[..item.Path.LastIndexOf('/')] : "";
                    await Session.Api.CopyAsync(new(item.Path, parent), default);
                    break;
                case "Delete note": case "Delete folder":
                    var details = await Session.Api.DetailsAsync(item.Path, default);
                    if (await DisplayAlertAsync("Delete", $"Delete {item.Name}" + (details.DescendantCount > 0 ? $" and {details.DescendantCount} descendant item(s)" : "") + "?", "Delete", "Cancel"))
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
        var name = await DisplayPromptAsync("New folder", "Folder name", placeholder: "New folder");
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
        ToolbarItems.Add(new ToolbarItem("Up", null, async () => { if (path.Length == 0) return; path = path.Contains('/') ? path[..path.LastIndexOf('/')] : ""; await LoadAsync(); }));
        var choose = new Button { Text = "Choose this folder" }; choose.Clicked += async (_, _) => { completion.TrySetResult(path); await Navigation.PopModalAsync(); };
        var cancel = new Button { Text = "Cancel" }; cancel.Clicked += async (_, _) => { completion.TrySetResult(null); await Navigation.PopModalAsync(); };
        var list = new CollectionView { ItemsSource = folders, ItemTemplate = new DataTemplate(() => { var label = new Label { Padding = 16, FontSize = 16 }; label.SetBinding(Label.TextProperty, nameof(LibraryEntry.Name)); return label; }), SelectionMode = SelectionMode.Single };
        list.SelectionChanged += async (_, e) => { if (e.CurrentSelection.FirstOrDefault() is LibraryEntry folder) { path = folder.Path; list.SelectedItem = null; await LoadAsync(); } };
        var newFolder = new Button { Text = "+ New folder" }; newFolder.Clicked += async (_, _) => { var name = await DisplayPromptAsync("New folder", "Name"); if (!string.IsNullOrWhiteSpace(name)) { await session.Api.CreateFolderAsync(new(path, name), default); await LoadAsync(); } };
        Content = new Grid { Padding = 16, RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, Children = { breadcrumb } };
        var buttons = new HorizontalStackLayout { Spacing = 8, Children = { choose, newFolder, cancel } };
        ((Grid)Content).Add(buttons, 0, 1); ((Grid)Content).Add(list, 0, 2);
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

public sealed class MobileNotePage : MobilePage
{
    private readonly Guid noteId;
    private readonly Editor editor = new() { AutoSize = EditorAutoSizeOption.Disabled, FontFamily = "monospace", FontSize = 15, Placeholder = "Write Markdown…" };
    private readonly WebView reader = new();
    private readonly Label status = new() { FontSize = 12, Opacity = 0.7, VerticalOptions = LayoutOptions.Center };
    private readonly Button toggle = new() { Text = "Edit" };
    private LibraryNote? note;
    private DraftDebouncer? drafts;
    private CancellationTokenSource? autosave;
    private bool editing;
    private bool settingText;
    private bool dirty;
    private bool offline;
    private readonly List<PendingAsset> pendingAssets = [];
    private string? pendingHeading;

    public MobileNotePage(MobileSession session, Guid noteId, string? heading = null) : base(session, "Note")
    {
        this.noteId = noteId; pendingHeading = heading;
        var save = new Button { Text = "Save" }; save.Clicked += async (_, _) => await SaveAsync();
        toggle.Clicked += async (_, _) => await ToggleAsync();
        var image = new Button { Text = "Image" }; image.Clicked += async (_, _) => await PickAssetAsync(true);
        var file = new Button { Text = "File" }; file.Clicked += async (_, _) => await PickAssetAsync(false);
        var link = new Button { Text = "Link" }; link.Clicked += async (_, _) => await InsertWikiLinkAsync(editor);
        var bar = new Grid { Padding = new Thickness(12, 8), ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 8 };
        bar.Add(status); bar.Add(image, 1); bar.Add(file, 2); bar.Add(link, 3); bar.Add(toggle, 4); bar.Add(save, 5);
        editor.TextChanged += EditorChanged;
        reader.Navigating += ReaderNavigating;
        Content = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, Children = { bar } };
        ((Grid)Content).Add(reader, 0, 1); ((Grid)Content).Add(editor, 0, 1); editor.IsVisible = false;
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
                    else if (await DisplayAlertAsync("Unsaved changes recovered", "Restore the local draft?", "Restore Draft", "Discard"))
                    {
                        settingText = true; editor.Text = draft.Markdown; settingText = false; dirty = true;
                        pendingAssets.AddRange(draft.PendingAssets ?? []);
                        if (!string.IsNullOrWhiteSpace(draft.BaseRevision)) note = note with { Revision = draft.BaseRevision };
                    }
                    else await Session.State.DeleteDraftAsync(draft.DraftId);
                }
            }
            Render(); UpdateState();
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
        if (note is null || !dirty) return;
        status.Text = "Saving…";
        try
        {
            var sent = editor.Text ?? "";
            foreach (var asset in pendingAssets)
            {
                var metadata = await PendingAssetStore.UploadAsync(Session.Api, asset);
                sent = sent.Replace($"asset-pending://{asset.Id:D}", AssetReferences.PathForNote(note.Path, asset.Id, metadata.Extension), StringComparison.Ordinal);
            }
            if (sent != editor.Text) { settingText = true; editor.Text = sent; settingText = false; }
            var saved = await Session.Api.UpdateNoteAsync(note.Id, new(sent, note.Revision), default);
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
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            status.Text = "Needs attention";
            var choice = await DisplayActionSheetAsync("This note changed elsewhere. Your draft was kept.", "Keep draft", null, "Open latest", "Save draft as new note");
            if (choice == "Open latest")
            {
                var latest = await Session.Api.ReadAsync(note!.Id, default);
                note = latest; settingText = true; editor.Text = latest.Markdown; settingText = false; dirty = false; offline = false; Render(); UpdateState();
            }
            else if (choice == "Save draft as new note") await SaveConflictAsNewAsync();
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
        editing = !editing; editor.IsVisible = editing; reader.IsVisible = !editing; toggle.Text = editing ? "Preview" : "Edit";
        if (!editing) Render(); else { await Task.Delay(100); editor.Focus(); }
    }

    private async void Render()
    {
        if (note is null) return;
        try
        {
            var draft = note with { Markdown = editor.Text ?? "" };
            NoteLinks? links = null; try { links = await Session.Api.LinksAsync(draft.Id, default); } catch (HttpRequestException) { }
            var images = new Dictionary<Guid, string>();
            foreach (var id in AssetReferences.Extract(draft.Path, draft.Markdown))
            {
                try
                {
                    var metadata = await Session.Api.AssetMetadataAsync(id, default); if (!metadata.InlineImage) continue;
                    images[id] = $"data:{metadata.ContentType};base64,{Convert.ToBase64String(await Session.Api.ReadAssetBytesAsync(id, default))}";
                }
                catch (HttpRequestException) { }
            }
            var path = await Session.Renderer.WriteDocumentAsync(FileSystem.CacheDirectory, draft, links, images);
            reader.Source = new UrlWebViewSource { Url = new Uri(path).AbsoluteUri + (pendingHeading ?? "") }; pendingHeading = null;
        }
        catch (Exception exception) { status.Text = "Preview unavailable: " + exception.Message; }
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
                var metadata = await Session.Api.AssetMetadataAsync(assetId, default);
                var directory = Path.Combine(FileSystem.CacheDirectory, "slate.lib", "open-assets"); Directory.CreateDirectory(directory);
                var destination = Path.Combine(directory, assetId.ToString("D") + metadata.Extension);
                await Session.Api.DownloadAssetAsync(assetId, destination, default);
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
        editor.TextChanged += (_, _) => Persist(); name.TextChanged += (_, _) => Persist();
        var save = new Button { Text = "Save" }; save.Clicked += async (_, _) => await SaveAsync();
        var image = new Button { Text = "Attach image" }; image.Clicked += async (_, _) => await PickAssetAsync(true);
        var file = new Button { Text = "Attach file" }; file.Clicked += async (_, _) => await PickAssetAsync(false);
        var link = new Button { Text = "Wiki link" }; link.Clicked += async (_, _) => await InsertWikiLinkAsync(editor);
        Content = new Grid { Padding = 16, RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, RowSpacing = 10, Children = { new Label { Text = folder.Length == 0 ? "Library" : folder, FontSize = 12, Opacity = 0.65 } } };
        ((Grid)Content).Add(name, 0, 1); ((Grid)Content).Add(new HorizontalStackLayout { Spacing = 8, Children = { image, file, link } }, 0, 2); ((Grid)Content).Add(editor, 0, 3);
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
        if (string.IsNullOrWhiteSpace(name.Text)) { await DisplayAlertAsync("Name required", "Enter a filename or title.", "OK"); return; }
        try
        {
            var targetName = name.Text.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? name.Text : name.Text + ".md";
            var targetPath = folder.Length == 0 ? targetName : folder + "/" + targetName;
            var markdown = editor.Text ?? "";
            foreach (var asset in pendingAssets)
            {
                var metadata = await PendingAssetStore.UploadAsync(Session.Api, asset);
                markdown = markdown.Replace($"asset-pending://{asset.Id:D}", AssetReferences.PathForNote(targetPath, asset.Id, metadata.Extension), StringComparison.Ordinal);
            }
            var note = await Session.Api.CreateNoteAsync(new(folder, name.Text, Path.GetFileNameWithoutExtension(name.Text), id, markdown), default);
            drafts?.Clear();
            if (Session.State is not null) { await Session.State.DeleteDraftAsync(id); await Session.State.CacheNoteAsync(note); }
            foreach (var asset in pendingAssets) PendingAssetStore.Delete(asset);
            pendingAssets.Clear();
            await Navigation.PopAsync(); await Navigation.PushAsync(new MobileNotePage(Session, note.Id));
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
    private readonly Editor comment = new() { Placeholder = "Optional thought or context", AutoSize = EditorAutoSizeOption.TextChanges };
    private readonly Label state = new() { FontSize = 12, Opacity = 0.7 };
    private readonly DraftKind kind;
    private readonly List<PendingAsset> pendingAssets = [];
    private readonly Label attachments = new() { FontSize = 12, Opacity = 0.75 };
    private DraftDebouncer? drafts;

    public MobileCapturePage(MobileSession session, SharedCaptureInput? shared = null) : this(session, Guid.NewGuid(), shared, null, null) { }

    public MobileCapturePage(MobileSession session, IncomingShare share) : this(session, Guid.NewGuid(), share.Payload, null,
        share.Asset is null ? null : [share.Asset]) { }

    public MobileCapturePage(MobileSession session, DraftRecord draft) : this(session, draft.DraftId,
        draft.PendingCapture is null ? null : new SharedCaptureInput(draft.PendingCapture.Content, Uri.TryCreate(draft.PendingCapture.Content, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"),
        draft.PendingCapture?.Comment, draft.PendingAssets) { }

    private MobileCapturePage(MobileSession session, Guid id, SharedCaptureInput? shared, string? existingComment, IReadOnlyList<PendingAsset>? assets) : base(session, shared is null && assets is null ? "Quick Thought" : "Save to Slate")
    {
        this.id = id;
        if (assets is not null) pendingAssets.AddRange(assets);
        kind = shared is null && pendingAssets.Count == 0 ? DraftKind.QuickThought : DraftKind.Share;
        if (shared is not null) content.Text = shared.Text;
        if (existingComment is not null) comment.Text = existingComment;
        content.TextChanged += (_, _) => Persist(); comment.TextChanged += (_, _) => Persist();
        var save = new Button { Text = "Save to Inbox" }; save.Clicked += async (_, _) => await SaveAsync();
        var image = new Button { Text = "Attach image" }; image.Clicked += async (_, _) => await PickAsync(true);
        var file = new Button { Text = "Attach file" }; file.Clicked += async (_, _) => await PickAsync(false);
        var attachmentBar = new HorizontalStackLayout { Spacing = 8, Children = { image, file, attachments } };
        Content = new Grid { Padding = 16, RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) }, RowSpacing = 10,
            Children = { new Label { Text = "Inbox", FontSize = 12, Opacity = 0.65 }, new Label { Text = kind == DraftKind.QuickThought ? "Quick Thought" : "Shared content", FontAttributes = FontAttributes.Bold, FontSize = 18 } } };
        ((Grid)Content).SetRow(((Grid)Content).Children[1], 1); ((Grid)Content).Add(content, 0, 2);
        ((Grid)Content).Add(attachmentBar, 0, 3);
        ((Grid)Content).Add(new Label { Text = "Context", FontSize = 12, Opacity = 0.65 }, 0, 4); ((Grid)Content).Add(comment, 0, 5);
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

    private CaptureNoteRequest Request() => new(id, content.Text ?? "", string.IsNullOrWhiteSpace(comment.Text) ? null : comment.Text, kind == DraftKind.Share ? "share" : "quick-thought", DateTimeOffset.UtcNow, pendingAssets.Select(x => x.Id).ToArray());
    private void Persist()
    {
        if (string.IsNullOrWhiteSpace(content.Text) && string.IsNullOrWhiteSpace(comment.Text) && pendingAssets.Count == 0) return;
        var request = Request();
        drafts?.Schedule(new(id, kind, content.Text ?? attachments.Text, DateTimeOffset.UtcNow, TargetPath: "inbox", PendingCapture: request, PendingAssets: pendingAssets.ToArray()));
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
        if (string.IsNullOrWhiteSpace(content.Text) && string.IsNullOrWhiteSpace(comment.Text) && pendingAssets.Count == 0) { await DisplayAlertAsync("Nothing to save", "Type a thought or attach a file first.", "OK"); return; }
        Persist(); if (drafts is not null) await drafts.FlushAsync();
        try
        {
            if (Session.State is null) { await DisplayAlertAsync("Configure Slate", "Open Settings and connect once before saving an offline capture.", "OK"); return; }
            var draft = (await Session.State.ReadDraftAsync(id))!;
            foreach (var asset in draft.PendingAssets ?? []) await PendingAssetStore.UploadAsync(Session.Api, asset);
            var request = draft.PendingCapture! with { AssetIds = (draft.PendingAssets ?? []).Select(x => x.Id).ToArray() };
            var note = await Session.Api.CaptureAsync(request, default);
            drafts?.Clear(); await Session.State.DeleteDraftAsync(id); await Session.State.CacheNoteAsync(note);
            foreach (var asset in draft.PendingAssets ?? []) PendingAssetStore.Delete(asset);
            await DisplayAlertAsync("Saved", "Captured in Inbox.", "Done"); await Navigation.PopAsync();
        }
        catch (Exception)
        {
            state.Text = "Saved locally · will retry when Slate can reach your server";
            await DisplayAlertAsync("Saved on this device", "The capture is safe locally. Slate will retry when it can reach your server.", "OK");
        }
    }
}

public sealed class MobileSearchPage : MobilePage
{
    private readonly Entry query = new() { Placeholder = "Search notes, title:, path:, tag:…", ReturnType = ReturnType.Search };
    private readonly ObservableCollection<SearchHit> results = [];
    private readonly Label state = new() { Margin = new Thickness(16, 8), Opacity = 0.7 };
    private CancellationTokenSource? delay;

    public MobileSearchPage(MobileSession session) : base(session, "Search")
    {
        query.TextChanged += (_, e) => { delay?.Cancel(); delay?.Dispose(); delay = new(); _ = SearchAfterDelayAsync(e.NewTextValue ?? "", delay.Token); };
        query.Completed += async (_, _) => await SearchAsync(query.Text ?? "", default);
        var list = new CollectionView { ItemsSource = results, SelectionMode = SelectionMode.Single, EmptyView = new Label { Text = "Search the full Library.", Margin = 24, Opacity = 0.65 } };
        list.ItemTemplate = new DataTemplate(() =>
        {
            var title = new Label { FontSize = 16, FontAttributes = FontAttributes.Bold }; title.SetBinding(Label.TextProperty, nameof(SearchHit.Title));
            var path = new Label { FontSize = 11, Opacity = 0.6 }; path.SetBinding(Label.TextProperty, nameof(SearchHit.Path));
            var snippet = new Label { FontSize = 13, MaxLines = 3 }; snippet.SetBinding(Label.TextProperty, nameof(SearchHit.Snippet));
            return new VerticalStackLayout { Padding = new Thickness(16, 10), Spacing = 3, Children = { title, path, snippet } };
        });
        list.SelectionChanged += async (_, e) => { if (e.CurrentSelection.FirstOrDefault() is SearchHit hit) { list.SelectedItem = null; await Navigation.PushAsync(new MobileNotePage(Session, hit.Id)); } };
        Content = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) }, Children = { query } };
        query.Margin = new Thickness(16, 12, 16, 4); ((Grid)Content).Add(state, 0, 1); ((Grid)Content).Add(list, 0, 2);
    }

    private async Task SearchAfterDelayAsync(string value, CancellationToken token)
    {
        try { await Task.Delay(200, token); await SearchAsync(value, token); } catch (OperationCanceledException) { }
    }

    private async Task SearchAsync(string value, CancellationToken token)
    {
        results.Clear();
        if (string.IsNullOrWhiteSpace(value)) { state.Text = "Search"; return; }
        state.Text = "Searching…";
        try
        {
            await Session.EnsureConnectedAsync(token);
            var page = await Session.Api.SearchAsync(value, 0, token);
            foreach (var hit in page.Results) results.Add(hit);
            state.Text = page.Total == 0 ? "No results" : $"{page.Total} result{(page.Total == 1 ? "" : "s")}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { state.Text = exception is HttpRequestException { StatusCode: HttpStatusCode.BadRequest } ? exception.Message : "Server unavailable"; }
    }
}

public sealed class MobileRecentPage : MobilePage
{
    private readonly ObservableCollection<RecentNote> recents = [];
    private readonly ObservableCollection<DraftRecord> drafts = [];

    public MobileRecentPage(MobileSession session) : base(session, "Recent")
    {
        var recentList = new CollectionView { ItemsSource = recents, SelectionMode = SelectionMode.Single };
        recentList.ItemTemplate = new DataTemplate(() => { var title = new Label { FontSize = 16, Padding = 12 }; title.SetBinding(Label.TextProperty, nameof(RecentNote.Title)); return title; });
        recentList.SelectionChanged += async (_, e) => { if (e.CurrentSelection.FirstOrDefault() is RecentNote recent) { recentList.SelectedItem = null; await Navigation.PushAsync(new MobileNotePage(Session, recent.Id)); } };
        var draftList = new CollectionView { ItemsSource = drafts, HeightRequest = 160, SelectionMode = SelectionMode.Single };
        draftList.ItemTemplate = new DataTemplate(() => { var text = new Label { FontSize = 14, Padding = 12, MaxLines = 2 }; text.SetBinding(Label.TextProperty, nameof(DraftRecord.Markdown)); return text; });
        draftList.SelectionChanged += async (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not DraftRecord draft) return; draftList.SelectedItem = null;
            var action = await DisplayActionSheetAsync("Unsaved draft", "Cancel", "Discard", "Resume");
            if (action == "Discard") { foreach (var asset in draft.PendingAssets ?? []) PendingAssetStore.Delete(asset); if (Session.State is not null) await Session.State.DeleteDraftAsync(draft.DraftId); drafts.Remove(draft); return; }
            if (action != "Resume") return;
            if (draft.PendingCapture is not null) await Navigation.PushAsync(new MobileCapturePage(Session, draft));
            else if (draft.NoteId is { } id) await Navigation.PushAsync(new MobileNotePage(Session, id));
            else await Navigation.PushAsync(new MobileNewNotePage(Session, draft));
        };
        Content = new Grid { Padding = 12, RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) }, Children = { new Label { Text = "Unsaved drafts", FontAttributes = FontAttributes.Bold } } };
        ((Grid)Content).Add(draftList, 0, 1); ((Grid)Content).Add(new Label { Text = "Recently opened", FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 12, 0, 0) }, 0, 2); ((Grid)Content).Add(recentList, 0, 3);
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
        var connect = new Button { Text = "Save and test connection" }; connect.Clicked += async (_, _) => await ConnectAsync();
        var update = new Button { Text = "Check for update" }; update.Clicked += async (_, _) => await UpdateCoordinator.CheckAsync(this, session.Api, true);
        Content = new VerticalStackLayout { Padding = 20, Spacing = 12, Children = { new Label { Text = "Server URL", FontAttributes = FontAttributes.Bold }, server, new Label { Text = "Device token", FontAttributes = FontAttributes.Bold }, token, connect, update, state,
            new Label { Text = "Generate a token on the server with: dotnet run --project src/Slate.Lib.Api -- --create-device-token Android", FontSize = 12, Opacity = 0.65 } } };
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

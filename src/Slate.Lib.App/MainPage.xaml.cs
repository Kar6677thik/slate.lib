using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using Slate.Lib.Core;
#if WINDOWS
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Windowing;
using Windows.System;
using Windows.UI.Core;
using Windows.ApplicationModel.DataTransfer;
#endif

namespace Slate.Lib.App;

public sealed class TreeRow : INotifyPropertyChanged
{
    public LibraryEntry Entry { get; init; } = new("", "", true, null, null);
    public int Depth { get; init; }
    private bool expanded;
    private bool hovered;
    private bool selected;
    private bool multiSelected;
    public bool SingleSelection => !multiSelected;
    public string CopyCaption => multiSelected ? "Copy selection" : "Copy";
    public string CutCaption => multiSelected ? "Cut selection" : "Cut";
    public string DeleteCaption => multiSelected ? "Delete selection…" : "Delete…";
    public void SetMultiSelection(bool value)
    {
        multiSelected = value;
        foreach (var property in new[] { nameof(SingleSelection), nameof(CopyCaption), nameof(CutCaption), nameof(DeleteCaption) }) PropertyChanged?.Invoke(this, new(property));
    }
    public bool Expanded
    {
        get => expanded;
        set
        {
            expanded = value;
            PropertyChanged?.Invoke(this, new(nameof(ChevronIcon)));
            PropertyChanged?.Invoke(this, new(nameof(IconSource)));
        }
    }
    public bool Hovered { get => hovered; set { hovered = value; PropertyChanged?.Invoke(this, new(nameof(OverflowOpacity))); PropertyChanged?.Invoke(this, new(nameof(RowBackground))); } }
    public bool Selected { get => selected; set { selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); PropertyChanged?.Invoke(this, new(nameof(OverflowOpacity))); PropertyChanged?.Invoke(this, new(nameof(RowBackground))); } }
    public Color RowBackground => Selected ? Color.FromArgb("#2C3D59") : Hovered ? Color.FromArgb("#2C2F3B") : Colors.Transparent;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool Loaded { get; set; }
    public int? NextPage { get; set; }
    public TreeRow? MoreFor { get; init; }
    public List<TreeRow> Children { get; } = [];
    public Thickness Indent => new(8 + Depth * 16, 0, 0, 0);
    public string Name => MoreFor is not null ? "Load more…" : Entry.Title ?? Entry.Name;
    public string ChevronIcon => Entry.IsDirectory ? (Expanded ? "chevron_down_16_regular.png" : "chevron_right_16_regular.png") : "";
    public string IconSource => MoreFor is not null ? "add_20_regular.png" : Entry.IsDirectory
        ? (Expanded ? "folder_open_20_regular.png" : "folder_20_regular.png")
        : "document_20_regular.png";
    public bool ShowChevron => MoreFor is null && Entry.IsDirectory;
    public bool ShowOverflow => MoreFor is null && (Hovered || Selected);
    public double OverflowOpacity => ShowOverflow ? 1 : 0;
}

public sealed record LinkSidebarItem(Guid? NoteId, string Title, string Detail, bool Resolved);

public partial class MainPage : ContentPage
{
    private enum ViewMode { Write, Preview, Split }
    private readonly LibraryApiClient api;
    private readonly MarkdownReader renderer;
    private readonly ObservableCollection<TreeRow> rows = [];
    private readonly ObservableCollection<LinkSidebarItem> backlinks = [];
    private readonly ObservableCollection<LinkSidebarItem> outgoingLinks = [];
    private TreeRow root = new() { Depth = -1, Expanded = true };
    private CancellationTokenSource connection = new();
    private CancellationTokenSource? reading;
    private CancellationTokenSource? previewDelay;
    private CancellationTokenSource? searchDelay;
    private CancellationTokenSource? autosaveDelay;
    private readonly TaskCompletionSource webReady = new();
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private readonly SemaphoreSlim previewGate = new(1, 1);
    private int previewGeneration;
    private string? renderedPreviewKey;
    private bool firstAppearance = true;
    private bool rebuilding;
    private bool browsing;
    private bool loadingDocument;
    private bool settingEditor;
    private bool dirty;
    private bool explicitOfflineRead;
    private Guid? openedNoteId;
    private LibraryNote? currentNote;
    private TreeRow? selectedRow;
    private string? clipboardPath;
    private bool clipboardCut;
    private ViewMode mode = ViewMode.Preview;
#if WINDOWS
    private bool closeHooked;
    private bool allowWindowClose;
    private bool pageKeysHooked;
#endif
    private string? lastLibraryVersion;
    private long lastSearchVersion;
    private bool searchKeyboardNavigating = false;
    private ClientStateStore? clientState;
    private DraftDebouncer? draftDebouncer;
    private PendingAssetStore? assetStore;
    private readonly List<PendingAsset> pendingAssets = [];
    private string? pendingHeading;

    public MainPage(LibraryApiClient api, MarkdownReader renderer)
    {
        this.api = api; this.renderer = renderer;
        InitializeComponent();
        ClientControls.RoundPanel(ExplorerPanel);
        ClientControls.RoundPanel(RightRail);
        ClientControls.RoundPanel(ReaderPane, 14);
        Tree.ItemsSource = rows;
        InitializeProductivity();
        BacklinksList.ItemsSource = backlinks;
        OutgoingList.ItemsSource = outgoingLinks;
        SetRailTab(true);
        SetDocumentChromeVisible(false);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        HookWindowClose();
        if (!firstAppearance) return;
        firstAppearance = false;
        if (Environment.GetEnvironmentVariable("SLATE_SERVER") is null && Guid.TryParse(Preferences.Get("library-id", ""), out var knownLibrary))
        {
            clientState = new ClientStateStore(FileSystem.AppDataDirectory, FileSystem.CacheDirectory, knownLibrary, 500L * 1024 * 1024);
            assetStore = new PendingAssetStore(FileSystem.AppDataDirectory, knownLibrary);
            OfflineReplayPump.Start(clientState, api);
        }
        ServerEntry.Text = Environment.GetEnvironmentVariable("SLATE_SERVER") ?? Preferences.Get("server", "http://localhost:5188");
        try { TokenEntry.Text = Environment.GetEnvironmentVariable("SLATE_TOKEN") ?? await SecureStorage.GetAsync("device-token"); }
        catch { Status.Text = "Saved token unavailable. Enter it again to connect."; }
        if (!string.IsNullOrWhiteSpace(TokenEntry.Text)) await ConnectAsync(false);
        else await OpenSettingsAsync();
    }

    private async void ConnectClicked(object? sender, EventArgs e)
    {
        if (await ResolveUnsavedAsync()) await ConnectAsync(true);
    }

    private async Task ConnectAsync(bool refreshServer)
    {
        connection.Cancel(); reading?.Cancel(); connection.Dispose(); connection = new();
        var cancellation = connection.Token;
        ResetDocument();
        openTabs.Clear(); navigationHistory.Clear(); navigationIndex = -1; RenderTabs(); UpdateNavigationControls();
        FeedbackBanner.IsVisible = false;
        root = new() { Depth = -1, Expanded = true }; rows.Clear(); SetSelectedRow(null);
        browsing = false; Busy.IsRunning = true; Status.Text = "Connecting…";
        ShowMessage("Connecting to your library", "Fetching folders from the server…");
        try
        {
            api.Connect(ServerEntry.Text ?? "", TokenEntry.Text ?? "");
            if (refreshServer) await api.RefreshAsync(cancellation);
            var status = await api.StatusAsync(cancellation);
            if (Environment.GetEnvironmentVariable("SLATE_SERVER") is null) Preferences.Set("library-id", status.LibraryId.ToString());
            clientState = new ClientStateStore(FileSystem.AppDataDirectory, FileSystem.CacheDirectory, status.LibraryId, 500L * 1024 * 1024);
            await WorkspaceNotes.MigrateBookmarksAsync(clientState, ServerEntry.Text ?? "");
            await RefreshPinnedFoldersAsync();
            assetStore = new PendingAssetStore(FileSystem.AppDataDirectory, status.LibraryId);
            OfflineReplayPump.Start(clientState, api);
            await clientState.ReconcileOfflineAsync(api, cancellation);
            await new PendingCaptureProcessor(clientState).RetryAsync((asset, token) => PendingAssetStore.UploadAsync(api, asset, token), api.CaptureAsync, cancellation);
            await LoadChildren(root, 0, cancellation);
            RebuildRows();
            var stored = true;
            if (Environment.GetEnvironmentVariable("SLATE_SERVER") is null && Environment.GetEnvironmentVariable("SLATE_TOKEN") is null)
            {
                Preferences.Set("server", ServerEntry.Text);
                try { await SecureStorage.SetAsync("device-token", TokenEntry.Text ?? ""); } catch { stored = false; }
            }
            Status.Text = $"Connected · server {status.ServerVersion} · search {status.IndexState}" + (stored ? "" : " · token could not be saved");
            UpdateSyncState(GitLabel(status.Git));
            ConnectionPanel.IsVisible = false;
            lastLibraryVersion = status.LibraryVersion; lastSearchVersion = status.SearchVersion;
            ShowMessage(rows.Count == 0 ? "Your library is empty" : "Choose a note", rows.Count == 0
                ? "Create a folder or note to begin."
                : "Expand a folder, open a note, or right-click an item for actions.");
            _ = PollStatusAsync(cancellation);
            _ = UpdateCoordinator.CheckAsync(this, api, false, cancellation);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) { if (!cancellation.IsCancellationRequested) ShowError(exception); }
        finally { if (!cancellation.IsCancellationRequested) Busy.IsRunning = false; }
    }

    private async Task LoadChildren(TreeRow parent, int page, CancellationToken cancellation)
    {
        var result = await api.ListAsync(parent.Entry.Path, page, cancellation);
        parent.Children.AddRange(result.Entries.Select(entry => new TreeRow { Entry = entry, Depth = parent.Depth + 1 }));
        parent.NextPage = result.NextPage; parent.Loaded = true;
    }

    private void RebuildRows(string? selectPath = null)
    {
        rebuilding = true; var desired = new List<TreeRow>();
        void Append(TreeRow node)
        {
            foreach (var child in node.Children.OrderByDescending(x => x.Entry.IsDirectory).ThenBy(x => x.Name, reverseSort ? Comparer<string>.Create((a, b) => StringComparer.OrdinalIgnoreCase.Compare(b, a)) : StringComparer.OrdinalIgnoreCase))
            {
                desired.Add(child);
                if (child.Expanded) Append(child);
            }
            if (node.NextPage.HasValue) desired.Add(new TreeRow { MoreFor = node, Depth = node.Depth + 1 });
        }
        Append(root);
        // Remove collapsed descendants before inserting new rows. Moving siblings past
        // descendants that are about to disappear made the native tree animation jump.
        var retained = desired.ToHashSet();
        for (var i = rows.Count - 1; i >= 0; i--)
            if (!retained.Contains(rows[i])) rows.RemoveAt(i);
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < rows.Count && ReferenceEquals(rows[i], desired[i])) continue;
            var existing = rows.IndexOf(desired[i]);
            if (existing >= 0) rows.Move(existing, i);
            else rows.Insert(i, desired[i]);
        }
        while (rows.Count > desired.Count) rows.RemoveAt(rows.Count - 1);
        SetSelectedRow(rows.FirstOrDefault(row => row.Entry.Path == selectPath) ??
                       rows.FirstOrDefault(row => !row.Entry.IsDirectory && row.Entry.Id == openedNoteId));
        Tree.SelectedItem = selectedRow?.Entry.IsDirectory == false ? selectedRow : null;
        rebuilding = false;
    }

    private async Task ReloadTreeAsync(string? selectPath = null)
    {
        var expanded = rows.Where(x => x.Expanded).Select(x => x.Entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectPath is not null)
        {
            var segments = selectPath.Split('/');
            for (var i = 1; i < segments.Length; i++) expanded.Add(string.Join('/', segments.Take(i)));
        }
        root = new() { Depth = -1, Expanded = true };
        await LoadChildren(root, 0, connection.Token);
        async Task Restore(TreeRow parent)
        {
            if (selectPath is not null && (parent.Entry.Path.Length == 0 || selectPath.StartsWith(parent.Entry.Path + "/", StringComparison.Ordinal)))
            {
                var relative = parent.Entry.Path.Length == 0 ? selectPath : selectPath[(parent.Entry.Path.Length + 1)..];
                var name = relative.Split('/')[0];
                var childPath = parent.Entry.Path.Length == 0 ? name : parent.Entry.Path + "/" + name;
                while (parent.NextPage is { } page && !parent.Children.Any(x => x.Entry.Path == childPath))
                    await LoadChildren(parent, page, connection.Token);
            }
            foreach (var child in parent.Children.Where(x => x.Entry.IsDirectory && expanded.Contains(x.Entry.Path)))
            {
                await LoadChildren(child, 0, connection.Token); child.Expanded = true; await Restore(child);
            }
        }
        await Restore(root);
        RebuildRows(selectPath);
    }

    private async void SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (rebuilding || e.CurrentSelection.FirstOrDefault() is not TreeRow row) return;
        if (HandleExplorerMultiSelection(row)) return;
        SetSelectedRow(row);
        if (row.MoreFor is null && !row.Entry.IsDirectory)
        {
            if (currentNote?.Id == row.Entry.Id) return;
            if (await ResolveUnsavedAsync()) await OpenNote(token => api.ReadAsync(row.Entry.Id!.Value, token));
            else { rebuilding = true; SetSelectedRow(rows.FirstOrDefault(x => x.Entry.Id == openedNoteId)); Tree.SelectedItem = selectedRow; rebuilding = false; }
            return;
        }
        if (browsing) return;
        browsing = true;
        try
        {
            if (row.MoreFor is { } parent) await LoadChildren(parent, parent.NextPage!.Value, connection.Token);
            else { if (!row.Loaded) await LoadChildren(row, 0, connection.Token); row.Expanded = !row.Expanded; }

            RebuildRows(row.Entry.Path); Status.Text = "Connected";
            rebuilding = true; Tree.SelectedItem = null; rebuilding = false;

        }
        catch (Exception exception) { ShowError(exception); }
        finally { browsing = false; }
    }

    private async Task OpenNote(Func<CancellationToken, Task<LibraryNote>> fetch, bool offlineOnly = false)
    {
        reading?.Cancel(); reading?.Dispose(); reading = CancellationTokenSource.CreateLinkedTokenSource(connection.Token);
        var cancellation = reading.Token;
        Busy.IsRunning = true; Status.Text = "Loading note…"; ShowMessage("Opening note", "Reading Markdown from your server…");
        try
        {
            var note = await fetch(cancellation);
            explicitOfflineRead = offlineOnly;
            SearchPanel.IsVisible = HistoryPanel.IsVisible = false;
            currentNote = note; openedNoteId = note.Id;
            pendingAssets.Clear();
            if (clientState is not null) await clientState.CacheNoteAsync(note, cancellationToken: cancellation);
            var markdown = note.Markdown;
            dirty = false;
            if (clientState is not null)
            {
                draftDebouncer = new(clientState);
                if (await clientState.ReadDraftAsync(note.Id, cancellation) is { } draft)
                {
                    if (NoteDocument.NormalizeLineEndings(draft.Markdown) == NoteDocument.NormalizeLineEndings(note.Markdown))
                        await clientState.DeleteDraftAsync(draft.DraftId, cancellation);
                    else if (await SlateDialogs.AlertAsync(this, "Unsaved changes recovered", "Restore the local draft?", "Restore Draft", "Discard"))
                    {
                        markdown = draft.Markdown; dirty = true;
                        pendingAssets.AddRange(draft.PendingAssets ?? []);
                        if (!string.IsNullOrWhiteSpace(draft.BaseRevision)) currentNote = note with { Revision = draft.BaseRevision, Markdown = draft.BaseMarkdown ?? note.Markdown };
                    }
                    else await clientState.DeleteDraftAsync(draft.DraftId, cancellation);
                }
            }
            settingEditor = true; MarkdownEditor.Text = markdown; settingEditor = false;
            TrackNote(note);
            await RediscoveryBrowser.RecordAsync(clientState, note.Id);
            UpdateDocumentChrome(); SetMode(mode, render: false); await RenderPreviewAsync(markdown, cancellation);
            if (!offlineOnly && !rows.Any(row => row.Entry.Id == note.Id)) await ReloadTreeAsync(note.Path);
            rebuilding = true; SetSelectedRow(rows.FirstOrDefault(row => row.Entry.Id == note.Id)); Tree.SelectedItem = selectedRow; rebuilding = false;
            Status.Text = offlineOnly ? "Downloaded copy · refresh when connected" : "Connected";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) { if (!cancellation.IsCancellationRequested) ShowError(exception); }
        finally { if (!cancellation.IsCancellationRequested) Busy.IsRunning = false; }
    }

    private void EditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (settingEditor || currentNote is null) return;
        if (!dirty && NoteDocument.NormalizeLineEndings(e.NewTextValue ?? "") == NoteDocument.NormalizeLineEndings(currentNote.Markdown)) return;
        dirty = true; UpdateDocumentChrome(updateInfo: false);
        draftDebouncer?.Schedule(CurrentDraft(e.NewTextValue ?? ""));
        autosaveDelay?.Cancel(); autosaveDelay?.Dispose(); autosaveDelay = new();
        _ = AutosaveAsync(autosaveDelay.Token);
        previewDelay?.Cancel(); previewDelay?.Dispose(); previewDelay = new();
        var token = previewDelay.Token;
        _ = DebouncedPreviewAsync(e.NewTextValue ?? "", token);
    }

    private DraftRecord CurrentDraft(string markdown) => new(currentNote!.Id, DraftKind.ExistingNote, NoteDocument.NormalizeLineEndings(markdown), DateTimeOffset.UtcNow,
        currentNote.Id, currentNote.Path, currentNote.Revision, currentNote.Markdown, PendingAssets: pendingAssets.ToArray());

    private async Task AutosaveAsync(CancellationToken token)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(2), token); if (dirty) await SaveAsync(); }
        catch (OperationCanceledException) { }
    }

    private async Task DebouncedPreviewAsync(string source, CancellationToken token)
    {
        try { await Task.Delay(350, token); UpdateInfoPanel(source); if (mode != ViewMode.Write) await RenderPreviewAsync(source, token); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Status.Text = "Preview unavailable: " + exception.Message; }
    }

    private async Task RenderPreviewAsync(string source, CancellationToken cancellation)
    {
        if (currentNote is null || mode == ViewMode.Write) return;
        var generation = ++previewGeneration;
        var key = $"{currentNote.Id}/{currentNote.Path}/{currentNote.Revision}/{lastLibraryVersion}/{source}";
        await previewGate.WaitAsync(cancellation);
        try
        {
            if (generation != previewGeneration || (key == renderedPreviewKey && pendingHeading is null)) return;
            await RenderPreviewCoreAsync(source, cancellation, generation);
            if (generation == previewGeneration) renderedPreviewKey = key;
        }
        finally { previewGate.Release(); }
    }

    private async Task RenderPreviewCoreAsync(string source, CancellationToken cancellation, int generation)
    {
        if (currentNote is null) return;
        var draft = currentNote with { Markdown = NoteDocument.NormalizeLineEndings(source) };
        NoteLinks? links = null;
        if (!explicitOfflineRead) try { links = await api.LinksAsync(draft.Id, cancellation); } catch (HttpRequestException) { }
        UpdateLinkSidebar(links);
        UpdateInfoPanel(draft.Markdown);
        var images = new Dictionary<Guid, string>();
        foreach (var id in AssetReferences.Extract(draft.Path, draft.Markdown))
        {
            if (clientState is not null && await clientState.Offline.ReadAssetAsync(id) is { } localAsset)
            {
                if (localAsset.Metadata.InlineImage) images[id] = $"data:{localAsset.Metadata.ContentType};base64,{Convert.ToBase64String(localAsset.Bytes)}";
                continue;
            }
            try
            {
                if (explicitOfflineRead) continue;
                var metadata = await api.AssetMetadataAsync(id, cancellation);
                if (!metadata.InlineImage) continue;
                var bytes = await api.ReadAssetBytesAsync(id, cancellation);
                images[id] = $"data:{metadata.ContentType};base64,{Convert.ToBase64String(bytes)}";
            }
            catch (HttpRequestException) { }
        }
        var path = await renderer.WriteDocumentAsync(FileSystem.CacheDirectory, draft, links, images, cancellation, desktopAppearance: true);
        await webReady.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (generation != previewGeneration || currentNote?.Id != draft.Id) return;
        loadingDocument = true;
#if WINDOWS
        var target = new Uri(path).AbsoluteUri + (pendingHeading ?? ""); pendingHeading = null;
        ((Microsoft.UI.Xaml.Controls.WebView2)Reader.Handler!.PlatformView!).Source = new Uri(target);
#else
        Reader.Source = new UrlWebViewSource { Url = new Uri(path).AbsoluteUri + (pendingHeading ?? "") }; pendingHeading = null;
#endif
    }

    private async void SaveClicked(object? sender, EventArgs e) => await SaveAsync();

    private async void AttachImageClicked(object? sender, EventArgs e) => await PickAssetAsync(true);
    private async void AttachFileClicked(object? sender, EventArgs e) => await PickAssetAsync(false);
    private async void InsertLinkClicked(object? sender, EventArgs e) => await InsertWikiLinkAsync();

    private async Task InsertWikiLinkAsync()
    {
        if (currentNote is null) return;
        var query = await SlateDialogs.PromptAsync(this, "Insert wiki link", "Search by note title", placeholder: "PostgreSQL");
        if (string.IsNullOrWhiteSpace(query)) return;
        try
        {
            var hits = (await api.SearchAsync(query, 0, connection.Token)).Results.Take(8).ToArray();
            if (hits.Length == 0) { Status.Text = "No matching notes."; return; }
            var labels = hits.Select(x => x.Title + " — " + x.Path).ToArray();
            var selected = await SlateDialogs.ChooseAsync(this, "Choose a note", "Cancel", null, labels);
            var index = Array.IndexOf(labels, selected); if (index < 0) return;
            var title = hits[index].Title.Replace("]", "", StringComparison.Ordinal);
            InsertMarkdown($"[[id:{hits[index].Id:D}|{title}]]");
        }
        catch (Exception exception) { ShowOperationError(exception, "Unable to search for links."); }
    }

    private void InsertMarkdown(string markdown)
    {
        if (mode == ViewMode.Preview) SetMode(ViewMode.Write);
        var source = MarkdownEditor.Text ?? ""; var position = Math.Clamp(MarkdownEditor.CursorPosition, 0, source.Length);
        MarkdownEditor.Text = source.Insert(position, markdown); MarkdownEditor.CursorPosition = position + markdown.Length;
    }

    private async Task PickAssetAsync(bool image)
    {
        try
        {
            if (currentNote is null || assetStore is null) return;
            var picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = image ? "Choose an image" : "Choose a file", FileTypes = image ? FilePickerFileType.Images : null });
            if (picked is null) return;
            await using var stream = await picked.OpenReadAsync();
            await StageAssetAsync(stream, picked.FileName, picked.ContentType, image);
        }
        catch (Exception exception) { ShowOperationError(exception, "Unable to attach the file."); }
    }

    private async Task StageAssetAsync(Stream stream, string filename, string? contentType, bool image)
    {
        if (currentNote is null || assetStore is null) return;
        var asset = await assetStore.StageAsync(stream, filename, contentType, connection.Token);
        pendingAssets.Add(asset);
        var label = Path.GetFileNameWithoutExtension(filename);
        var markdown = (image ? "!" : "") + $"[{label}](asset-pending://{asset.Id:D})";
        var source = MarkdownEditor.Text ?? ""; var position = Math.Clamp(MarkdownEditor.CursorPosition, 0, source.Length);
        MarkdownEditor.Text = source.Insert(position, markdown); MarkdownEditor.CursorPosition = position + markdown.Length;
        draftDebouncer?.Schedule(CurrentDraft(MarkdownEditor.Text));
        if (mode == ViewMode.Preview) SetMode(ViewMode.Write);
    }
    private async Task<bool> SaveAsync()
    {
        await saveGate.WaitAsync();
        try { return await SaveCoreAsync(); }
        finally { saveGate.Release(); }
    }

    private async Task<bool> SaveCoreAsync()
    {
        if (currentNote is null || !dirty) return true;
        Busy.IsRunning = true; DocumentState.Text = "Saving…";
        try
        {
            var sent = NoteDocument.NormalizeLineEndings(MarkdownEditor.Text ?? "");
            foreach (var asset in clientState is null ? pendingAssets : [])
            {
                var metadata = await PendingAssetStore.UploadAsync(api, asset, connection.Token);
                sent = sent.Replace($"asset-pending://{asset.Id:D}", AssetReferences.PathForNote(currentNote.Path, asset.Id, metadata.Extension), StringComparison.Ordinal);
            }
            if (sent != MarkdownEditor.Text) { settingEditor = true; MarkdownEditor.Text = sent; settingEditor = false; }
            var saved = clientState is null ? await api.UpdateNoteAsync(currentNote.Id, new(sent, currentNote.Revision), connection.Token)
                : await OfflineActions.EditAsync(clientState, api, currentNote, sent, pendingAssets.ToArray(), connection.Token);
            if (sent == MarkdownEditor.Text) { settingEditor = true; MarkdownEditor.Text = saved.Markdown; settingEditor = false; sent = saved.Markdown; }
            currentNote = saved;
            if (NoteDocument.NormalizeLineEndings(MarkdownEditor.Text ?? "") == sent)
            {
                dirty = false; draftDebouncer?.Clear();
                if (clientState is not null) await clientState.DeleteDraftAsync(saved.Id, connection.Token);
                foreach (var asset in pendingAssets) PendingAssetStore.Delete(asset);
                pendingAssets.Clear();
            }
            else
            {
                dirty = true; draftDebouncer?.Schedule(CurrentDraft(MarkdownEditor.Text ?? ""));
            }
            if (clientState is not null) await clientState.CacheNoteAsync(saved, cancellationToken: connection.Token);
            UpdateDocumentChrome();
            await ReloadTreeAsync(saved.Path);
            await RenderPreviewAsync(saved.Markdown, connection.Token);
            Status.Text = "Connected"; return true;
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            if (draftDebouncer is not null) await draftDebouncer.FlushAsync();
            DocumentState.Text = "Needs attention";
            try
            {
                var merged = await MergeWorkbench.OpenAsync(this, api, clientState, currentNote!, MarkdownEditor.Text ?? "");
                if (merged is null) return false;
                currentNote = merged; settingEditor = true; MarkdownEditor.Text = merged.Markdown; settingEditor = false; dirty = false; draftDebouncer?.Clear();
                await RenderPreviewAsync(merged.Markdown, connection.Token); UpdateDocumentChrome(); return true;
            }
            catch (Exception failure) { ShowOperationError(failure, "The server changed again. Both versions remain preserved; reopen the merge workbench."); return false; }
        }
        catch (OfflinePendingException exception) { DocumentState.Text = "Queued on this device"; Status.Text = exception.Message; return true; }
        catch (Exception exception) { if (draftDebouncer is not null) await draftDebouncer.FlushAsync(); ShowOperationError(exception, "Unable to save note."); UpdateDocumentChrome(); return false; }
        finally { Busy.IsRunning = false; }
    }

    private async Task<bool> SaveConflictAsNewAsync()
    {
        if (currentNote is null) return false;
        var oldId = currentNote.Id;
        var id = Guid.NewGuid();
        var folder = currentNote.Path.Contains('/') ? currentNote.Path[..currentNote.Path.LastIndexOf('/')] : "";
        var name = Path.GetFileNameWithoutExtension(currentNote.Path) + " recovered " + id.ToString("N")[..8];
        var created = await api.CreateNoteAsync(new(folder, name, currentNote.Title + " recovered", id, MarkdownEditor.Text ?? ""), connection.Token);
        draftDebouncer?.Clear(); if (clientState is not null) { await clientState.DeleteDraftAsync(oldId); await clientState.CacheNoteAsync(created); }
        currentNote = created; openedNoteId = created.Id; settingEditor = true; MarkdownEditor.Text = created.Markdown; settingEditor = false; dirty = false;
        await ReloadTreeAsync(created.Path); await RenderPreviewAsync(created.Markdown, connection.Token); UpdateDocumentChrome();
        return true;
    }

    private async Task<bool> ResolveUnsavedAsync()
    {
        if (!dirty) return true;
        var choice = await SlateDialogs.ChooseAsync(this, "Save changes?", "Cancel", null, "Save", "Discard");
        if (choice == "Save") return await SaveAsync();
        if (choice == "Discard")
        {
            dirty = false; draftDebouncer?.Clear();
            if (currentNote is not null && clientState is not null) await clientState.DeleteDraftAsync(currentNote.Id);
            return true;
        }
        return false;
    }

    private async void CloseNoteClicked(object? sender, EventArgs e) { if (currentNote is not null) await CloseTabAsync(currentNote.Id); }
    private void ResetDocument()
    {
        currentNote = null; openedNoteId = null; dirty = false; settingEditor = true; MarkdownEditor.Text = ""; settingEditor = false; pendingAssets.Clear();
        draftDebouncer?.Clear(); draftDebouncer = null;
        NoteTitle.Text = "Your workspace"; Breadcrumb.Text = "Library";
        AttachImageButton.IsEnabled = AttachFileButton.IsEnabled = InsertLinkButton.IsEnabled = HistoryButton.IsEnabled = WriteButton.IsEnabled = PreviewButton.IsEnabled = SplitButton.IsEnabled = SaveButton.IsEnabled = false;
        CloseNoteButton.IsVisible = false; SaveButton.IsVisible = false; DocumentState.Text = "";
        SetDocumentChromeVisible(false);
        UpdateLinkSidebar(null); UpdateInfoPanel("");
        ShowMessage("Choose a note", "Expand a folder or create a Markdown note.");
    }

    private void UpdateDocumentChrome(bool updateInfo = true)
    {
        if (currentNote is null) return;
        SetDocumentChromeVisible(true);
        NoteTitle.Text = currentNote.Title + (dirty ? "  •" : ""); Breadcrumb.Text = "Library  ›  " + currentNote.Path.Replace("/", "  ›  ");
        DocumentState.Text = dirty ? "Unsaved" : "Saved ✓";
        SaveButton.IsVisible = dirty;
        if (updateInfo) UpdateInfoPanel(MarkdownEditor.Text ?? currentNote.Markdown);
        AttachImageButton.IsEnabled = AttachFileButton.IsEnabled = InsertLinkButton.IsEnabled = HistoryButton.IsEnabled = WriteButton.IsEnabled = PreviewButton.IsEnabled = SplitButton.IsEnabled = SaveButton.IsEnabled = true; CloseNoteButton.IsVisible = true;
        RenderTabs();
    }

    private void WriteClicked(object? sender, EventArgs e) => SetMode(ViewMode.Write);
    private void PreviewClicked(object? sender, EventArgs e) => SetMode(ViewMode.Preview);
    private void SplitClicked(object? sender, EventArgs e) => SetMode(ViewMode.Split);
    private void SetMode(ViewMode value, bool render = true)
    {
        mode = value; MessagePanel.IsVisible = currentNote is null;
        EditorPane.IsVisible = currentNote is not null && value != ViewMode.Preview;
        Reader.IsVisible = currentNote is not null && value != ViewMode.Write;
        ReaderPane.IsVisible = Reader.IsVisible;
        Grid.SetColumn(EditorPane, 0); Grid.SetColumnSpan(EditorPane, value == ViewMode.Write ? 2 : 1);
        Grid.SetColumn(ReaderPane, value == ViewMode.Preview ? 0 : 1); Grid.SetColumnSpan(ReaderPane, value == ViewMode.Preview ? 2 : 1);
        EditorPane.Margin = value == ViewMode.Write ? new Thickness(4, 0, 4, 6) : new Thickness(4, 0, 6, 6);
        ReaderPane.Margin = value == ViewMode.Preview ? new Thickness(4, 0, 4, 6) : new Thickness(6, 0, 4, 6);
        SetModeButtonState(WriteButton, value == ViewMode.Write);
        SetModeButtonState(PreviewButton, value == ViewMode.Preview);
        SetModeButtonState(SplitButton, value == ViewMode.Split);
        if (render && currentNote is not null && value != ViewMode.Write) _ = DebouncedPreviewAsync(MarkdownEditor.Text ?? "", connection.Token);
        if (value != ViewMode.Preview) MarkdownEditor.Focus();
    }

    private static void SetModeButtonState(Button button, bool selected)
    {
        button.BackgroundColor = selected ? Color.FromArgb("#334768") : Colors.Transparent;
        button.TextColor = selected ? Color.FromArgb("#F2F3F7") : Color.FromArgb("#9C9EAE");
    }

    private async void SettingsClicked(object? sender, EventArgs e) => await OpenSettingsAsync();
    private void SyncStatusTapped(object? sender, TappedEventArgs e) => SyncClicked(sender, e);
    private void SyncStatusPointerEntered(object? sender, Microsoft.Maui.Controls.PointerEventArgs e) => SyncStatusSurface.BackgroundColor = Color.FromArgb("#333333");
    private void SyncStatusPointerExited(object? sender, Microsoft.Maui.Controls.PointerEventArgs e) => SyncStatusSurface.BackgroundColor = Colors.Transparent;

    private void LinksTabClicked(object? sender, EventArgs e) => SetRailTab(true);
    private void InfoTabClicked(object? sender, EventArgs e) => SetRailTab(false);

    private void SetRailTab(bool linksSelected)
    {
        OutlineRail.IsVisible = false;
        LinksRail.IsVisible = linksSelected;
        InfoRail.IsVisible = !linksSelected;
        LinksTabIndicator.IsVisible = linksSelected;
        InfoTabIndicator.IsVisible = !linksSelected;
        LinksTabButton.TextColor = linksSelected ? Color.FromArgb("#DADADA") : Color.FromArgb("#A0A0A0");
        InfoTabButton.TextColor = linksSelected ? Color.FromArgb("#A0A0A0") : Color.FromArgb("#DADADA");
    }

    private void SetDocumentChromeVisible(bool visible)
    {
        NoteToolbar.IsVisible = visible;
        RightRail.IsVisible = rightSidebarOpen;
        RailDivider.IsVisible = rightSidebarOpen;
        RailDividerColumn.Width = rightSidebarOpen ? new GridLength(1) : new GridLength(0);
        RailColumn.Width = rightSidebarOpen ? new GridLength(260) : new GridLength(0);
    }

    private void UpdateLinkSidebar(NoteLinks? links)
    {
        backlinks.Clear(); outgoingLinks.Clear();
        if (links is not null)
        {
            foreach (var link in links.Backlinks)
                backlinks.Add(new(link.SourceId, link.SourceTitle, link.Heading is null ? link.SourcePath : $"{link.SourcePath} · {link.Heading}", true));
            foreach (var link in links.Outgoing)
            {
                var title = link.TargetTitle ?? link.Label ?? link.Target;
                var detail = link.State == "resolved" ? link.TargetPath ?? "Resolved note" : link.State.Replace('-', ' ');
                outgoingLinks.Add(new(link.TargetId, title, detail, link.State == "resolved"));
            }
        }
        BacklinksHeading.Text = $"Linked mentions · {backlinks.Count}";
        OutgoingHeading.Text = $"Outgoing links · {outgoingLinks.Count}";
    }

    private void UpdateInfoPanel(string markdown)
    {
        InfoPath.Text = currentNote?.Path ?? "No note selected";
        InfoRevision.Text = currentNote?.Revision ?? "—";
        var normalized = NoteDocument.NormalizeLineEndings(markdown);
        var plain = normalized;
        try { var document = NoteDocument.Parse(normalized, currentNote?.Path ?? "Untitled.md", allowMissingId: true); normalized = document.Body; plain = document.PlainText; }
        catch (InvalidDataException) { }
        var words = plain.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        InfoWords.Text = $"{words} word{(words == 1 ? "" : "s")} · {normalized.Length} characters";
        WorkspaceStats.Text = InfoWords.Text;
        RenderTabs();
    }

    private async void BacklinkSelected(object? sender, SelectionChangedEventArgs e) => await NavigateFromRailAsync(BacklinksList, e);
    private async void OutgoingLinkSelected(object? sender, SelectionChangedEventArgs e) => await NavigateFromRailAsync(OutgoingList, e);

    private async Task NavigateFromRailAsync(CollectionView list, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not LinkSidebarItem { NoteId: { } id }) return;
        list.SelectedItem = null;
        if (await ResolveUnsavedAsync()) await OpenNote(token => api.ReadAsync(id, token));
    }

    private string TargetFolder(TreeRow? row = null)
    {
        row ??= selectedRow;
        if (row is null || row.MoreFor is not null) return "";
        return row.Entry.IsDirectory ? row.Entry.Path : (row.Entry.Path.Contains('/') ? row.Entry.Path[..row.Entry.Path.LastIndexOf('/')] : "");
    }

    private async void NewNoteClicked(object? sender, EventArgs e) => await NewNoteAsync(TargetFolder());
    private async void NewMenuClicked(object? sender, EventArgs e)
    {
        var choice = await SlateDialogs.ChooseAsync(this, "New", "Cancel", null, "New note", "New folder", "Quick Thought", "Question", "Add Link");
        if (choice == "New note") await NewNoteAsync(TargetFolder());
        else if (choice == "New folder") await NewFolderAsync(TargetFolder());
        else if (choice == "Quick Thought") await QuickThoughtAsync();
        else if (choice == "Question") await CreateKnowledgeAsync("question");
        else if (choice == "Add Link") await CreateKnowledgeAsync("link");
    }
    private async Task NewNoteAsync(string folder)
    {
        if (!await ResolveUnsavedAsync()) return;
        var name = await SlateDialogs.PromptAsync(this, "New note", "Create in " + (folder.Length == 0 ? "Library" : folder), accept: "Create", cancel: "Cancel", placeholder: "My note");
        if (string.IsNullOrWhiteSpace(name)) return;
        var id = Guid.NewGuid(); var template = await KnowledgeDialogs.TemplateAsync(this, api, id, Path.GetFileNameWithoutExtension(name));
        if (template.Cancelled) return;
        await RunMutationAsync(async () =>
        {
            var request = new CreateNoteRequest(folder, name, Path.GetFileNameWithoutExtension(name), id, template.Markdown);
            var note = clientState is null ? await api.CreateNoteAsync(request, connection.Token) : await OfflineActions.CreateAsync(clientState, api, request, [], connection.Token);
            await ReloadTreeAsync(note.Path); await OpenNote(token => api.ReadAsync(note.Id, token));
        }, "Unable to create note.");
    }

    private async void NewFolderClicked(object? sender, EventArgs e) => await NewFolderAsync(TargetFolder());
    private async Task NewFolderAsync(string parent)
    {
        var name = await SlateDialogs.PromptAsync(this, "New folder", "Name", accept: "Create", cancel: "Cancel", placeholder: "New folder");
        if (string.IsNullOrWhiteSpace(name)) return;
        await RunMutationAsync(async () => { var created = await api.CreateFolderAsync(new(parent, name), connection.Token); await ReloadTreeAsync(created.Path); }, "Unable to create folder.");
    }

    private async Task RenameAsync(TreeRow row)
    {
        if (!await ResolveUnsavedAsync()) return;
        var name = await SlateDialogs.PromptAsync(this, "Rename", "New name", accept: "Rename", cancel: "Cancel", initialValue: row.Entry.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        await RunMutationAsync(async () =>
        {
            var changed = await LinkManagementViews.MoveAsync(this, api, row.Entry.Path, "", name); if (changed is null) return;
            if (row.Entry.IsDirectory && clientState is not null) { await clientState.Workspace.RemapFoldersAsync(changed.Items); await RefreshPinnedFoldersAsync(); }
            await ReloadTreeAsync(changed.Items[0].DestinationPath);
            if (openedNoteId is { } id) await OpenNote(token => api.ReadAsync(id, token));
        }, "Unable to rename item.");
    }

    private void SetClipboard(TreeRow row, bool cut)
    {
        if (explorerSelection.Count > 1 && explorerSelection.Paths.Contains(row.Entry.Path))
        { explorerClipboard = explorerSelection.Paths.ToArray(); clipboardCut = cut; ClipboardStatus.Text = (cut ? "Cut: " : "Copied: ") + explorerClipboard.Length + " items"; return; }
        explorerClipboard = null;
        clipboardPath = row.Entry.Path; clipboardCut = cut;
        ClipboardStatus.Text = (cut ? "Cut: " : "Copied: ") + row.Entry.Name;
    }

    private async Task PasteAsync(string folder)
    {
        if (explorerClipboard is { } selected)
        { await RunBulkAsync(clipboardCut ? "move" : "copy", folder, selected); return; }
        if (clipboardPath is null) { Status.Text = "Nothing has been copied or cut."; return; }
        if (clipboardCut) { await RunBulkAsync("move", folder, [clipboardPath]); return; }
        if (!await ResolveUnsavedAsync()) return;
        await RunMutationAsync(async () =>
        {
            var request = new TransferItemRequest(clipboardPath, folder);
            var changed = clipboardCut ? await api.MoveAsync(request, connection.Token) : await api.CopyAsync(request, connection.Token);
            if (clipboardCut && changed.IsDirectory && clientState is not null) { await clientState.Workspace.RemapFoldersAsync([new(clipboardPath, changed.Path, true)]); await RefreshPinnedFoldersAsync(); }
            if (clipboardCut) { clipboardPath = null; ClipboardStatus.Text = ""; }
            await ReloadTreeAsync(changed.Path);
            if (openedNoteId is { } id) await OpenNote(token => api.ReadAsync(id, token));
        }, clipboardCut ? "Unable to move item." : "Unable to copy item.");
    }

    private async Task DuplicateAsync(TreeRow row)
    {
        if (!await ResolveUnsavedAsync()) return;
        await RunMutationAsync(async () =>
        {
            var changed = await api.CopyAsync(new(row.Entry.Path, row.Entry.Path.Contains('/') ? row.Entry.Path[..row.Entry.Path.LastIndexOf('/')] : ""), connection.Token);
            await ReloadTreeAsync(changed.Path);
        }, "Unable to duplicate item.");
    }

    private async Task DeleteAsync(TreeRow row)
    {
        if (!await ResolveUnsavedAsync()) return;
        await RunMutationAsync(async () =>
        {
            var details = await api.DetailsAsync(row.Entry.Path, connection.Token);
            var description = details.IsDirectory && details.DescendantCount > 0
                ? $"Permanently delete '{row.Entry.Name}' and its {details.DescendantCount} descendant item(s)?"
                : $"Permanently delete '{row.Entry.Name}'?";
            if (!await SlateDialogs.AlertAsync(this, "Delete", description, "Delete", "Cancel")) return;
            await api.DeleteAsync(new(row.Entry.Path, details.IsDirectory), connection.Token);
            if (openedNoteId == row.Entry.Id || (details.IsDirectory && currentNote?.Path.StartsWith(row.Entry.Path + "/", StringComparison.OrdinalIgnoreCase) == true)) ResetDocument();
            await ReloadTreeAsync(TargetFolder(row));
        }, "Unable to delete item.");
    }

    private async Task RunMutationAsync(Func<Task> action, string fallback)
    {
        Busy.IsRunning = true;
        try { await action(); Status.Text = "Connected"; }
        catch (Exception exception) { ShowOperationError(exception, fallback); }
        finally { Busy.IsRunning = false; }
    }

    private static TreeRow RowFrom(object? sender) => (TreeRow)((MenuFlyoutItem)sender!).CommandParameter!;
    private async void ContextOpen(object? sender, EventArgs e) { var row = RowFrom(sender); rebuilding = true; SetSelectedRow(row); Tree.SelectedItem = row; rebuilding = false; if (row.Entry.IsDirectory) { if (!row.Loaded) await LoadChildren(row, 0, connection.Token); row.Expanded = !row.Expanded; RebuildRows(row.Entry.Path); } else if (await ResolveUnsavedAsync()) await OpenNote(token => api.ReadAsync(row.Entry.Id!.Value, token)); }
    private async void ContextNewNote(object? sender, EventArgs e) => await NewNoteAsync(TargetFolder(RowFrom(sender)));
    private async void ContextNewFolder(object? sender, EventArgs e) => await NewFolderAsync(TargetFolder(RowFrom(sender)));
    private async void ContextRename(object? sender, EventArgs e) => await RenameAsync(RowFrom(sender));
    private void ContextCut(object? sender, EventArgs e) => SetClipboard(RowFrom(sender), true);
    private void ContextCopy(object? sender, EventArgs e) => SetClipboard(RowFrom(sender), false);
    private async void ContextPaste(object? sender, EventArgs e) => await PasteAsync(TargetFolder(RowFrom(sender)));
    private async void ContextDuplicate(object? sender, EventArgs e) { var row = RowFrom(sender); if (explorerSelection.Count > 1 && explorerSelection.Paths.Contains(row.Entry.Path)) await RunBulkAsync("duplicate"); else await DuplicateAsync(row); }
    private async void ContextDelete(object? sender, EventArgs e) { var row = RowFrom(sender); if (explorerSelection.Count > 1 && explorerSelection.Paths.Contains(row.Entry.Path)) await RunBulkAsync("delete"); else await DeleteAsync(row); }
    private async void ContextMove(object? sender, EventArgs e) { var row = RowFrom(sender); await RunBulkAsync("move", pathsOverride: explorerSelection.Paths.Contains(row.Entry.Path) ? null : [row.Entry.Path]); }

    private void SetSelectedRow(TreeRow? row)
    {
        selectedRow = row;
        RefreshSelectionAppearance();
    }

    private void TreeRowPointerEntered(object? sender, Microsoft.Maui.Controls.PointerEventArgs e)
    {
        if (sender is PointerGestureRecognizer { BindingContext: TreeRow row }) row.Hovered = true;
    }

    private void TreeRowPointerExited(object? sender, Microsoft.Maui.Controls.PointerEventArgs e)
    {
        if (sender is PointerGestureRecognizer { BindingContext: TreeRow row }) row.Hovered = false;
    }

    private async void RowOverflowClicked(object? sender, EventArgs e)
    {
        if (sender is not ImageButton { BindingContext: TreeRow row }) return;
        try
        {
            if (explorerSelection.Count > 1 && explorerSelection.Paths.Contains(row.Entry.Path)) { await SelectedActionsAsync(); return; }
            var actions = new List<string> { "Open", "New note here", "New folder here", "Rename", "Cut", "Copy", "Paste here", "Duplicate", "Delete…" };
            actions.Add(row.Entry.IsDirectory ? "Pin / unpin folder" : "Favorite / unfavorite");
            var action = await SlateDialogs.ChooseAsync(this, row.Name, "Cancel", null, actions.ToArray());
            switch (action)
            {
                case "Open":
                    if (row.Entry.IsDirectory)
                    {
                        if (!row.Loaded) await LoadChildren(row, 0, connection.Token);
                        row.Expanded = !row.Expanded; RebuildRows(row.Entry.Path);
                    }
                    else if (await ResolveUnsavedAsync()) await OpenNote(t => api.ReadAsync(row.Entry.Id!.Value, t));
                    break;
                case "New note here": await NewNoteAsync(TargetFolder(row)); break;
                case "New folder here": await NewFolderAsync(TargetFolder(row)); break;
                case "Rename": await RenameAsync(row); break;
                case "Cut": SetClipboard(row, true); break;
                case "Copy": SetClipboard(row, false); break;
                case "Paste here": await PasteAsync(TargetFolder(row)); break;
                case "Duplicate": await DuplicateAsync(row); break;
                case "Delete…": await DeleteAsync(row); break;
                case "Pin / unpin folder": await ToggleFolderPinAsync(row.Entry.Path); break;
                case "Favorite / unfavorite": if (clientState is not null && row.Entry.Id is { } id) await clientState.Workspace.ToggleFavoriteAsync(id, row.Entry.Title ?? row.Entry.Name); break;
            }
        }
        catch (Exception exception) { ShowOperationError(exception, "The item menu could not complete this action."); }
    }
    private void TreeHandlerChanged(object? sender, EventArgs e)
    {
#if WINDOWS
        if (Tree.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement element) element.KeyDown += TreeKeyDown;
#endif
    }

#if WINDOWS
    private async void TreeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        if (e.Key is VirtualKey.Up or VirtualKey.Down)
        {
            e.Handled = true; var visible = rows.Where(r => r.MoreFor is null).ToArray(); if (visible.Length == 0) return;
            var index = Array.IndexOf(visible, selectedRow); index = Math.Clamp(index + (e.Key == VirtualKey.Up ? -1 : 1), 0, visible.Length - 1);
            selectedRow = visible[index]; explorerSelection.Select(selectedRow.Entry.Path, visible.Select(r => r.Entry.Path).ToArray(), ctrl && shift, shift);
            RefreshSelectionAppearance(); Tree.ScrollTo(selectedRow);
        }
        else if (e.Key == VirtualKey.Enter && selectedRow is not null) { e.Handled = true; await OpenExplorerRowAsync(selectedRow); }
        else if (ctrl && e.Key == VirtualKey.A) { e.Handled = true; explorerSelection.SelectAll(VisibleExplorerPaths()); RefreshSelectionAppearance(); }
        else if (e.Key == VirtualKey.Escape && explorerSelection.Count > 0) { e.Handled = true; explorerSelection.Clear(); RefreshSelectionAppearance(); }
        else if (e.Key == VirtualKey.Delete && explorerSelection.Count > 1) { e.Handled = true; await RunBulkAsync("delete"); }
        else if (ctrl && e.Key == VirtualKey.N) { e.Handled = true; if (shift) await NewFolderAsync(TargetFolder()); else await NewNoteAsync(TargetFolder()); }
        else if (ctrl && e.Key == VirtualKey.C && selectedRow is not null) { e.Handled = true; SetClipboard(selectedRow, false); }
        else if (ctrl && e.Key == VirtualKey.X && selectedRow is not null) { e.Handled = true; SetClipboard(selectedRow, true); }
        else if (ctrl && e.Key == VirtualKey.V) { e.Handled = true; await PasteAsync(TargetFolder()); }
        else if (ctrl && e.Key == VirtualKey.D && selectedRow is not null) { e.Handled = true; await RunBulkAsync("duplicate"); }
        else if (e.Key == VirtualKey.F2 && selectedRow is not null && explorerSelection.Count <= 1) { e.Handled = true; await RenameAsync(selectedRow); }
        else if (e.Key == VirtualKey.Delete && selectedRow is not null) { e.Handled = true; await DeleteAsync(selectedRow); }
    }
#endif

    private void EditorHandlerChanged(object? sender, EventArgs e)
    {
#if WINDOWS
        if (MarkdownEditor.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement element) element.KeyDown += EditorKeyDown;
#endif
    }

#if WINDOWS
    private async void EditorKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled) return;
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        var editingCommand = ctrl ? e.Key switch
        {
            VirtualKey.B => "bold", VirtualKey.I => "italic", VirtualKey.K => shift ? "wiki" : "link",
            (VirtualKey)192 => "code", VirtualKey.Number7 when shift => "number", VirtualKey.Number8 when shift => "bullet", VirtualKey.Number9 when shift => "task", _ => null
        } : null;
        if (editingCommand is not null) { e.Handled = true; ApplyMarkdownCommand(editingCommand); }
        else if (ctrl && e.Key == VirtualKey.S) { e.Handled = true; await SaveAsync(); }
        else if (ctrl && e.Key == VirtualKey.V)
        {
            var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Bitmap))
            {
                e.Handled = true;
                try
                {
                    var reference = await content.GetBitmapAsync();
                    using var random = await reference.OpenReadAsync();
                    using var stream = random.AsStreamForRead();
                    await StageAssetAsync(stream, "clipboard.png", "image/png", true);
                }
                catch (Exception exception) { ShowOperationError(exception, "Unable to paste the image."); }
            }
        }
    }
#endif

    private void PageHandlerChanged(object? sender, EventArgs e)
    {
#if WINDOWS
        HookWindowClose();
        if (pageKeysHooked || Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement element) return;
        element.PreviewKeyDown += PageKeyDown;
        RegisterDesktopShortcuts(element);
        pageKeysHooked = true;
#endif
    }

#if WINDOWS
    private async void PageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled) return;
        if (e.Key == VirtualKey.Escape && SlateDialogs.TryDismiss(this)) { e.Handled = true; return; }
        if (DesktopDialogs.IsOpen(this)) return;
        if (SearchPanel.IsVisible && e.Key is VirtualKey.Down or VirtualKey.Up or VirtualKey.Enter)
        {
            var hits = SearchResults.ItemsSource?.Cast<SearchHit>().ToArray() ?? [];
            if (hits.Length == 0) return;
            e.Handled = true;
            var index = Array.IndexOf(hits, SearchResults.SelectedItem as SearchHit);
            if (e.Key == VirtualKey.Enter) await OpenSearchHitAsync(hits[Math.Max(0, index)]);
            else
            {
                index = Math.Clamp(index + (e.Key == VirtualKey.Down ? 1 : -1), 0, hits.Length - 1);
                searchKeyboardNavigating = true;
                try { SearchResults.SelectedItem = hits[index]; SearchResults.ScrollTo(index); }
                finally { searchKeyboardNavigating = false; }
            }
            return;
        }
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        if (ctrl && e.Key == VirtualKey.K && MarkdownEditor.IsFocused) { e.Handled = true; ApplyMarkdownCommand(shift ? "wiki" : "link"); }
        else if (ctrl && (e.Key == VirtualKey.K || (shift && e.Key == VirtualKey.F))) { e.Handled = true; OpenSearch(); }
        if (ctrl && e.Key == VirtualKey.P) { e.Handled = true; CommandPaletteClicked(this, EventArgs.Empty); }
        if (ctrl && e.Key == VirtualKey.N) { e.Handled = true; if (shift) await NewFolderAsync(TargetFolder()); else await NewNoteAsync(TargetFolder()); }
        if (ctrl && e.Key == VirtualKey.S) { e.Handled = true; await SaveAsync(); }
        if (ctrl && e.Key == VirtualKey.W && currentNote is not null) { e.Handled = true; await CloseTabAsync(currentNote.Id); }
        if (ctrl && e.Key == VirtualKey.E && currentNote is not null) { e.Handled = true; SetMode(mode == ViewMode.Preview ? ViewMode.Write : ViewMode.Preview); }
        if (ctrl && e.Key == VirtualKey.Tab) { e.Handled = true; await CycleTabAsync(shift ? -1 : 1); }
        if (e.Key == VirtualKey.Escape && SearchPanel.IsVisible) { e.Handled = true; CloseSearchClicked(this, EventArgs.Empty); }
        else if (e.Key == VirtualKey.Escape && HistoryPanel.IsVisible) { e.Handled = true; CloseHistoryClicked(this, EventArgs.Empty); }
    }
    private void HookWindowClose()
    {
        if (closeHooked || Window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native) return;
        var titleBar = native.AppWindow.TitleBar;
        var icon = Path.Combine(AppContext.BaseDirectory, "slateicon.ico");
        if (File.Exists(icon)) native.AppWindow.SetIcon(icon);
        titleBar.BackgroundColor = Windows.UI.Color.FromArgb(255, 34, 36, 45);
        titleBar.ForegroundColor = Windows.UI.Color.FromArgb(255, 241, 243, 249);
        titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(255, 34, 36, 45);
        titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 155, 163, 184);
        titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 55, 58, 72);
        titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 241, 243, 249);
        native.AppWindow.Closing += WindowClosing;
        closeHooked = true;
    }

    private async void WindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (allowWindowClose || !dirty) return;
        args.Cancel = true;
        if (await ResolveUnsavedAsync()) { allowWindowClose = true; sender.Destroy(); }
    }
#else
    private void HookWindowClose() { }
#endif

    private void SearchClicked(object? sender, EventArgs e) => OpenSearch();
    private async void InboxClicked(object? sender, EventArgs e) => await OpenFolderViewAsync("inbox", "Inbox");
    private async void RecentClicked(object? sender, EventArgs e)
    {
        if (clientState is null) { Status.Text = "Connect before opening Recent."; return; }
        var recent = await clientState.ReadRecentsAsync();
        ShowResultView("Recent", recent.Select(x => new SearchHit(x.Id, x.Title, x.Path, "Recently opened on this device", 0, "")).ToArray(), recent.Count == 0 ? "No recently opened notes." : "");
    }

    private async void QuickThoughtClicked(object? sender, EventArgs e) => await QuickThoughtAsync();

    private async Task QuickThoughtAsync()
    {
        var text = await SlateDialogs.PromptAsync(this, "Quick Thought", "Save a thought to Inbox", accept: "Save", cancel: "Cancel", placeholder: "What are you thinking?");
        if (string.IsNullOrWhiteSpace(text)) return;
        var capture = new CaptureNoteRequest(Guid.NewGuid(), text, CapturedAt: DateTimeOffset.UtcNow);
        if (clientState is not null)
        {
            var action = await SlateDialogs.ChooseAsync(this, "Save thought", "Cancel", null, "Save to Inbox", "Append to note");
            if (action == "Cancel") return;
            if (action == "Append to note")
            {
                await clientState.SaveDraftAsync(new(capture.CaptureId, DraftKind.QuickThought, capture.Content, DateTimeOffset.UtcNow, PendingCapture: capture, ReadyToSubmit: false));
                try
                {
                    var saved = await KnowledgeDialogs.AppendDraftAsync(this, api, clientState, capture, []);
                    if (saved is not null) { await clientState.DeleteDraftAsync(capture.CaptureId); await OpenNote(_ => Task.FromResult(saved)); }
                }
                catch (Exception exception) { ShowOperationError(exception, "The append was not confirmed. Review the saved drafts before retrying."); }
                return;
            }
        }
        try
        {
            if (clientState is null) await api.CaptureAsync(capture, connection.Token);
            else await OfflineActions.SaveAsync(clientState, api, new(capture.CaptureId, DraftKind.QuickThought, capture.Content, DateTimeOffset.UtcNow, PendingCapture: capture), new(Guid.NewGuid(), capture.CaptureId, "capture", capture.Content, Capture: capture), "inbox/capture.md", connection.Token);
            if (clientState is not null) await clientState.DeleteDraftAsync(capture.CaptureId);
            Status.Text = "Saved to Inbox"; await ReloadTreeAsync("inbox");
        }
        catch (Exception exception)
        {
            if (clientState is not null)
            {
                await clientState.SaveDraftAsync(new(capture.CaptureId, DraftKind.QuickThought, capture.Content, DateTimeOffset.UtcNow, TargetPath: "inbox", PendingCapture: capture));
                Status.Text = "Saved on this device · will retry when connected";
            }
            else ShowOperationError(exception, "Unable to save Quick Thought.");
        }
    }

    private async Task OpenFolderViewAsync(string path, string title)
    {
        try
        {
            var all = new List<SearchHit>(); var page = 0;
            do
            {
                var folder = await api.ListAsync(path, page, connection.Token);
                foreach (var entry in folder.Entries.Where(x => !x.IsDirectory && x.Id.HasValue))
                {
                    var note = await api.ReadAsync(entry.Id!.Value, connection.Token);
                    var body = NoteDocument.Parse(note.Markdown, note.Path).PlainText;
                    var heading = body.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? note.Title;
                    if (heading.Length > 80) heading = heading[..80] + "…";
                    all.Add(new(note.Id, heading, note.Path, body, 0, ""));
                }
                if (folder.NextPage is null) break; page = folder.NextPage.Value;
            } while (true);
            ShowResultView(title, all, all.Count == 0 ? "Inbox is empty." : "");
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound) { ShowResultView(title, [], "Inbox is empty."); }
        catch (Exception exception) { ShowOperationError(exception, "Unable to open Inbox."); }
    }

    private void ShowResultView(string title, IReadOnlyList<SearchHit> results, string empty)
    {
        searchDelay?.Cancel();
        SearchEntry.IsVisible = false;
        SearchPaging.IsVisible = false; SearchFilterButton.IsVisible = false; searchRequest++;
        HistoryPanel.IsVisible = false;
        SearchPanel.IsVisible = true; SearchSummary.Text = title; SearchResults.ItemsSource = results; SearchEmpty.Text = empty;
    }
    private void OpenSearch()
    {
        SearchEntry.IsVisible = true; SearchSummary.Text = "Find a note";
        SearchFilterButton.IsVisible = true;
        HistoryPanel.IsVisible = false;
        SearchPanel.IsVisible = true;
        SearchEntry.Focus();
        if (!string.IsNullOrWhiteSpace(SearchEntry.Text)) _ = SearchAsync(SearchEntry.Text, CancellationToken.None);
    }

    private void SearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        searchDelay?.Cancel(); searchDelay?.Dispose(); searchDelay = new();
        var token = searchDelay.Token;
        HistoryPanel.IsVisible = false;
        SearchPanel.IsVisible = true;
        _ = DebouncedSearchAsync(e.NewTextValue ?? "", token);
    }

    private async Task DebouncedSearchAsync(string query, CancellationToken token)
    {
        try { await Task.Delay(200, token); await SearchAsync(query, token); }
        catch (OperationCanceledException) { }
    }

    private async Task SearchAsync(string query, CancellationToken token, int requestedPage = 0)
    {
        var request = ++searchRequest; SearchPaging.IsVisible = false;
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchResults.ItemsSource = Array.Empty<SearchHit>(); SearchSummary.Text = "Search"; SearchEmpty.Text = "Type a query to search the Library."; return;
        }
        SearchSummary.Text = "Searching…";
        try
        {
            var page = await api.SearchAsync(query, requestedPage, token);
            if (request != searchRequest || token.IsCancellationRequested) return;
            activeSearchQuery = query; searchPage = requestedPage;
            SearchPaging.IsVisible = page.Total > 20; SearchPrevious.IsEnabled = searchPage > 0;
            SearchNext.IsEnabled = (searchPage + 1) * 20 < page.Total && searchPage < 1999;
            SearchPageLabel.Text = $"Page {searchPage + 1}";
            SearchResults.ItemsSource = page.Results;
            SearchSummary.Text = $"Search · {page.Total} result{(page.Total == 1 ? "" : "s")}";
            SearchEmpty.Text = "No matching notes.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (request != searchRequest) return;
            SearchResults.ItemsSource = Array.Empty<SearchHit>(); SearchSummary.Text = "Search needs attention";
            SearchEmpty.Text = exception is HttpRequestException ? exception.Message : "Search is temporarily unavailable.";
        }
    }

    private async void SearchResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (searchKeyboardNavigating) return;
        if (e.CurrentSelection.FirstOrDefault() is not SearchHit hit) return;
        await OpenSearchHitAsync(hit);
    }

    private async Task OpenSearchHitAsync(SearchHit hit)
    {
        SearchResults.SelectedItem = null;
        if (await ResolveUnsavedAsync()) await OpenNote(async token =>
        {
            try { return await api.ReadAsync(hit.Id, token); }
            catch (HttpRequestException) when (clientState is not null)
            {
                var cached = await clientState.ReadCachedNoteAsync(hit.Id, token);
                if (cached is null) throw;
                return cached.Note;
            }
        });
    }

    private void CloseSearchClicked(object? sender, EventArgs e)
    {
        SearchPanel.IsVisible = false;
        if (currentNote is null) ShowMessage("Choose a note", "Expand a folder or create a Markdown note."); else SetMode(mode);
    }

    private async void SyncClicked(object? sender, EventArgs e)
    {
        Busy.IsRunning = true; UpdateSyncState("Syncing…");
        try
        {
            var state = await api.SyncAsync(connection.Token); UpdateSyncState(GitLabel(state));
            await ReloadTreeAsync(currentNote?.Path);
            if (currentNote is not null && !dirty)
            {
                try { await OpenNote(token => api.ReadAsync(currentNote.Id, token)); }
                catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound) { ResetDocument(); }
            }
        }
        catch (Exception exception) { UpdateSyncState("Sync error"); ShowOperationError(exception, "Unable to synchronize Git."); }
        finally { Busy.IsRunning = false; }
    }

    private async void HistoryClicked(object? sender, EventArgs e)
    {
        if (currentNote is null || !await ResolveUnsavedAsync()) return;
        try { if (await HistoryBrowser.OpenAsync(this, api, currentNote.Id) is { } restored) await OpenNote(_ => Task.FromResult(restored)); }
        catch (Exception exception) { ShowOperationError(exception, "History action could not be completed. Current content was preserved unless a new revision was already saved."); }
    }
    private async void HistorySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (currentNote is null || e.CurrentSelection.FirstOrDefault() is not NoteHistoryEntry entry) return;
        Busy.IsRunning = true;
        try
        {
            var historical = await api.HistoricalNoteAsync(currentNote.Id, entry.Commit, connection.Token);
            HistoricalLabel.Text = $"{historical.Timestamp.LocalDateTime:g} · {historical.Message} · read-only";
            HistoricalEditor.Text = historical.Markdown;
        }
        catch (Exception exception) { HistoricalLabel.Text = "Historical version unavailable: " + exception.Message; }
        finally { Busy.IsRunning = false; }
    }

    private void CloseHistoryClicked(object? sender, EventArgs e) { HistoryPanel.IsVisible = false; SetMode(mode); }

    private static string GitLabel(GitSyncState? state) => state is null ? "Git unavailable" : state.State switch
    {
        "Synced" => "Synced",
        "Pending" or "Committed" => "Sync pending",
        "Syncing" => "Syncing…",
        "Conflict" => "Sync needs attention",
        "Error" => "Sync error",
        "LocalOnly" => "Committed locally",
        _ => "Git not initialized"
    };

    private async Task PollStatusAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            while (await timer.WaitForNextTickAsync(token))
            {
                var status = await api.StatusAsync(token);
                UpdateSyncState(GitLabel(status.Git));
                var libraryChanged = lastLibraryVersion is not null && status.LibraryVersion != lastLibraryVersion;
                var searchChanged = status.SearchVersion != lastSearchVersion;
                lastLibraryVersion = status.LibraryVersion; lastSearchVersion = status.SearchVersion;
                if (libraryChanged)
                {
                    if (dirty) Status.Text = "Library changed elsewhere · your unsaved draft is preserved";
                    else
                    {
                        var id = currentNote?.Id;
                        await ReloadTreeAsync(currentNote?.Path);
                        if (id is { } currentId && !dirty) await OpenNote(cancellation => api.ReadAsync(currentId, cancellation));
                    }
                }
                if (searchChanged && SearchPanel.IsVisible && !string.IsNullOrWhiteSpace(SearchEntry.Text))
                    await SearchAsync(SearchEntry.Text, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) { UpdateSyncState("Status unavailable"); }
    }

    private void UpdateSyncState(string label)
    {
        SyncState.Text = label;
        SyncIndicator.Color = label == "Synced" ? Color.FromArgb("#34D399")
            : label.Contains("error", StringComparison.OrdinalIgnoreCase) || label.Contains("attention", StringComparison.OrdinalIgnoreCase)
                ? Color.FromArgb("#EF6B73")
                : label.Contains("Syncing", StringComparison.OrdinalIgnoreCase) || label.Contains("pending", StringComparison.OrdinalIgnoreCase)
                    ? Color.FromArgb("#F0B35A")
                    : Color.FromArgb("#808080");
    }

    private void ShowMessage(string title, string detail) { Reader.IsVisible = ReaderPane.IsVisible = EditorPane.IsVisible = SearchPanel.IsVisible = HistoryPanel.IsVisible = false; MessagePanel.IsVisible = true; MessageTitle.Text = title; MessageDetail.Text = detail; }
    private void ShowOperationError(Exception exception, string fallback)
    {
        Status.Text = exception is HttpRequestException ? exception.Message : fallback + " " + exception.Message;
        FeedbackText.Text = Status.Text; FeedbackBanner.IsVisible = true;
        DocumentState.Text = dirty ? "Unsaved · needs attention" : DocumentState.Text;
    }
    private void ShowError(Exception exception)
    {
        var message = exception switch
        {
            HttpRequestException { StatusCode: not null } => exception.Message,
            HttpRequestException => "Cannot reach the server. Check the URL and that the backend is running, then refresh.",
            OperationCanceledException => "The server took too long to respond. Try refreshing.",
            ArgumentException => exception.Message,
            _ => "The operation could not be completed. Check the server log and try refreshing."
        };
        Status.Text = "Unable to load";
        if (currentNote is null) ShowMessage("Something needs attention", message); else DocumentState.Text = message;
    }

    private async void ReaderNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (loadingDocument) { loadingDocument = false; return; }
        e.Cancel = true;
        if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme == "file") { e.Cancel = false; return; }
        if (uri.Scheme == "slate-command")
        {
#if WINDOWS
            await HandleReaderCommandAsync(uri.Host);
#endif
        }
        else if (uri.Scheme == "slate-note" && Guid.TryParse(uri.Host, out var id))
        {
            pendingHeading = uri.Fragment;
            if (await ResolveUnsavedAsync()) await OpenNote(token => api.ReadAsync(id, token));
        }
        else if (uri.Scheme == "slate-asset" && Guid.TryParse(uri.Host, out var assetId))
        {
            try
            {
                var local = clientState is null ? null : await clientState.Offline.ReadAssetAsync(assetId);
                var metadata = local?.Metadata ?? await api.AssetMetadataAsync(assetId, connection.Token);
                var directory = Path.Combine(FileSystem.CacheDirectory, "slate.lib", "open-assets"); Directory.CreateDirectory(directory);
                var filename = assetId.ToString("D") + metadata.Extension; var destination = Path.Combine(directory, filename);
                if (local is { } downloaded) await File.WriteAllBytesAsync(destination, downloaded.Bytes);
                else await api.DownloadAssetAsync(assetId, destination, connection.Token);
                await Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(new OpenFileRequest(metadata.OriginalFilename, new ReadOnlyFile(destination)));
            }
            catch { Status.Text = "The attachment could not be opened."; }
        }
        else if (uri.Scheme == "https" && uri.Host == "slate.invalid")
        {
            if (await ResolveUnsavedAsync()) await OpenNote(token => api.ReadPathAsync(Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')), token));
        }
        else if (uri.Scheme is "http" or "https")
        {
            try { await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred); } catch { Status.Text = "The external link could not be opened."; }
        }
    }

    private async void ReaderHandlerChanged(object? sender, EventArgs e)
    {
#if WINDOWS
        if (Reader.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 view) return;
        try
        {
            await view.EnsureCoreWebView2Async(); var core = view.CoreWebView2;
            core.Settings.IsScriptEnabled = true; core.Settings.AreHostObjectsAllowed = false; core.Settings.IsWebMessageEnabled = false; core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.AddWebResourceRequestedFilter("http*", Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) => args.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
            core.NewWindowRequested += (_, args) => args.Handled = true; core.DownloadStarting += (_, args) => args.Cancel = true; webReady.TrySetResult();
        }
        catch (Exception exception) { webReady.TrySetException(exception); }
#else
        webReady.TrySetResult();
        await Task.CompletedTask;
#endif
    }
}

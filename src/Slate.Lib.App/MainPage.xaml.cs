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
    public bool Expanded { get => expanded; set { expanded = value; PropertyChanged?.Invoke(this, new(nameof(Glyph))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool Loaded { get; set; }
    public int? NextPage { get; set; }
    public TreeRow? MoreFor { get; init; }
    public List<TreeRow> Children { get; } = [];
    public Thickness Indent => new(16 + Depth * 18, 0, 0, 0);
    public string Name => MoreFor is not null ? "Load more…" : Entry.Title ?? Entry.Name;
    public string Glyph => MoreFor is not null ? "+" : Entry.IsDirectory ? (Expanded ? "▾" : "▸") : "·";
}

public partial class MainPage : ContentPage
{
    private enum ViewMode { Write, Preview, Split }
    private readonly LibraryApiClient api;
    private readonly MarkdownReader renderer;
    private readonly ObservableCollection<TreeRow> rows = [];
    private TreeRow root = new() { Depth = -1, Expanded = true };
    private CancellationTokenSource connection = new();
    private CancellationTokenSource? reading;
    private CancellationTokenSource? previewDelay;
    private CancellationTokenSource? searchDelay;
    private CancellationTokenSource? autosaveDelay;
    private readonly TaskCompletionSource webReady = new();
    private bool firstAppearance = true;
    private bool rebuilding;
    private bool browsing;
    private bool loadingDocument;
    private bool settingEditor;
    private bool dirty;
    private Guid? openedNoteId;
    private LibraryNote? currentNote;
    private TreeRow? selectedRow;
    private string? clipboardPath;
    private bool clipboardCut;
    private ViewMode mode = ViewMode.Split;
#if WINDOWS
    private bool closeHooked;
    private bool allowWindowClose;
    private bool pageKeysHooked;
#endif
    private string? lastLibraryVersion;
    private long lastSearchVersion;
    private ClientStateStore? clientState;
    private DraftDebouncer? draftDebouncer;
    private PendingAssetStore? assetStore;
    private readonly List<PendingAsset> pendingAssets = [];
    private string? pendingHeading;

    public MainPage(LibraryApiClient api, MarkdownReader renderer)
    {
        this.api = api; this.renderer = renderer;
        InitializeComponent(); Tree.ItemsSource = rows;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        HookWindowClose();
        if (!firstAppearance) return;
        firstAppearance = false;
        ServerEntry.Text = Environment.GetEnvironmentVariable("SLATE_SERVER") ?? Preferences.Get("server", "http://localhost:5188");
        try { TokenEntry.Text = Environment.GetEnvironmentVariable("SLATE_TOKEN") ?? await SecureStorage.GetAsync("device-token"); }
        catch { Status.Text = "Saved token unavailable. Enter it again to connect."; }
        if (!string.IsNullOrWhiteSpace(TokenEntry.Text)) await ConnectAsync(false);
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
        root = new() { Depth = -1, Expanded = true }; rows.Clear(); selectedRow = null;
        browsing = false; Busy.IsRunning = true; Status.Text = "Connecting…";
        ShowMessage("Connecting to your library", "Fetching folders from the server…");
        try
        {
            api.Connect(ServerEntry.Text ?? "", TokenEntry.Text ?? "");
            if (refreshServer) await api.RefreshAsync(cancellation);
            var status = await api.StatusAsync(cancellation);
            clientState = new ClientStateStore(FileSystem.AppDataDirectory, FileSystem.CacheDirectory, status.LibraryId, 500L * 1024 * 1024);
            assetStore = new PendingAssetStore(FileSystem.AppDataDirectory, status.LibraryId);
            await new PendingCaptureProcessor(clientState).RetryAsync((asset, token) => PendingAssetStore.UploadAsync(api, asset, token), api.CaptureAsync, cancellation);
            await LoadChildren(root, 0, cancellation);
            RebuildRows();
            Preferences.Set("server", ServerEntry.Text);
            var stored = true;
            try { await SecureStorage.SetAsync("device-token", TokenEntry.Text ?? ""); } catch { stored = false; }
            Status.Text = $"Connected · server {status.ServerVersion} · search {status.IndexState}" + (stored ? "" : " · token could not be saved");
            SyncState.Text = GitLabel(status.Git);
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
        rebuilding = true; rows.Clear();
        void Append(TreeRow node)
        {
            foreach (var child in node.Children)
            {
                rows.Add(child);
                if (child.Expanded) Append(child);
            }
            if (node.NextPage.HasValue) rows.Add(new TreeRow { MoreFor = node, Depth = node.Depth + 1 });
        }
        Append(root);
        selectedRow = rows.FirstOrDefault(row => row.Entry.Path == selectPath) ??
                      rows.FirstOrDefault(row => !row.Entry.IsDirectory && row.Entry.Id == openedNoteId);
        Tree.SelectedItem = selectedRow;
        rebuilding = false;
    }

    private async Task ReloadTreeAsync(string? selectPath = null)
    {
        var expanded = rows.Where(x => x.Expanded).Select(x => x.Entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        root = new() { Depth = -1, Expanded = true };
        await LoadChildren(root, 0, connection.Token);
        async Task Restore(TreeRow parent)
        {
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
        selectedRow = row;
        if (row.MoreFor is null && !row.Entry.IsDirectory)
        {
            if (currentNote?.Id == row.Entry.Id) return;
            if (await ResolveUnsavedAsync()) await OpenNote(token => api.ReadAsync(row.Entry.Id!.Value, token));
            else { rebuilding = true; Tree.SelectedItem = rows.FirstOrDefault(x => x.Entry.Id == openedNoteId); rebuilding = false; }
            return;
        }
        if (browsing) return;
        browsing = true;
        try
        {
            if (row.MoreFor is { } parent) await LoadChildren(parent, parent.NextPage!.Value, connection.Token);
            else { if (!row.Loaded) await LoadChildren(row, 0, connection.Token); row.Expanded = !row.Expanded; }
            RebuildRows(row.Entry.Path); Status.Text = "Connected";
        }
        catch (Exception exception) { ShowError(exception); }
        finally { browsing = false; }
    }

    private async Task OpenNote(Func<CancellationToken, Task<LibraryNote>> fetch)
    {
        reading?.Cancel(); reading?.Dispose(); reading = CancellationTokenSource.CreateLinkedTokenSource(connection.Token);
        var cancellation = reading.Token;
        Busy.IsRunning = true; Status.Text = "Loading note…"; ShowMessage("Opening note", "Reading Markdown from your server…");
        try
        {
            var note = await fetch(cancellation);
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
                    if (draft.Markdown == note.Markdown) await clientState.DeleteDraftAsync(draft.DraftId, cancellation);
                    else if (await DisplayAlertAsync("Unsaved changes recovered", "Restore the local draft?", "Restore Draft", "Discard"))
                    {
                        markdown = draft.Markdown; dirty = true;
                        pendingAssets.AddRange(draft.PendingAssets ?? []);
                        if (!string.IsNullOrWhiteSpace(draft.BaseRevision)) currentNote = note with { Revision = draft.BaseRevision };
                    }
                    else await clientState.DeleteDraftAsync(draft.DraftId, cancellation);
                }
            }
            settingEditor = true; MarkdownEditor.Text = markdown; settingEditor = false;
            UpdateDocumentChrome(); SetMode(mode); await RenderPreviewAsync(markdown, cancellation);
            rebuilding = true; Tree.SelectedItem = rows.FirstOrDefault(row => row.Entry.Id == note.Id); selectedRow = Tree.SelectedItem as TreeRow; rebuilding = false;
            Status.Text = "Connected";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) { if (!cancellation.IsCancellationRequested) ShowError(exception); }
        finally { if (!cancellation.IsCancellationRequested) Busy.IsRunning = false; }
    }

    private void EditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (settingEditor || currentNote is null) return;
        dirty = true; UpdateDocumentChrome();
        draftDebouncer?.Schedule(CurrentDraft(e.NewTextValue ?? ""));
        autosaveDelay?.Cancel(); autosaveDelay?.Dispose(); autosaveDelay = new();
        _ = AutosaveAsync(autosaveDelay.Token);
        previewDelay?.Cancel(); previewDelay?.Dispose(); previewDelay = new();
        var token = previewDelay.Token;
        _ = DebouncedPreviewAsync(e.NewTextValue ?? "", token);
    }

    private DraftRecord CurrentDraft(string markdown) => new(currentNote!.Id, DraftKind.ExistingNote, markdown, DateTimeOffset.UtcNow,
        currentNote.Id, currentNote.Path, currentNote.Revision, currentNote.Markdown, PendingAssets: pendingAssets.ToArray());

    private async Task AutosaveAsync(CancellationToken token)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(2), token); if (dirty) await SaveAsync(); }
        catch (OperationCanceledException) { }
    }

    private async Task DebouncedPreviewAsync(string source, CancellationToken token)
    {
        try { await Task.Delay(350, token); if (mode != ViewMode.Write) await RenderPreviewAsync(source, token); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Status.Text = "Preview unavailable: " + exception.Message; }
    }

    private async Task RenderPreviewAsync(string source, CancellationToken cancellation)
    {
        if (currentNote is null) return;
        var draft = currentNote with { Markdown = source };
        NoteLinks? links = null; try { links = await api.LinksAsync(draft.Id, cancellation); } catch (HttpRequestException) { }
        var images = new Dictionary<Guid, string>();
        foreach (var id in AssetReferences.Extract(draft.Path, draft.Markdown))
        {
            try
            {
                var metadata = await api.AssetMetadataAsync(id, cancellation);
                if (!metadata.InlineImage) continue;
                var bytes = await api.ReadAssetBytesAsync(id, cancellation);
                images[id] = $"data:{metadata.ContentType};base64,{Convert.ToBase64String(bytes)}";
            }
            catch (HttpRequestException) { }
        }
        var path = await renderer.WriteDocumentAsync(FileSystem.CacheDirectory, draft, links, images, cancellation);
        await webReady.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellation);
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
        var query = await DisplayPromptAsync("Insert wiki link", "Search by note title", placeholder: "PostgreSQL");
        if (string.IsNullOrWhiteSpace(query)) return;
        try
        {
            var hits = (await api.SearchAsync(query, 0, connection.Token)).Results.Take(8).ToArray();
            if (hits.Length == 0) { Status.Text = "No matching notes."; return; }
            var labels = hits.Select(x => x.Title + " — " + x.Path).ToArray();
            var selected = await DisplayActionSheetAsync("Choose a note", "Cancel", null, labels);
            var index = Array.IndexOf(labels, selected); if (index < 0) return;
            var title = hits[index].Title.Replace("]", "", StringComparison.Ordinal);
            InsertMarkdown($"[[id:{hits[index].Id:D}|{title}]]");
        }
        catch (Exception exception) { ShowOperationError(exception, "Unable to search for links."); }
    }

    private void InsertMarkdown(string markdown)
    {
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
    }
    private async Task<bool> SaveAsync()
    {
        if (currentNote is null || !dirty) return true;
        Busy.IsRunning = true; DocumentState.Text = "Saving…";
        try
        {
            var sent = MarkdownEditor.Text ?? "";
            foreach (var asset in pendingAssets)
            {
                var metadata = await PendingAssetStore.UploadAsync(api, asset, connection.Token);
                sent = sent.Replace($"asset-pending://{asset.Id:D}", AssetReferences.PathForNote(currentNote.Path, asset.Id, metadata.Extension), StringComparison.Ordinal);
            }
            if (sent != MarkdownEditor.Text) { settingEditor = true; MarkdownEditor.Text = sent; settingEditor = false; }
            var saved = await api.UpdateNoteAsync(currentNote.Id, new(sent, currentNote.Revision), connection.Token);
            currentNote = saved;
            if ((MarkdownEditor.Text ?? "") == sent)
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
            var choice = await DisplayActionSheetAsync("This note changed elsewhere. Your draft was kept.", "Keep draft", null, "Open latest", "Save draft as new note");
            if (choice == "Open latest")
            {
                var latest = await api.ReadAsync(currentNote!.Id, connection.Token);
                draftDebouncer?.Clear(); currentNote = latest; settingEditor = true; MarkdownEditor.Text = latest.Markdown; settingEditor = false; dirty = false;
                await RenderPreviewAsync(latest.Markdown, connection.Token); UpdateDocumentChrome();
            }
            else if (choice == "Save draft as new note") return await SaveConflictAsNewAsync();
            return false;
        }
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
        var choice = await DisplayActionSheetAsync("Save changes?", "Cancel", null, "Save", "Discard");
        if (choice == "Save") return await SaveAsync();
        if (choice == "Discard")
        {
            dirty = false; draftDebouncer?.Clear();
            if (currentNote is not null && clientState is not null) await clientState.DeleteDraftAsync(currentNote.Id);
            return true;
        }
        return false;
    }

    private async void CloseNoteClicked(object? sender, EventArgs e) { if (await ResolveUnsavedAsync()) ResetDocument(); }
    private void ResetDocument()
    {
        currentNote = null; openedNoteId = null; dirty = false; settingEditor = true; MarkdownEditor.Text = ""; settingEditor = false; pendingAssets.Clear();
        draftDebouncer?.Clear(); draftDebouncer = null;
        NoteTitle.Text = "Your library, one note at a time"; Breadcrumb.Text = "Choose a note from the folders on the left.";
        AttachImageButton.IsEnabled = AttachFileButton.IsEnabled = InsertLinkButton.IsEnabled = HistoryButton.IsEnabled = WriteButton.IsEnabled = PreviewButton.IsEnabled = SplitButton.IsEnabled = SaveButton.IsEnabled = false;
        CloseNoteButton.IsVisible = false; DocumentState.Text = ""; ShowMessage("Choose a note", "Expand a folder or create a Markdown note.");
    }

    private void UpdateDocumentChrome()
    {
        if (currentNote is null) return;
        NoteTitle.Text = currentNote.Title + (dirty ? "  •" : ""); Breadcrumb.Text = "Library  ›  " + currentNote.Path.Replace("/", "  ›  ");
        DocumentState.Text = dirty ? "Unsaved" : "Saved ✓";
        AttachImageButton.IsEnabled = AttachFileButton.IsEnabled = InsertLinkButton.IsEnabled = HistoryButton.IsEnabled = WriteButton.IsEnabled = PreviewButton.IsEnabled = SplitButton.IsEnabled = SaveButton.IsEnabled = true; CloseNoteButton.IsVisible = true;
    }

    private void WriteClicked(object? sender, EventArgs e) => SetMode(ViewMode.Write);
    private void PreviewClicked(object? sender, EventArgs e) => SetMode(ViewMode.Preview);
    private void SplitClicked(object? sender, EventArgs e) => SetMode(ViewMode.Split);
    private void SetMode(ViewMode value)
    {
        mode = value; MessagePanel.IsVisible = currentNote is null;
        EditorPane.IsVisible = currentNote is not null && value != ViewMode.Preview;
        Reader.IsVisible = currentNote is not null && value != ViewMode.Write;
        Grid.SetColumn(EditorPane, 0); Grid.SetColumnSpan(EditorPane, value == ViewMode.Write ? 2 : 1);
        Grid.SetColumn(Reader, value == ViewMode.Preview ? 0 : 1); Grid.SetColumnSpan(Reader, value == ViewMode.Preview ? 2 : 1);
        if (currentNote is not null && value != ViewMode.Write) _ = DebouncedPreviewAsync(MarkdownEditor.Text ?? "", CancellationToken.None);
        if (value != ViewMode.Preview) MarkdownEditor.Focus();
    }

    private string TargetFolder(TreeRow? row = null)
    {
        row ??= selectedRow;
        if (row is null || row.MoreFor is not null) return "";
        return row.Entry.IsDirectory ? row.Entry.Path : (row.Entry.Path.Contains('/') ? row.Entry.Path[..row.Entry.Path.LastIndexOf('/')] : "");
    }

    private async void NewNoteClicked(object? sender, EventArgs e) => await NewNoteAsync(TargetFolder());
    private async Task NewNoteAsync(string folder)
    {
        if (!await ResolveUnsavedAsync()) return;
        var name = await DisplayPromptAsync("New note", "Name", accept: "Create", cancel: "Cancel", placeholder: "My note");
        if (string.IsNullOrWhiteSpace(name)) return;
        await RunMutationAsync(async () =>
        {
            var note = await api.CreateNoteAsync(new(folder, name, Path.GetFileNameWithoutExtension(name)), connection.Token);
            await ReloadTreeAsync(note.Path); await OpenNote(token => api.ReadAsync(note.Id, token));
        }, "Unable to create note.");
    }

    private async void NewFolderClicked(object? sender, EventArgs e) => await NewFolderAsync(TargetFolder());
    private async Task NewFolderAsync(string parent)
    {
        var name = await DisplayPromptAsync("New folder", "Name", accept: "Create", cancel: "Cancel", placeholder: "New folder");
        if (string.IsNullOrWhiteSpace(name)) return;
        await RunMutationAsync(async () => { var created = await api.CreateFolderAsync(new(parent, name), connection.Token); await ReloadTreeAsync(created.Path); }, "Unable to create folder.");
    }

    private async Task RenameAsync(TreeRow row)
    {
        if (!await ResolveUnsavedAsync()) return;
        var name = await DisplayPromptAsync("Rename", "New name", accept: "Rename", cancel: "Cancel", initialValue: row.Entry.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        await RunMutationAsync(async () =>
        {
            var changed = await api.RenameAsync(new(row.Entry.Path, name), connection.Token);
            await ReloadTreeAsync(changed.Path);
            if (openedNoteId is { } id) await OpenNote(token => api.ReadAsync(id, token));
        }, "Unable to rename item.");
    }

    private void SetClipboard(TreeRow row, bool cut)
    {
        clipboardPath = row.Entry.Path; clipboardCut = cut;
        ClipboardStatus.Text = (cut ? "Cut: " : "Copied: ") + row.Entry.Name;
    }

    private async Task PasteAsync(string folder)
    {
        if (clipboardPath is null) { Status.Text = "Nothing has been copied or cut."; return; }
        if (!await ResolveUnsavedAsync()) return;
        await RunMutationAsync(async () =>
        {
            var request = new TransferItemRequest(clipboardPath, folder);
            var changed = clipboardCut ? await api.MoveAsync(request, connection.Token) : await api.CopyAsync(request, connection.Token);
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
            if (!await DisplayAlertAsync("Delete", description, "Delete", "Cancel")) return;
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
    private async void ContextOpen(object? sender, EventArgs e) { var row = RowFrom(sender); rebuilding = true; Tree.SelectedItem = row; rebuilding = false; selectedRow = row; if (row.Entry.IsDirectory) { if (!row.Loaded) await LoadChildren(row, 0, connection.Token); row.Expanded = !row.Expanded; RebuildRows(row.Entry.Path); } else if (await ResolveUnsavedAsync()) await OpenNote(token => api.ReadAsync(row.Entry.Id!.Value, token)); }
    private async void ContextNewNote(object? sender, EventArgs e) => await NewNoteAsync(TargetFolder(RowFrom(sender)));
    private async void ContextNewFolder(object? sender, EventArgs e) => await NewFolderAsync(TargetFolder(RowFrom(sender)));
    private async void ContextRename(object? sender, EventArgs e) => await RenameAsync(RowFrom(sender));
    private void ContextCut(object? sender, EventArgs e) => SetClipboard(RowFrom(sender), true);
    private void ContextCopy(object? sender, EventArgs e) => SetClipboard(RowFrom(sender), false);
    private async void ContextPaste(object? sender, EventArgs e) => await PasteAsync(TargetFolder(RowFrom(sender)));
    private async void ContextDuplicate(object? sender, EventArgs e) => await DuplicateAsync(RowFrom(sender));
    private async void ContextDelete(object? sender, EventArgs e) => await DeleteAsync(RowFrom(sender));

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
        if (ctrl && e.Key == VirtualKey.N) { e.Handled = true; if (shift) await NewFolderAsync(TargetFolder()); else await NewNoteAsync(TargetFolder()); }
        else if (ctrl && e.Key == VirtualKey.C && selectedRow is not null) { e.Handled = true; SetClipboard(selectedRow, false); }
        else if (ctrl && e.Key == VirtualKey.X && selectedRow is not null) { e.Handled = true; SetClipboard(selectedRow, true); }
        else if (ctrl && e.Key == VirtualKey.V) { e.Handled = true; await PasteAsync(TargetFolder()); }
        else if (e.Key == VirtualKey.F2 && selectedRow is not null) { e.Handled = true; await RenameAsync(selectedRow); }
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
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        if (ctrl && e.Key == VirtualKey.S) { e.Handled = true; await SaveAsync(); }
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
        element.KeyDown += PageKeyDown; pageKeysHooked = true;
#endif
    }

#if WINDOWS
    private void PageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        if (ctrl && shift && e.Key == VirtualKey.F) { e.Handled = true; OpenSearch(); }
    }
    private void HookWindowClose()
    {
        if (closeHooked || Window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native) return;
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

    private async void QuickThoughtClicked(object? sender, EventArgs e)
    {
        var text = await DisplayPromptAsync("Quick Thought", "Save a thought to Inbox", accept: "Save", cancel: "Cancel", placeholder: "What are you thinking?");
        if (string.IsNullOrWhiteSpace(text)) return;
        var capture = new CaptureNoteRequest(Guid.NewGuid(), text, CapturedAt: DateTimeOffset.UtcNow);
        try { await api.CaptureAsync(capture, connection.Token); Status.Text = "Saved to Inbox"; await ReloadTreeAsync("inbox"); }
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
            try { await api.CreateFolderAsync(new("", path), connection.Token); } catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Conflict) { }
            var all = new List<SearchHit>(); var page = 0;
            do
            {
                var folder = await api.ListAsync(path, page, connection.Token);
                all.AddRange(folder.Entries.Where(x => !x.IsDirectory && x.Id.HasValue).Select(x => new SearchHit(x.Id!.Value, x.Title ?? x.Name, x.Path, "", 0, "")));
                if (folder.NextPage is null) break; page = folder.NextPage.Value;
            } while (true);
            ShowResultView(title, all, all.Count == 0 ? "Inbox is empty." : "");
        }
        catch (Exception exception) { ShowOperationError(exception, "Unable to open Inbox."); }
    }

    private void ShowResultView(string title, IReadOnlyList<SearchHit> results, string empty)
    {
        MessagePanel.IsVisible = EditorPane.IsVisible = Reader.IsVisible = HistoryPanel.IsVisible = false;
        SearchPanel.IsVisible = true; SearchSummary.Text = title; SearchResults.ItemsSource = results; SearchEmpty.Text = empty;
    }
    private void OpenSearch()
    {
        MessagePanel.IsVisible = EditorPane.IsVisible = Reader.IsVisible = HistoryPanel.IsVisible = false;
        SearchPanel.IsVisible = true;
        SearchEntry.Focus();
        if (!string.IsNullOrWhiteSpace(SearchEntry.Text)) _ = SearchAsync(SearchEntry.Text, CancellationToken.None);
    }

    private void SearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        searchDelay?.Cancel(); searchDelay?.Dispose(); searchDelay = new();
        var token = searchDelay.Token;
        MessagePanel.IsVisible = EditorPane.IsVisible = Reader.IsVisible = HistoryPanel.IsVisible = false;
        SearchPanel.IsVisible = true;
        _ = DebouncedSearchAsync(e.NewTextValue ?? "", token);
    }

    private async Task DebouncedSearchAsync(string query, CancellationToken token)
    {
        try { await Task.Delay(200, token); await SearchAsync(query, token); }
        catch (OperationCanceledException) { }
    }

    private async Task SearchAsync(string query, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchResults.ItemsSource = Array.Empty<SearchHit>(); SearchSummary.Text = "Search"; SearchEmpty.Text = "Type a query to search the Library."; return;
        }
        SearchSummary.Text = "Searching…";
        try
        {
            var page = await api.SearchAsync(query, 0, token);
            SearchResults.ItemsSource = page.Results;
            SearchSummary.Text = $"Search · {page.Total} result{(page.Total == 1 ? "" : "s")}";
            SearchEmpty.Text = "No matching notes.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            SearchResults.ItemsSource = Array.Empty<SearchHit>(); SearchSummary.Text = "Search needs attention";
            SearchEmpty.Text = exception is HttpRequestException ? exception.Message : "Search is temporarily unavailable.";
        }
    }

    private async void SearchResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchHit hit) return;
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
        Busy.IsRunning = true; SyncState.Text = "Syncing…";
        try
        {
            var state = await api.SyncAsync(connection.Token); SyncState.Text = GitLabel(state);
            await ReloadTreeAsync(currentNote?.Path);
            if (currentNote is not null && !dirty)
            {
                try { await OpenNote(token => api.ReadAsync(currentNote.Id, token)); }
                catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound) { ResetDocument(); }
            }
        }
        catch (Exception exception) { SyncState.Text = "Sync error"; ShowOperationError(exception, "Unable to synchronize Git."); }
        finally { Busy.IsRunning = false; }
    }

    private async void HistoryClicked(object? sender, EventArgs e)
    {
        if (currentNote is null || !await ResolveUnsavedAsync()) return;
        Busy.IsRunning = true;
        try
        {
            var history = await api.HistoryAsync(currentNote.Id, connection.Token);
            HistoryTitle.Text = "History — " + currentNote.Title;
            HistoryList.ItemsSource = history;
            HistoricalEditor.Text = "";
            HistoricalLabel.Text = history.Count == 0 ? "No committed versions yet." : "Choose a version. Historical Markdown is read-only.";
            MessagePanel.IsVisible = EditorPane.IsVisible = Reader.IsVisible = SearchPanel.IsVisible = false;
            HistoryPanel.IsVisible = true;
        }
        catch (Exception exception) { ShowOperationError(exception, "Unable to load note history."); }
        finally { Busy.IsRunning = false; }
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
                SyncState.Text = GitLabel(status.Git);
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
        catch (Exception) { SyncState.Text = "Status unavailable"; }
    }

    private void ShowMessage(string title, string detail) { Reader.IsVisible = EditorPane.IsVisible = SearchPanel.IsVisible = HistoryPanel.IsVisible = false; MessagePanel.IsVisible = true; MessageTitle.Text = title; MessageDetail.Text = detail; }
    private void ShowOperationError(Exception exception, string fallback)
    {
        Status.Text = exception is HttpRequestException ? exception.Message : fallback + " " + exception.Message;
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
        if (uri.Scheme == "slate-note" && Guid.TryParse(uri.Host, out var id))
        {
            pendingHeading = uri.Fragment;
            if (await ResolveUnsavedAsync()) await OpenNote(token => api.ReadAsync(id, token));
        }
        else if (uri.Scheme == "slate-asset" && Guid.TryParse(uri.Host, out var assetId))
        {
            try
            {
                var metadata = await api.AssetMetadataAsync(assetId, connection.Token);
                var directory = Path.Combine(FileSystem.CacheDirectory, "slate.lib", "open-assets"); Directory.CreateDirectory(directory);
                var filename = assetId.ToString("D") + metadata.Extension; var destination = Path.Combine(directory, filename);
                await api.DownloadAssetAsync(assetId, destination, connection.Token);
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

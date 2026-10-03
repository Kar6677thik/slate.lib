using Slate.Lib.Core;

namespace Slate.Lib.App;

public partial class MainPage
{
    private readonly ExplorerSelection explorerSelection = new();
    private readonly CommandRegistry commandRegistry = new();
    private string[]? draggedExplorerPaths;
    private string[]? explorerClipboard;

    private string[] VisibleExplorerPaths() => rows.Where(r => r.MoreFor is null).Select(r => r.Entry.Path).ToArray();

    private void InitializeProductivity()
    {
        void Add(string id, string title, Func<Task> execute, Func<bool>? available = null, string? shortcut = null) => commandRegistry.Register(new(id, title, execute, available, shortcut));
        Task Action(Action action) { action(); return Task.CompletedTask; }
        bool Note() => currentNote is not null;
        bool Item() => explorerSelection.Count > 0 || selectedRow?.MoreFor is null && selectedRow is not null;
        Add("new-note", "New Note", () => NewNoteAsync(TargetFolder()), shortcut: "Ctrl+N");
        Add("new-folder", "New Folder", () => NewFolderAsync(TargetFolder()), shortcut: "Ctrl+Shift+N");
        Add("capture", "Quick Thought", QuickThoughtAsync);
        Add("question", "Question", () => CreateKnowledgeAsync("question"));
        Add("add-link", "Add Link", () => CreateKnowledgeAsync("link"));
        Add("answer", "Answer Question", AnswerQuestionAsync, () => currentNote is { } note && NoteDocument.Parse(note.Markdown, note.Path).Type == "question");
        Add("append-capture", "Append to note", AppendCurrentNoteAsync, Note);
        Add("daily", "Daily Note", async () => { if (!await ResolveUnsavedAsync()) return; var note = await WorkspaceNotes.DailyAsync(api); await ReloadTreeAsync(note.Path); await OpenNote(_ => Task.FromResult(note)); });
        Add("search", "Search", () => Action(OpenSearch), shortcut: "Ctrl+Shift+F");
        Add("open-note", "Open Note", () => Action(OpenSearch));
        Add("smart-views", "Smart views and saved searches", ShowSmartViewsAsync);
        Add("offline", "Offline downloads and pending work", async () =>
        {
            if (clientState is null) throw new InvalidOperationException("Connect once to establish this library's identity.");
            if (await OfflineActions.OpenAsync(this, clientState, api, currentNote?.Id) is { } id && await ResolveUnsavedAsync())
                await OpenNote(async _ => (await clientState.Offline.ReadAsync(id))?.Note ?? await api.ReadAsync(id, default), offlineOnly: true);
        });
        Add("graph", "Nearby note graph", async () => { if (await GraphBrowser.OpenAsync(this, api, currentNote!.Id) is { } id && await ResolveUnsavedAsync()) await OpenNote(token => api.ReadAsync(id, token)); }, Note);
        Add("related", "Related notes", async () => { if (await GraphBrowser.RelatedAsync(this, api, currentNote!.Id) is { } id && await ResolveUnsavedAsync()) await OpenNote(token => api.ReadAsync(id, token)); }, Note);
        Add("rediscover", "Rediscover notes", async () => { if (await RediscoveryBrowser.OpenAsync(this, api, clientState) is { } id && await ResolveUnsavedAsync()) await OpenNote(token => api.ReadAsync(id, token)); });
        Add("attachments", "Manage attachments", async () => { if (await AttachmentBrowser.OpenAsync(this, api) is { } id && await ResolveUnsavedAsync()) await OpenNote(token => api.ReadAsync(id, token)); });
        Add("link-checks", "Check missing and ambiguous links", CheckLinksAsync);
        Add("wiki-export", "Convert wiki links to portable Markdown", ExportWikiLinksAsync, Note);
        Add("favorite", "Toggle Favorite", ToggleCurrentFavoriteAsync, () => Note() && clientState is not null);
        Add("pin", "Pin Folder", () => ToggleFolderPinAsync(selectedRow!.Entry.Path), () => selectedRow?.Entry.IsDirectory == true && clientState is not null);
        Add("move", "Move", () => RunBulkAsync("move"), Item);
        Add("copy", "Copy", () => RunBulkAsync("copy"), Item);
        Add("duplicate", "Duplicate", () => RunBulkAsync("duplicate"), Item);
        Add("delete", "Delete", () => RunBulkAsync("delete"), Item);
        Add("rename", "Rename", () => RenameAsync(selectedRow!), () => selectedRow is not null && explorerSelection.Count <= 1);
        Add("sync", "Sync", async () => { var result = await api.SyncAsync(connection.Token); UpdateSyncState(GitLabel(result)); await ReloadTreeAsync(currentNote?.Path); });
        Add("refresh", "Refresh", async () => { await api.RefreshAsync(connection.Token); await ReloadTreeAsync(currentNote?.Path); });
        Add("sync-conflict", "Review independent sync conflicts", () => MergeWorkbench.ReviewSyncAsync(this, api));
        Add("history", "History", () => Action(() => HistoryClicked(this, EventArgs.Empty)), Note);
        Add("recover-deleted", "Recover deleted notes", async () => { if (await ResolveUnsavedAsync() && await HistoryBrowser.RecoverAsync(this, api) is { } recovered) await OpenNote(_ => Task.FromResult(recovered)); });
        Add("left-sidebar", "Toggle left sidebar", () => Action(() => ToggleLeftClicked(this, EventArgs.Empty)));
        Add("right-sidebar", "Toggle right sidebar", () => Action(() => ToggleRightClicked(this, EventArgs.Empty)));
        Add("write", "Write", () => Action(() => SetMode(ViewMode.Write)), Note);
        Add("preview", "Preview", () => Action(() => SetMode(ViewMode.Preview)), Note);
        Add("split", "Split", () => Action(() => SetMode(ViewMode.Split)), Note);
        Add("settings", "Open Settings", OpenSettingsAsync);
        foreach (var (id, caption) in new[] { ("bold", "Bold"), ("italic", "Italic"), ("code", "Inline code"), ("fence", "Code block"), ("bullet", "Bullet list"), ("number", "Numbered list"), ("task", "Task checkbox"), ("quote", "Blockquote"), ("h1", "Heading 1"), ("h2", "Heading 2"), ("h3", "Heading 3"), ("link", "Markdown link"), ("wiki", "Wiki link") })
            Add("edit-" + id, caption, () => Action(() => ApplyMarkdownCommand(id)), Note);
        Add("image", "Image", () => PickAssetAsync(true), Note);
        Add("file", "File attachment", () => PickAssetAsync(false), Note);
    }

    private void RefreshSelectionAppearance()
    {
        explorerSelection.Retain(VisibleExplorerPaths());
        foreach (var row in rows)
        {
            row.Selected = explorerSelection.Count > 0 ? explorerSelection.Paths.Contains(row.Entry.Path) : ReferenceEquals(row, selectedRow);
            row.SetMultiSelection(explorerSelection.Count > 1 && row.Selected);
        }
        SelectionCount.Text = explorerSelection.Count > 1 ? $"{explorerSelection.Count} selected" : "";
    }
    private bool HandleExplorerMultiSelection(TreeRow row)
    {
        if (row.MoreFor is not null) return false;
#if WINDOWS
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        explorerSelection.Select(row.Entry.Path, VisibleExplorerPaths(), ctrl, shift); selectedRow = row; RefreshSelectionAppearance();
        return ctrl || shift;
#else
        return false;
#endif
    }
    private void ExplorerRowHandlerChanged(object? sender, EventArgs args)
    {
#if WINDOWS
        if (sender is not Grid grid || grid.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement element) return;
        element.PointerPressed += (_, e) =>
        {
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if ((ctrl || shift) && grid.BindingContext is TreeRow { MoreFor: null } row)
            { e.Handled = true; HandleExplorerMultiSelection(row); }
        };
#endif
    }
    private async Task SelectedActionsAsync()
    {
        var action = await SlateDialogs.ChooseAsync(this, $"{explorerSelection.Count} selected", "Cancel", null, "Move", "Copy", "Duplicate", "Delete");
        if (action != "Cancel") await RunBulkAsync(action.ToLowerInvariant());
    }
    private async Task OpenExplorerRowAsync(TreeRow row)
    {
        if (row.Entry.IsDirectory) { if (!row.Loaded) await LoadChildren(row, 0, connection.Token); row.Expanded = !row.Expanded; RebuildRows(row.Entry.Path); }
        else if (await ResolveUnsavedAsync()) await OpenNote(t => api.ReadAsync(row.Entry.Id!.Value, t));
    }
    private async void ExplorerRowTapped(object? sender, TappedEventArgs args)
    {
        if (sender is not BindableObject { BindingContext: TreeRow row } || browsing) return;
#if WINDOWS
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (ctrl || shift) return; // The native pointer handler owns range/toggle selection.
#endif
        browsing = true;
        try
        {
            HandleExplorerMultiSelection(row); SetSelectedRow(row);
            if (row.MoreFor is { } parent) { await LoadChildren(parent, parent.NextPage!.Value, connection.Token); RebuildRows(parent.Entry.Path); }
            else await OpenExplorerRowAsync(row);
        }
        catch (Exception error) { ShowOperationError(error, "This item could not be opened."); }
        finally { browsing = false; }
    }
    private async Task RunBulkAsync(string operation, string? destination = null, string[]? pathsOverride = null)
    {
        if (!await ResolveUnsavedAsync()) return;
        var chosen = pathsOverride ?? (explorerSelection.Count > 0 ? explorerSelection.Paths.ToArray() : selectedRow is null ? [] : [selectedRow.Entry.Path]);
        if (chosen.Length == 0) return;
        if (operation is "move" or "copy" && destination is null)
        {
            destination = await SlateDialogs.PromptAsync(this, operation == "move" ? "Move selection" : "Copy selection", "Destination folder (empty for Library)", initialValue: TargetFolder());
            if (destination is null) return;
        }
        await RunMutationAsync(async () =>
        {
            var preview = await api.PreviewBulkAsync(new(Guid.NewGuid(), operation, chosen, destination ?? ""), connection.Token);
            var detail = string.Join('\n', preview.Items.Take(12).Select(x => x.SourcePath + (x.DestinationPath is null ? "" : " → " + x.DestinationPath)))
                + (preview.Items.Count > 12 ? $"\n…and {preview.Items.Count - 12} more" : "") + $"\n\n{preview.NoteCount} notes."
                + (operation == "delete" ? " Original content remains recoverable in history and the server recovery area." : " Collisions never overwrite content.");
            if (!await SlateDialogs.AlertAsync(this, operation == "delete" ? "Delete selection?" : "Review " + operation, detail, operation == "delete" ? "Delete" : "Apply", "Cancel")) return;
            var repair = await LinkManagementViews.ReviewMoveRepairsAsync(this, preview); if (repair is null) return;
            var result = await api.ApplyBulkAsync(new(preview.OperationId, preview.Fingerprint, repair.Value), connection.Token);
            if (operation == "move" && clientState is not null) { await clientState.Workspace.RemapFoldersAsync(result.Items); await RefreshPinnedFoldersAsync(); }
            if (operation == "move") { explorerClipboard = null; clipboardPath = null; }
            explorerSelection.Clear();
            if (operation == "delete" && currentNote is { } note && chosen.Any(p => note.Path == p || note.Path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase))) ResetDocument();
            await ReloadTreeAsync(result.Items.FirstOrDefault()?.DestinationPath);
            if (currentNote is { } current) await OpenNote(t => api.ReadAsync(current.Id, t));
            ClipboardStatus.Text = $"{operation}: {result.Items.Count} items completed";
        }, "The selection could not be changed. Refresh and review it again.");
    }
    private void ApplyMarkdownCommand(string command)
    {
        if (currentNote is null) return;
        SetMode(mode == ViewMode.Preview ? ViewMode.Write : mode);
        var edit = MarkdownEditing.Transform(MarkdownEditor.Text ?? "", MarkdownEditor.CursorPosition, MarkdownEditor.SelectionLength, command);
        MarkdownEditor.Text = edit.Text; MarkdownEditor.CursorPosition = edit.Cursor; MarkdownEditor.SelectionLength = edit.Length; MarkdownEditor.Focus();
    }
    private async Task ShowRegisteredCommandsAsync()
    {
#if WINDOWS
        var query = new Entry { Placeholder = "Search commands…" }; var matches = new System.Collections.ObjectModel.ObservableCollection<SlateCommand>();
        var list = new CollectionView { ItemsSource = matches, SelectionMode = SelectionMode.Single, HeightRequest = 290 };
        Action<string?>? finish = null; var navigating = false;
        list.ItemTemplate = new DataTemplate(() =>
        {
            var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(10, 8), HeightRequest = 38 };
            var label = DesktopTheme.Label(""); label.SetBinding(Label.TextProperty, nameof(SlateCommand.Title)); row.Add(label);
            var hint = DesktopTheme.Label("", 11); hint.TextColor = DesktopTheme.Muted; hint.SetBinding(Label.TextProperty, nameof(SlateCommand.Shortcut)); row.Add(hint, 1); return row;
        });
        void Update() { matches.Clear(); foreach (var command in commandRegistry.Search(query.Text ?? "")) matches.Add(command); navigating = true; list.SelectedItem = matches.FirstOrDefault(); navigating = false; }
        query.TextChanged += (_, _) => Update(); query.Completed += (_, _) => finish?.Invoke((list.SelectedItem as SlateCommand ?? matches.FirstOrDefault())?.Id);
        list.SelectionChanged += (_, e) => { if (!navigating && e.CurrentSelection.FirstOrDefault() is SlateCommand command) finish?.Invoke(command.Id); };
        query.HandlerChanged += (_, _) =>
        {
            if (query.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement element) return;
            element.KeyDown += (_, e) =>
            {
                if (e.Key is not (Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down) || matches.Count == 0) return;
                e.Handled = true; var index = matches.IndexOf(list.SelectedItem as SlateCommand ?? matches[0]);
                index = Math.Clamp(index + (e.Key == Windows.System.VirtualKey.Up ? -1 : 1), 0, matches.Count - 1);
                navigating = true; list.SelectedItem = matches[index]; list.ScrollTo(index); navigating = false;
            };
        };
        var body = new VerticalStackLayout { Spacing = 8 }; body.Add(DesktopTheme.Field(query)); body.Add(list); Update();
        var result = await DesktopDialogs.ShowAsync(this, "Commands", "command.png", body, close =>
        {
            finish = close; query.Loaded += (_, _) => query.Focus(); return DesktopTheme.Label("↑ ↓ navigate  ·  Enter run  ·  Esc close", 11);
        }, 560);
        if (result is not null) await commandRegistry.ExecuteAsync(result);
#endif
    }
    private void ExplorerDragStarting(object? sender, DragStartingEventArgs e)
    {
        if (sender is not DragGestureRecognizer { BindingContext: TreeRow { MoreFor: null } row }) { e.Cancel = true; return; }
        draggedExplorerPaths = explorerSelection.Paths.Contains(row.Entry.Path) ? explorerSelection.Paths.ToArray() : [row.Entry.Path];
        e.Data.Properties["slate-items"] = true; e.Data.Text = $"{draggedExplorerPaths.Length} Slate items";
    }
    private void ExplorerDragOver(object? sender, DragEventArgs e)
    {
        var row = (sender as DropGestureRecognizer)?.BindingContext as TreeRow;
        var valid = row?.Entry.IsDirectory == true && draggedExplorerPaths is { Length: > 0 } && draggedExplorerPaths.All(p => !row.Entry.Path.Equals(p, StringComparison.OrdinalIgnoreCase) && !row.Entry.Path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));
        // MAUI exposes Copy/None for the drag indicator; the reviewed server operation is a move.
        e.AcceptedOperation = valid ? DataPackageOperation.Copy : DataPackageOperation.None;
        if (row is not null) row.Hovered = valid;
    }
    private void ExplorerDragLeave(object? sender, DragEventArgs e) { if ((sender as DropGestureRecognizer)?.BindingContext is TreeRow row) row.Hovered = false; }
    private void ExplorerDragCompleted(object? sender, DropCompletedEventArgs e)
    {
        draggedExplorerPaths = null;
        foreach (var row in rows) row.Hovered = false;
    }
    private async void ExplorerDrop(object? sender, DropEventArgs e)
    {
        if ((sender as DropGestureRecognizer)?.BindingContext is not TreeRow row || !row.Entry.IsDirectory || draggedExplorerPaths is null) return;
        var selection = draggedExplorerPaths; draggedExplorerPaths = null; row.Hovered = false;
        var operation = "move";
#if WINDOWS
        if (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) operation = "copy";
#endif
        await RunBulkAsync(operation, row.Entry.Path, selection);
    }
}

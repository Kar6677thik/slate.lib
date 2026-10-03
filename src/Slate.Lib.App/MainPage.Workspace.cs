using Slate.Lib.Core;

namespace Slate.Lib.App;

public partial class MainPage
{
    private readonly List<(Guid Id, string Title)> openTabs = [];
    private readonly List<Guid> navigationHistory = [];
    private int navigationIndex = -1;
    private bool navigatingHistory;
    private bool leftSidebarOpen = true;
    private bool rightSidebarOpen;
    private bool animatingSidebar;
    private bool reverseSort;
    private string? tabSignature;

    private async void OutlineClicked(object? sender, EventArgs e)
    {
        LinksRail.IsVisible = InfoRail.IsVisible = false;
        LinksTabIndicator.IsVisible = InfoTabIndicator.IsVisible = false;
        OutlineRail.IsVisible = true; OutlineItems.Clear();
        if (currentNote is null) { OutlineItems.Add(new Label { Text = "Open a note to see its outline.", FontSize = 13 }); return; }
        try
        {
            if (mode == ViewMode.Write) SetMode(ViewMode.Split);
            await ReadingOutlineView.PopulateAsync(OutlineItems, Reader);
        }
        catch (Exception) { OutlineItems.Add(new Label { Text = "The preview is loading. Open Outline again when it is ready.", FontSize = 13 }); }
    }

    private void TrackNote(LibraryNote note)
    {
        var index = openTabs.FindIndex(x => x.Id == note.Id);
        if (index < 0) openTabs.Add((note.Id, note.Title));
        else openTabs[index] = (note.Id, note.Title);
        if (!navigatingHistory && (navigationIndex < 0 || navigationHistory[navigationIndex] != note.Id))
        {
            navigationHistory.RemoveRange(navigationIndex + 1, navigationHistory.Count - navigationIndex - 1);
            navigationHistory.Add(note.Id);
            navigationIndex = navigationHistory.Count - 1;
        }
        RenderTabs();
        UpdateNavigationControls();
    }

    private void RenderTabs()
    {
        var signature = string.Join('|', openTabs.Select(x => $"{x.Id}:{x.Title}")) + $"/{currentNote?.Id}/{dirty}";
        if (signature == tabSignature) return;
        tabSignature = signature;
        NoteTabs.Clear();
        foreach (var tab in openTabs)
        {
            var active = tab.Id == currentNote?.Id;
            var row = new Grid { ColumnDefinitions = { new(18), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 7, WidthRequest = 190, HeightRequest = 36, Padding = new Thickness(10, 0, 4, 0) };
            var label = new Label { Text = tab.Title + (active && dirty ? " •" : ""), FontSize = 13,
                TextColor = Color.FromArgb(active ? "#F2F3F7" : "#9C9EAE"), VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation };
            var select = new TapGestureRecognizer();
            select.Tapped += async (_, _) => { if (currentNote?.Id != tab.Id && await ResolveUnsavedAsync()) await OpenNote(t => api.ReadAsync(tab.Id, t)); };
            label.GestureRecognizers.Add(select);
            var close = new ImageButton { Source = "dismiss_20_regular.png", Padding = 7, WidthRequest = 28, HeightRequest = 28, BorderWidth = 0, CornerRadius = 5, VerticalOptions = LayoutOptions.Center, BackgroundColor = Colors.Transparent };
            SemanticProperties.SetDescription(close, "Close " + tab.Title);
            ToolTipProperties.SetText(close, "Close " + tab.Title);
            close.Clicked += async (_, _) => await CloseTabAsync(tab.Id);
            row.Add(new Image { Source = "document_20_regular.png", WidthRequest = 15, HeightRequest = 15, Opacity = active ? 1 : .6 });
            row.Add(label, 1); row.Add(close, 2);
            NoteTabs.Add(new Border { Content = row, StrokeThickness = active ? 1 : 0, Stroke = Color.FromArgb("#484B5B"),
                BackgroundColor = Color.FromArgb(active ? "#343743" : "#22242D"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 }, Margin = new Thickness(0, 3, 0, 3) });
        }
    }

    private async Task CloseTabAsync(Guid id)
    {
        if (currentNote?.Id == id)
        {
            if (!await ResolveUnsavedAsync()) return;
            openTabs.RemoveAll(x => x.Id == id);
            ResetDocument();
            if (openTabs.LastOrDefault() is var next && next.Id != Guid.Empty)
                await OpenNote(t => api.ReadAsync(next.Id, t));
        }
        else openTabs.RemoveAll(x => x.Id == id);
        RenderTabs();
    }

    private async void NavigateBackClicked(object? sender, EventArgs e) => await NavigateHistoryAsync(-1);
    private async void NavigateForwardClicked(object? sender, EventArgs e) => await NavigateHistoryAsync(1);
    private async Task NavigateHistoryAsync(int delta)
    {
        var next = navigationIndex + delta;
        if (navigatingHistory || next < 0 || next >= navigationHistory.Count || !await ResolveUnsavedAsync()) return;
        navigatingHistory = true;
        try { await OpenNote(t => api.ReadAsync(navigationHistory[next], t)); if (currentNote?.Id == navigationHistory[next]) navigationIndex = next; }
        finally { navigatingHistory = false; UpdateNavigationControls(); }
    }

    private async void ToggleLeftClicked(object? sender, EventArgs e)
    {
        if (animatingSidebar) return;
        animatingSidebar = true;
        try
        {
            leftSidebarOpen = !leftSidebarOpen;
            ExplorerPanel.IsVisible = true;
            HeaderExplorerTools.IsVisible = leftSidebarOpen;
            await AnimateColumnAsync(ExplorerColumn, leftSidebarOpen ? 260 : 0);
            ExplorerPanel.IsVisible = leftSidebarOpen;
        }
        finally { animatingSidebar = false; }
    }

    private async void ToggleRightClicked(object? sender, EventArgs e)
    {
        if (animatingSidebar) return;
        animatingSidebar = true;
        try
        {
            rightSidebarOpen = !rightSidebarOpen;
            RightRail.IsVisible = RailDivider.IsVisible = true;
            RailDividerColumn.Width = 1;
            await AnimateColumnAsync(RailColumn, rightSidebarOpen ? 260 : 0);
            RightRail.IsVisible = RailDivider.IsVisible = rightSidebarOpen;
            RailDividerColumn.Width = rightSidebarOpen ? 1 : 0;
        }
        finally { animatingSidebar = false; }
    }

    private async Task AnimateColumnAsync(ColumnDefinition column, double target)
    {
        // Change layout once. Animating column widths reflows the WebView at every frame.
        var panel = column == ExplorerColumn ? ExplorerPanel : RightRail;
        if (target == 0) await panel.FadeToAsync(0, 80, Easing.Linear);
        column.Width = new GridLength(target);
        if (column == ExplorerColumn) HeaderExplorerColumn.Width = new GridLength(Math.Max(0, target - 44));
        if (target > 0) { panel.Opacity = 0; await panel.FadeToAsync(1, 100, Easing.Linear); }
        else panel.Opacity = 1;
    }

    private void ShowFilesClicked(object? sender, EventArgs e)
    {
        if (!leftSidebarOpen) ToggleLeftClicked(sender, e);
        SearchPanel.IsVisible = HistoryPanel.IsVisible = false;
        SetMode(mode);
    }

    private void SortTreeClicked(object? sender, EventArgs e) { reverseSort = !reverseSort; RebuildRows(currentNote?.Path); }
    private void CollapseFoldersClicked(object? sender, EventArgs e)
    {
        void Collapse(TreeRow node) { foreach (var child in node.Children) { child.Expanded = false; Collapse(child); } }
        Collapse(root); RebuildRows(currentNote?.Path);
    }

    private async void DailyNoteClicked(object? sender, EventArgs e)
    {
        if (!await ResolveUnsavedAsync()) return;
        try
        {
            var note = await WorkspaceNotes.DailyAsync(api);
            await ReloadTreeAsync(note.Path);
            await OpenNote(_ => Task.FromResult(note));
        }
        catch (Exception exception) { ShowOperationError(exception, "The daily note could not be opened."); }
    }

    private async void BookmarksClicked(object? sender, EventArgs e)
    {
        try { await ShowFavoritesAsync(); }
        catch (Exception exception) { ShowOperationError(exception, "Favorites could not be opened."); }
    }

    private async void NoteActionsClicked(object? sender, EventArgs e)
    {
        try { await NoteActionsAsync(sender, e); }
        catch (Exception exception) { ShowOperationError(exception, "The note action could not be completed."); }
    }

    private async Task NoteActionsAsync(object? sender, EventArgs e)
    {
        if (currentNote is not { } note) return;
        var actions = new List<string> { "Reading view", "Source mode", "Split view", "Favorite / unfavorite", "Rename", "Move", "Duplicate", "Find in note", "Copy path", "Version history", "Reveal in library", "Append to note", "Check links", "Convert wiki links" };
        if (NoteDocument.Parse(note.Markdown, note.Path).Type == "question") actions.Add("Answer question");
        var action = await SlateDialogs.ChooseAsync(this, note.Title, "Cancel", null, actions.ToArray());
        switch (action)
        {
            case "Reading view": SetMode(ViewMode.Preview); break;
            case "Source mode": SetMode(ViewMode.Write); break;
            case "Split view": SetMode(ViewMode.Split); break;
            case "Favorite / unfavorite": await ToggleCurrentFavoriteAsync(); break;
            case "Answer question": await AnswerQuestionAsync(); break;
            case "Append to note": await AppendCurrentNoteAsync(); break;
            case "Check links": await CheckLinksAsync(); break;
            case "Convert wiki links": await ExportWikiLinksAsync(); break;
            case "Rename": await RenameAsync(new TreeRow { Entry = new LibraryEntry(Path.GetFileName(note.Path), note.Path, false, note.Id, note.Title) }); break;
            case "Move":
                if (!await ResolveUnsavedAsync()) return;
                var destination = await SlateDialogs.PromptAsync(this, "Move note", "Destination folder path (leave empty for the library root)", initialValue: TargetFolder());
                if (destination is null) return;
                await RunBulkAsync("move", destination, [note.Path]);
                break;
            case "Duplicate": await DuplicateAsync(new TreeRow { Entry = new LibraryEntry(Path.GetFileName(note.Path), note.Path, false, note.Id, note.Title) }); break;
            case "Find in note":
                var query = await SlateDialogs.PromptAsync(this, "Find in note", "Text");
                if (string.IsNullOrEmpty(query)) return;
                var position = (MarkdownEditor.Text ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (position < 0) { await SlateDialogs.AlertAsync(this, "Find", "No match found.", "OK"); return; }
                SetMode(ViewMode.Write); MarkdownEditor.Focus(); MarkdownEditor.CursorPosition = position; MarkdownEditor.SelectionLength = query.Length;
                break;
            case "Copy path": await Clipboard.Default.SetTextAsync(note.Path); break;
            case "Version history": HistoryClicked(sender, e); break;
            case "Reveal in library":
                ShowFilesClicked(sender, e); await ReloadTreeAsync(note.Path);
                if (selectedRow is not null) Tree.ScrollTo(selectedRow, position: ScrollToPosition.Center);
                break;
        }
    }

    private async void CommandPaletteClicked(object? sender, EventArgs e)
    {
        try { await RunCommandPaletteAsync(sender, e); }
        catch (Exception exception) { ShowOperationError(exception, "The command could not be completed."); }
    }

    private async Task RunCommandPaletteAsync(object? sender, EventArgs e) => await ShowRegisteredCommandsAsync();
}

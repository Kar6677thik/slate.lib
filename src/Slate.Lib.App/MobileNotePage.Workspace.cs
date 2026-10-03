using Slate.Lib.Core;

namespace Slate.Lib.App;

public sealed partial class MobileNotePage
{
    private Grid workspace = null!;
    private Grid? drawerOverlay;

    private void InitializeWorkspace()
    {
        ToolbarItems.Clear();
        ToolbarItems.Add(new ToolbarItem("Files", "panel_left.png", async () => await OpenDrawerAsync(false)));
        ToolbarItems.Add(new ToolbarItem("Note sidebar", "panel_right.png", async () => await OpenDrawerAsync(true)));
        ToolbarItems.Add(new ToolbarItem("Note actions", "more_horizontal_20_regular.png", async () => await NoteMenuAsync()));
        var content = Content;
        Content = null;
        workspace = new Grid(); workspace.Add(content); Content = workspace;
    }

    private async Task OpenDrawerAsync(bool right)
    {
        if (drawerOverlay is not null) return;
        drawerOverlay = new Grid { BackgroundColor = Color.FromArgb("#80000000") };
        var dismiss = new BoxView { Color = Colors.Transparent };
        var tap = new TapGestureRecognizer(); tap.Tapped += async (_, _) => await CloseDrawerAsync(); dismiss.GestureRecognizers.Add(tap);
        drawerOverlay.Add(dismiss);
        var rows = new VerticalStackLayout { Spacing = 0, Padding = new Thickness(8, 10) };
        var panel = new Grid { WidthRequest = Math.Min(340, Math.Max(260, Width * .85)), BackgroundColor = MobileTheme.Surface,
            HorizontalOptions = right ? LayoutOptions.End : LayoutOptions.Start, RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        var close = MobileTheme.IconButton("dismiss_20_regular.png", "Close sidebar", size: 44);
        close.Clicked += async (_, _) => await CloseDrawerAsync();
        var header = new HorizontalStackLayout { Padding = 8, Spacing = 5 };
        header.Add(close);
        panel.Add(header); panel.Add(new ScrollView { Content = rows }, 0, 1);
        drawerOverlay.Add(panel); workspace.Add(drawerOverlay);
        panel.TranslationX = right ? panel.WidthRequest : -panel.WidthRequest;
        await panel.TranslateToAsync(0, 0, 120, Easing.CubicOut);
        try
        {
            if (right)
            {
                var links = MobileTheme.IconButton("link_20_regular.png", "Links", size: 44);
                var outline = MobileTheme.IconButton("outline.png", "Outline", size: 44);
                var info = MobileTheme.IconButton("info_20_regular.png", "Note information", size: 44);
                links.Clicked += async (_, _) => await PopulateLinksAsync(rows);
                outline.Clicked += async (_, _) => { try { await PopulateOutlineAsync(rows); } catch (Exception exception) { await ShowErrorAsync(exception, "The outline is not ready. Try again after the preview loads."); } };
                info.Clicked += (_, _) =>
                {
                    var text = editor.Text ?? "";
                    try { text = NoteDocument.Parse(text, note?.Path ?? "note.md").PlainText; }
                    catch (InvalidDataException) { }
                    rows.Clear(); rows.Add(MobileTheme.Label(note?.Path ?? "", 13));
                    rows.Add(MobileTheme.Label($"{text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length} words · {text.Length} characters", 12, MobileTheme.Secondary));
                };
                header.Add(links); header.Add(outline); header.Add(info);
                await PopulateLinksAsync(rows);
            }
            else
            {
                header.Add(MobileTheme.Label("Files", 16));
                await PopulateFolderAsync(rows, "", 0);
            }
        }
        catch (Exception exception) { rows.Add(MobileTheme.Label("Unable to load sidebar. " + exception.Message, 13, MobileTheme.Secondary)); }
    }

    private async Task CloseDrawerAsync()
    {
        if (drawerOverlay is not { } overlay) return;
        drawerOverlay = null;
        await overlay.FadeToAsync(0, 80);
        workspace.Remove(overlay);
    }

    private async Task PopulateFolderAsync(VerticalStackLayout parent, string folder, int depth)
    {
        var page = 0;
        do
        {
            var result = await Session.Api.ListAsync(folder, page, default);
            foreach (var item in result.Entries)
            {
                var children = new VerticalStackLayout { IsVisible = false };
                var loaded = false; var busy = false;
                var button = ClientControls.NavigationRow(item.Title ?? item.Name, async () =>
                {
                    if (busy) return;
                    busy = true;
                    try
                    {
                        if (!item.IsDirectory)
                        {
                            await CloseDrawerAsync();
                            await Navigation.PushAsync(new MobileNotePage(Session, item.Id!.Value)); return;
                        }
                        if (!loaded) { await PopulateFolderAsync(children, item.Path, depth + 1); loaded = true; }
                        children.IsVisible = !children.IsVisible;
                    }
                    catch (Exception exception) { await ShowErrorAsync(exception, "The folder could not be opened."); }
                    finally { busy = false; }
                }, item.IsDirectory ? "folder_20_regular.png" : "document_20_regular.png", depth * 12);
                parent.Add(button); parent.Add(children);
            }
            if (result.NextPage is null) break;
            page = result.NextPage.Value;
        } while (true);
    }

    private async Task PopulateLinksAsync(VerticalStackLayout rows)
    {
        rows.Clear();
        if (note is null) return;
        try
        {
            var links = await Session.Api.LinksAsync(note.Id, default);
            rows.Add(MobileTheme.Label("Linked mentions", 14, weight: FontAttributes.Bold));
            foreach (var link in links.Backlinks) AddLink(rows, link.SourceTitle, link.SourceId);
            if (links.Backlinks.Count == 0) rows.Add(MobileTheme.Label("No backlinks found.", 13, MobileTheme.Secondary));
            rows.Add(MobileTheme.Rule()); rows.Add(MobileTheme.Label("Outgoing links", 14, weight: FontAttributes.Bold));
            foreach (var link in links.Outgoing) AddLink(rows, link.TargetTitle ?? link.Label ?? link.Target, link.TargetId);
            if (links.Outgoing.Count == 0) rows.Add(MobileTheme.Label("No outgoing links.", 13, MobileTheme.Secondary));
        }
        catch (Exception exception) { rows.Add(MobileTheme.Label("Links unavailable. " + exception.Message, 13)); }
    }

    private void AddLink(VerticalStackLayout rows, string title, Guid? id)
    {
        var button = ClientControls.NavigationRow(title, async () => { if (id is null) return; await CloseDrawerAsync(); await Navigation.PushAsync(new MobileNotePage(Session, id.Value)); }, "document_20_regular.png");
        rows.Add(button);
    }

    private async Task PopulateOutlineAsync(VerticalStackLayout rows)
    {
        rows.Clear();
        if (editing) await ToggleAsync();
        await ReadingOutlineView.PopulateAsync(rows, reader, CloseDrawerAsync);
    }

    private async Task NoteMenuAsync()
    {
        if (note is null) return;
        var actions = new List<string> { "Favorite / unfavorite", "Nearby note graph", "Related notes", "Offline work", "Copy path", "Rename", "Move", "Version history", "Find in note", "Append to note", "Check links", "Convert wiki links", "Quick Thought", "Settings" };
        if (NoteDocument.Parse(note.Markdown, note.Path).Type == "question") actions.Add("Answer question");
        var action = await SlateDialogs.ChooseAsync(this, note.Title, "Cancel", null, actions.ToArray());
        try
        {
            switch (action)
            {
                case "Favorite / unfavorite": if (Session.State is not null) await Session.State.Workspace.ToggleFavoriteAsync(note.Id, note.Title); break;
                case "Answer question":
                    if (dirty) { await SaveAsync(); if (dirty) return; }
                    var answer = await KnowledgeDialogs.AnswerAsync(this); if (string.IsNullOrWhiteSpace(answer)) return;
                    note = await Session.Api.AnswerAsync(note.Id, new(answer, note.Revision, DateTimeOffset.UtcNow));
                    settingText = true; editor.Text = note.Markdown; settingText = false; Render(); UpdateState(); break;
                case "Append to note":
                    if (dirty) { await SaveAsync(); if (dirty) return; }
                    var appended = await KnowledgeDialogs.AppendAsync(this, Session.Api, note);
                    if (appended is not null) await Navigation.PushAsync(new MobileNotePage(Session, appended.Id)); break;
                case "Nearby note graph":
                    if (await GraphBrowser.OpenAsync(this, Session.Api, note.Id) is { } graphNote) await Navigation.PushAsync(new MobileNotePage(Session, graphNote)); break;
                case "Related notes":
                    if (await GraphBrowser.RelatedAsync(this, Session.Api, note.Id) is { } relatedNote) await Navigation.PushAsync(new MobileNotePage(Session, relatedNote)); break;
                case "Copy path": await Clipboard.Default.SetTextAsync(note.Path); break;
                case "Offline work":
                    if (Session.State is not null && await OfflineActions.OpenAsync(this, Session.State, Session.Api, note.Id) is { } downloaded) await Navigation.PushAsync(new MobileNotePage(Session, downloaded)); break;
                case "Check links":
                    if (dirty) { await SaveAsync(); if (dirty) return; }
                    if (await LinkManagementViews.IssuesAsync(this, Session.Api) is { } repaired) await Navigation.PushAsync(new MobileNotePage(Session, repaired.Id)); break;
                case "Convert wiki links":
                    if (dirty) { await SaveAsync(); if (dirty) return; }
                    if (await LinkManagementViews.ExportAsync(this, Session.Api, note.Id) is { } exported)
                    { note = exported; settingText = true; editor.Text = note.Markdown; settingText = false; Render(); UpdateState(); } break;
                case "Rename":
                    if (dirty) { await SaveAsync(); if (dirty) return; }
                    var name = await SlateDialogs.PromptAsync(this, "Rename", "Filename", initialValue: Path.GetFileName(note.Path));
                    if (string.IsNullOrWhiteSpace(name)) return;
                    if (await LinkManagementViews.MoveAsync(this, Session.Api, note.Path, "", name) is null) return;
                    note = await Session.Api.ReadAsync(note.Id, default); Title = note.Title;
                    settingText = true; editor.Text = note.Markdown; settingText = false; Render(); UpdateState(); break;
                case "Move":
                    if (dirty) { await SaveAsync(); if (dirty) return; }
                    var folder = await MobileFolderPickerPage.PickAsync(Navigation, Session);
                    if (folder is null) return;
                    if (await LinkManagementViews.MoveAsync(this, Session.Api, note.Path, folder) is null) return;
                    note = await Session.Api.ReadAsync(note.Id, default);
                    settingText = true; editor.Text = note.Markdown; settingText = false; Render(); UpdateState(); break;
                case "Version history":
                    if (dirty) { await SaveAsync(); if (dirty) return; }
                    if (await HistoryBrowser.OpenAsync(this, Session.Api, note.Id) is { } restored)
                    { note = restored; settingText = true; editor.Text = note.Markdown; settingText = false; Title = note.Title; Render(); UpdateState(); }
                    break;
                case "Find in note":
                    var query = await SlateDialogs.PromptAsync(this, "Find in note", "Text");
                    if (string.IsNullOrEmpty(query)) return;
                    if (!editing) await ToggleAsync();
                    var position = (editor.Text ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase);
                    if (position >= 0) { editor.Focus(); editor.CursorPosition = position; editor.SelectionLength = query.Length; }
                    else await SlateDialogs.AlertAsync(this, "Find", "No match found.", "OK");
                    break;
                case "Quick Thought": await Navigation.PushAsync(new MobileCapturePage(Session)); break;
                case "Settings": await Navigation.PushAsync(new MobileSettingsPage(Session)); break;
            }
        }
        catch (Exception exception) { await ShowErrorAsync(exception, "The action could not be completed."); }
    }
}

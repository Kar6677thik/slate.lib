using Slate.Lib.Core;

namespace Slate.Lib.App;

public sealed partial class MobileFolderPage
{
    private readonly ExplorerSelection mobileSelection = new();
    private readonly Label selectedCount = MobileTheme.Label("", 13);
    private readonly HorizontalStackLayout selectionBar = new() { IsVisible = false, Spacing = 4, Padding = new Thickness(0, 4) };
    protected override bool OnBackButtonPressed()
    {
        if (mobileSelection.Count > 0) { ClearSelected(); return true; }
        return base.OnBackButtonPressed();
    }
    private View BuildSelectionBar()
    {
        selectionBar.Add(selectedCount);
        foreach (var (operation, icon, caption) in new[] { ("move", "folder_open_20_regular.png", "Move selected"), ("copy", "copy.png", "Copy selected"), ("duplicate", "document_20_regular.png", "Duplicate selected"), ("delete", "delete.png", "Delete selected") })
        {
            var button = MobileTheme.IconButton(icon, caption, size: 44);
            button.Clicked += async (_, _) => await ApplyMobileBulkAsync(operation); selectionBar.Add(button);
        }
        var clear = MobileTheme.IconButton("dismiss_20_regular.png", "Clear selection", size: 44); clear.Clicked += (_, _) => ClearSelected(); selectionBar.Add(clear);
        return selectionBar;
    }
    private void InstallSelectionGesture(Grid grid)
    {
#if ANDROID
        grid.HandlerChanged += (_, _) =>
        {
            if (grid.Handler?.PlatformView is not Android.Views.View native) return;
            native.LongClickable = true;
            native.LongClick += (_, e) => { if (grid.BindingContext is LibraryEntry entry) { e.Handled = true; ToggleSelected(entry); } };
        };
#endif
    }
    private void ToggleSelected(LibraryEntry entry)
    {
        mobileSelection.Select(entry.Path, entries.Select(x => x.Path).ToArray(), toggle: true); RefreshSelected();
    }
    private void ClearSelected() { mobileSelection.Clear(); RefreshSelected(); }
    private void RefreshSelected()
    {
        selectionBar.IsVisible = mobileSelection.Count > 0; selectedCount.Text = $"{mobileSelection.Count} selected";
        for (var i = 0; i < entries.Count; i++) entries[i] = entries[i] with { };
    }
    private async Task ApplyMobileBulkAsync(string operation)
    {
        try
        {
            string? destination = "";
            if (operation is "move" or "copy") destination = await MobileFolderPickerPage.PickAsync(Navigation, Session);
            if (destination is null) return;
            var preview = await Session.Api.PreviewBulkAsync(new(Guid.NewGuid(), operation, mobileSelection.Paths.ToArray(), destination));
            var text = string.Join('\n', preview.Items.Take(8).Select(x => x.SourcePath + (x.DestinationPath is null ? "" : " → " + x.DestinationPath)))
                + $"\n\n{preview.NoteCount} notes." + (operation == "delete" ? " Originals remain recoverable in history and the server recovery area." : "");
            if (!await SlateDialogs.AlertAsync(this, "Review " + operation, text, operation == "delete" ? "Delete" : "Apply", "Cancel")) return;
            var repair = await LinkManagementViews.ReviewMoveRepairsAsync(this, preview); if (repair is null) return;
            var result = await Session.Api.ApplyBulkAsync(new(preview.OperationId, preview.Fingerprint, repair.Value));
            if (operation == "move" && Session.State is not null) await Session.State.Workspace.RemapFoldersAsync(result.Items);
            ClearSelected(); await LoadAsync(); state.Text = $"{result.Items.Count} items completed";
        }
        catch (Exception exception) { await ShowErrorAsync(exception, "The selection could not be changed. Refresh and review it again."); }
    }
}

internal static class MarkdownToolbar
{
    internal static View Create(Editor editor, Func<Task> image, Func<Task> file, Func<Task>? wiki = null)
    {
        var bar = new HorizontalStackLayout { Spacing = 2 };
        foreach (var (command, text) in new[] { ("h2", "H"), ("bold", "B"), ("italic", "I"), ("code", "<>"), ("bullet", "•"), ("task", "☑"), ("link", "↗"), ("wiki", "[[ ]]") })
        {
            var button = new Button { Text = text, FontSize = 13, HeightRequest = 40, MinimumWidthRequest = 40, Padding = 4, BackgroundColor = Colors.Transparent, TextColor = MobileTheme.Secondary, CornerRadius = 8 };
            SemanticProperties.SetDescription(button, command + " Markdown");
            button.Clicked += async (_, _) =>
            {
                if (command == "wiki" && wiki is not null) { await wiki(); return; }
                var edit = MarkdownEditing.Transform(editor.Text ?? "", editor.CursorPosition, editor.SelectionLength, command);
                editor.Text = edit.Text; editor.CursorPosition = edit.Cursor; editor.SelectionLength = edit.Length; editor.Focus();
            }; bar.Add(button);
        }
        var addImage = MobileTheme.IconButton("image_20_regular.png", "Image", size: 40); addImage.Clicked += async (_, _) => await image(); bar.Add(addImage);
        var addFile = MobileTheme.IconButton("attach_20_regular.png", "File attachment", size: 40); addFile.Clicked += async (_, _) => await file(); bar.Add(addFile);
        return new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = bar, HeightRequest = 44 };
    }
}

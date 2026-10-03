using Slate.Lib.Core;

namespace Slate.Lib.App;

internal static class MergeWorkbench
{
    internal static async Task ReviewSyncAsync(ContentPage owner, LibraryApiClient api)
    {
        var preview = await api.PreviewSyncMergeAsync();
        var content = MobileTheme.Label("These two histories changed different files. Slate validated the combined library. Combining preserves both histories in a new commit.\n\nIncoming paths:\n" + string.Join("\n", preview.RemotePaths.Take(30)) + (preview.RemotePaths.Count > 30 ? $"\n…and {preview.RemotePaths.Count - 30} more" : ""), 13);
        if (await SlateDialogs.ContentAsync(owner, "Review independent sync changes", content, "Combine histories", "Keep separate") != "accept") return;
        var result = await api.ApplySyncMergeAsync(preview.Id);
        await SlateDialogs.AlertAsync(owner, "Changes combined", result.Detail ?? result.State, "Done");
    }
    internal static async Task<LibraryNote?> OpenAsync(ContentPage owner, LibraryApiClient api, ClientStateStore? state, LibraryNote basis, string local)
    {
        if (state is not null)
        {
            var retained = await state.ReadDraftAsync(basis.Id) ?? new(basis.Id, DraftKind.ExistingNote, local, DateTimeOffset.UtcNow, basis.Id, basis.Path, basis.Revision, basis.Markdown);
            await state.SaveDraftAsync(retained with { Markdown = local, UpdatedAt = DateTimeOffset.UtcNow });
            foreach (var asset in retained.PendingAssets ?? [])
            {
                var metadata = await PendingAssetStore.UploadAsync(api, asset);
                local = local.Replace($"asset-pending://{asset.Id:D}", AssetReferences.PathForNote(basis.Path, asset.Id, metadata.Extension), StringComparison.Ordinal);
            }
        }
        var current = await api.ReadAsync(basis.Id, default);
        var merge = await Task.Run(() => TextMerge.Merge(basis.Markdown, local, current.Markdown));
        var blocks = merge.Blocks.ToArray(); var resolved = blocks.Select(x => !x.Conflict).ToArray();
        var information = MobileTheme.Label(merge.HasConflicts ? "Both versions changed the same text. Resolve each block, or review and edit the complete result." : "Changes do not overlap. The proposed result includes both versions.", 13, MobileTheme.Secondary);
        var versions = new Picker { Title = "Compare version", ItemsSource = new[] { "Proposed result", "Base", "Local draft", "Current server" }, SelectedIndex = 0 };
        var editor = new Editor { Text = merge.Proposed, HeightRequest = 260, AutoSize = EditorAutoSizeOption.Disabled, FontSize = 13 };
        MobileTheme.Input(editor); var proposed = merge.Proposed; var switching = false;
        editor.TextChanged += (_, _) => { if (!switching && versions.SelectedIndex == 0) proposed = editor.Text ?? ""; };
        versions.SelectedIndexChanged += (_, _) =>
        {
            switching = true; editor.IsReadOnly = versions.SelectedIndex != 0;
            editor.Text = versions.SelectedIndex switch { 1 => basis.Markdown, 2 => local, 3 => current.Markdown, _ => proposed }; switching = false;
        };
        var indexes = Enumerable.Range(0, blocks.Length).Where(x => blocks[x].Conflict).ToArray();
        var chooseBlock = new Picker { Title = "Conflicting block", ItemsSource = indexes.Select((x, i) => $"Block {i + 1}").ToArray(), SelectedIndex = indexes.Length > 0 ? 0 : -1 };
        var useLocal = MobileTheme.Button("Use local block", "edit_20_regular.png");
        var useCurrent = MobileTheme.Button("Use current block", "arrow_sync_20_regular.png");
        var blockTools = new VerticalStackLayout { Spacing = 4, IsVisible = merge.HasConflicts, Children = { chooseBlock, new HorizontalStackLayout { Spacing = 6, Children = { useLocal, useCurrent } } } };
        var blockText = MobileTheme.Label("", 12, MobileTheme.Secondary);
        void ShowBlock() { if (chooseBlock.SelectedIndex >= 0) { var block = blocks[indexes[chooseBlock.SelectedIndex]]; blockText.Text = "LOCAL BLOCK\n" + block.Local + "\nCURRENT BLOCK\n" + block.Current; } }
        chooseBlock.SelectedIndexChanged += (_, _) => ShowBlock(); ShowBlock();
        blockTools.Add(new ScrollView { HeightRequest = 100, Content = blockText });
        editor.TextChanged += (_, _) => { if (!switching && versions.SelectedIndex == 0) blockTools.IsEnabled = false; };
        void SelectBlock(bool useLocalVersion)
        {
            if (chooseBlock.SelectedIndex < 0) return;
            var index = indexes[chooseBlock.SelectedIndex]; var block = blocks[index];
            blocks[index] = block with { Proposed = useLocalVersion ? block.Local : block.Current }; resolved[index] = true;
            proposed = string.Concat(blocks.Select(x => x.Proposed)); switching = true;
            versions.SelectedIndex = 0; switching = true; editor.Text = proposed; switching = false;
            information.Text = $"{resolved.Count(x => !x)} unresolved blocks. After manual editing, confirm the complete result below.";
        }
        useLocal.Clicked += (_, _) => SelectBlock(true); useCurrent.Clicked += (_, _) => SelectBlock(false);
        var manual = new CheckBox(); var manualRow = new HorizontalStackLayout { Spacing = 6, Children = { manual, MobileTheme.Label("I reviewed the complete proposed result", 12) } };
        var content = new VerticalStackLayout { Spacing = 8, Children = { information, versions, editor, blockTools, manualRow } };
        while (true)
        {
            var action = await SlateDialogs.ContentAsync(owner, "Resolve note changes", content, "Save merged result", "Keep draft", "Save local as separate note");
            if (action is null or "cancel") return null;
            if (action != "accept")
            {
                var name = await SlateDialogs.PromptAsync(owner, "Keep both notes", "New filename in the same folder", initialValue: Path.GetFileNameWithoutExtension(current.Path) + " recovered");
                if (string.IsNullOrWhiteSpace(name)) continue;
                var id = Guid.NewGuid(); var source = NoteDocument.ReplaceId(local, id);
                var slash = current.Path.LastIndexOf('/'); return await api.CreateNoteAsync(new(slash < 0 ? "" : current.Path[..slash], name, Id: id, InitialMarkdown: source), default);
            }
            if (resolved.Any(x => !x) && !manual.IsChecked) { information.Text = "Review every conflicting block or confirm that you reviewed the full edited result."; continue; }
            // A second server edit is rejected by the normal revision check; the preserved draft remains intact.
            var saved = await api.UpdateNoteAsync(current.Id, new(proposed, current.Revision), default);
            if (state is not null) { await state.Offline.Queue.ResolveConflictAsync(current.Id); await state.DeleteDraftAsync(current.Id); }
            return saved;
        }
    }
}

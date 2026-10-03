using Slate.Lib.Core;

namespace Slate.Lib.App;

internal static class LinkManagementViews
{
    internal static async Task<bool> ReviewAsync(ContentPage page, string title, IReadOnlyList<NoteTextPreview> previews, string accept)
    {
        if (previews.Count == 0) return true;
        var index = 0; var after = false;
        var heading = MobileTheme.Label("", 13); var detail = MobileTheme.Label("", 12, MobileTheme.Secondary);
        var source = new Editor { IsReadOnly = true, HeightRequest = 220, BackgroundColor = MobileTheme.Canvas, TextColor = MobileTheme.Primary, FontSize = 12 };
        var beforeAfter = MobileTheme.Button("Show proposed source", "document_20_regular.png");
        var previous = MobileTheme.Button("Previous note", "arrow_left_20_regular.png"); var next = MobileTheme.Button("Next note", "arrow_right.png");
        void Show()
        {
            var preview = previews[index]; heading.Text = $"{index + 1} / {previews.Count} · {preview.Path}";
            detail.Text = string.Join('\n', preview.Changes.Select(c => c.Original + " → " + c.Proposed + "\nTarget: " + c.TargetPath));
            source.Text = after ? preview.ProposedMarkdown : preview.OriginalMarkdown;
            beforeAfter.Text = after ? "Show original source" : "Show proposed source";
            previous.IsEnabled = index > 0; next.IsEnabled = index < previews.Count - 1;
        }
        beforeAfter.Clicked += (_, _) => { after = !after; Show(); }; previous.Clicked += (_, _) => { index--; Show(); }; next.Clicked += (_, _) => { index++; Show(); }; Show();
        var nav = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } }; nav.Add(previous); nav.Add(next, 1); nav.IsVisible = previews.Count > 1;
        var content = new VerticalStackLayout { Spacing = 8, Children = { heading, new ScrollView { Content = detail, MaximumHeightRequest = 90 }, beforeAfter, source, nav } };
        return await SlateDialogs.ContentAsync(page, title, content, accept) == "accept";
    }
    internal static async Task<bool?> ReviewMoveRepairsAsync(ContentPage page, BulkPreview preview)
    {
        if (preview.Repairs is not { Count: > 0 } repairs) return false;
        var action = await SlateDialogs.ChooseAsync(page, $"{repairs.Count} notes have incoming links affected by this move", "Cancel", null, "Review and repair", "Move without repairs");
        if (action == "Move without repairs") return false;
        if (action != "Review and repair") return null;
        return await ReviewAsync(page, "Incoming link repairs", repairs, "Move and repair") ? true : null;
    }
    internal static async Task<BulkOperationResult?> MoveAsync(ContentPage page, LibraryApiClient api, string path, string destination, string? newName = null)
    {
        var preview = await api.PreviewBulkAsync(new(Guid.NewGuid(), newName is null ? "move" : "rename", [path], destination, newName));
        if (!await SlateDialogs.AlertAsync(page, newName is null ? "Review move" : "Review rename", path + " → " + preview.Items[0].DestinationPath, "Continue", "Cancel")) return null;
        var repairs = await ReviewMoveRepairsAsync(page, preview); if (repairs is null) return null;
        return await api.ApplyBulkAsync(new(preview.OperationId, preview.Fingerprint, repairs.Value));
    }
    internal static async Task<LibraryNote?> ExportAsync(ContentPage page, LibraryApiClient api, Guid id)
    {
        var preview = await api.WikiExportAsync(id);
        if (preview.Changes.Count == 0) { await SlateDialogs.AlertAsync(page, "Portable links", "No resolvable wiki links to convert. Missing or ambiguous links remain unchanged.", "OK"); return null; }
        if (!await ReviewAsync(page, "Convert wiki links to Markdown", [preview], "Apply conversion")) return null;
        return await api.ApplyWikiExportAsync(id, new(preview.Revision, preview.ProposedMarkdown));
    }
    internal static async Task<LibraryNote?> IssuesAsync(ContentPage owner, LibraryApiClient api)
    {
        var page = 0;
        while (true)
        {
            var result = await api.LinkIssuesAsync(page);
            if (result.Total == 0) { await SlateDialogs.AlertAsync(owner, "Link checks", "No missing or ambiguous note links.", "OK"); return null; }
            var labels = result.Results.Select((x, i) => $"{i + 1}. {x.SourcePath} · {x.Link.State} · {x.Link.Target}").ToList();
            if (page > 0) labels.Add("Previous page"); if ((page + 1) * 20 < result.Total) labels.Add("Next page");
            var choice = await SlateDialogs.ChooseAsync(owner, $"Link checks · {result.Total} links", "Close", null, labels.ToArray());
            if (choice == "Previous page") { page--; continue; } if (choice == "Next page") { page++; continue; }
            var index = labels.IndexOf(choice); if (index < 0 || index >= result.Results.Count) return null;
            var issue = result.Results[index];
            var path = await SlateDialogs.PromptAsync(owner, "Choose link destination", "Note path in Library. This replaces the link with a link to the note, removing any missing heading.", initialValue: issue.Link.TargetPath);
            if (string.IsNullOrWhiteSpace(path)) return null;
            var target = await api.ReadPathAsync(path, default);
            var request = new LinkRepairRequest(issue.SourceRevision, issue.Link.Start, target.Id, target.Revision);
            var preview = await api.PreviewLinkRepairAsync(issue.SourceId, request);
            if (!await ReviewAsync(owner, "Review link repair", [preview], "Repair link")) return null;
            return await api.ApplyLinkRepairAsync(issue.SourceId, request);
        }
    }
}

public partial class MainPage
{
    private async Task CheckLinksAsync()
    {
        if (!await ResolveUnsavedAsync()) return;
        if (await LinkManagementViews.IssuesAsync(this, api) is { } changed) await OpenNote(_ => Task.FromResult(changed));
    }
    private async Task ExportWikiLinksAsync()
    {
        if (currentNote is not { } note || !await ResolveUnsavedAsync()) return;
        if (await LinkManagementViews.ExportAsync(this, api, note.Id) is { } changed) await OpenNote(_ => Task.FromResult(changed));
    }
}

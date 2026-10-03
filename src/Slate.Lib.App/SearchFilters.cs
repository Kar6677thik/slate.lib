namespace Slate.Lib.App;

internal static class SearchFilters
{
    internal static async Task<string?> AddAsync(ContentPage owner, string current)
    {
        var field = await SlateDialogs.ChooseAsync(owner, "Add search filter", "Cancel", null, "Type", "Status", "Contains", "Created", "Modified", "Tag", "Path");
        string? value; string key;
        switch (field)
        {
            case "Type": key = "type"; value = await SlateDialogs.PromptAsync(owner, "Note type", "For example: question, link, idea", maxLength: 80); break;
            case "Status": key = "status"; value = await SlateDialogs.PromptAsync(owner, "Note status", "For example: open, learning, needs-review", maxLength: 80); break;
            case "Contains":
                key = "has"; value = await SlateDialogs.ChooseAsync(owner, "Contains", "Cancel", null, "image", "file", "code", "diagram", "backlinks", "links", "question"); if (value == "Cancel") return null; break;
            case "Created": case "Modified":
                key = field.ToLowerInvariant(); value = await SlateDialogs.PromptAsync(owner, field + " date", "YYYY-MM-DD or YYYY-MM-DD..YYYY-MM-DD. Use * for an open end.", maxLength: 24); break;
            case "Tag": key = "tag"; value = await SlateDialogs.PromptAsync(owner, "Tag", "Exact tag name", maxLength: 80); break;
            case "Path": key = "path"; value = await SlateDialogs.PromptAsync(owner, "Folder or path", "For example: Projects/Databases", maxLength: 220); break;
            default: return null;
        }
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim().Replace('"', ' ');
        return (current.Trim() + " " + key + ":" + (value.Any(char.IsWhiteSpace) ? "\"" + value + "\"" : value)).Trim();
    }
}

public partial class MainPage
{
    private int searchPage;
    private int searchRequest;
    private string activeSearchQuery = "";
    private async void SearchFiltersClicked(object? sender, EventArgs e)
    {
        try { if (await SearchFilters.AddAsync(this, SearchEntry.Text ?? "") is { } query) SearchEntry.Text = query; }
        catch (Exception ex) { ShowOperationError(ex, "The filter could not be added."); }
    }
    private async void SearchPreviousClicked(object? sender, EventArgs e) => await SearchAsync(activeSearchQuery, connection.Token, Math.Max(0, searchPage - 1));
    private async void SearchNextClicked(object? sender, EventArgs e) => await SearchAsync(activeSearchQuery, connection.Token, searchPage + 1);
}

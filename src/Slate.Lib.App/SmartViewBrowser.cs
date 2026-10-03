using System.Collections.ObjectModel;
using Slate.Lib.Core;

namespace Slate.Lib.App;

internal static class SmartViewBrowser
{
    internal static async Task<SearchHit?> OpenAsync(ContentPage owner, LibraryApiClient api, WorkspacePreferenceStore preferences, string? currentQuery = null)
    {
        var saved = (await preferences.ReadAsync()).Searches;
        var labels = SmartViews.All.Select(x => x.Title).Concat(saved.Select(x => "Saved · " + x.Name)).ToList();
        labels.Insert(0, "Favorites");
        if (!string.IsNullOrWhiteSpace(currentQuery)) labels.Add("Save current search…");
        if (saved.Count > 0) labels.Add("Manage saved searches…");
        var choice = await SlateDialogs.ChooseAsync(owner, "Smart views", "Cancel", null, labels.ToArray());
        if (choice == "Save current search…")
        {
            await api.SearchAsync(currentQuery!, 0, default); // Validate using the same engine before persisting.
            var name = await SlateDialogs.PromptAsync(owner, "Save search", "Name", maxLength: 80);
            if (!string.IsNullOrWhiteSpace(name)) await preferences.SaveSearchAsync(Guid.NewGuid(), name, currentQuery!);
            return null;
        }
        if (choice == "Manage saved searches…") { await ManageAsync(owner, preferences, saved); return null; }
        if (choice == "Favorites")
        {
            var favorites = (await preferences.ReadAsync()).Favorites;
            return await ResultsAsync(owner, "Favorites", "Favorites stay on this device and follow note moves.", page => Task.FromResult(new SearchPage("", page, 20, favorites.Count,
                favorites.Skip(page * 20).Take(20).Select(x => new SearchHit(x.Id, x.Title, "", "Favorite", 0, "")).ToArray(), 0)));
        }
        if (SmartViews.All.FirstOrDefault(x => x.Title == choice) is { } view)
            return await ResultsAsync(owner, view.Title, view.Description, page => api.SmartViewAsync(view.Id, page));
        if (saved.FirstOrDefault(x => "Saved · " + x.Name == choice) is { } search)
            return await ResultsAsync(owner, search.Name, search.Query, page => api.SearchAsync(search.Query, page, default));
        return null;
    }

    private static async Task ManageAsync(ContentPage owner, WorkspacePreferenceStore preferences, IReadOnlyList<SavedSearch> saved)
    {
        var labels = saved.Select((x, index) => $"{index + 1}. {x.Name}").ToArray();
        var selection = await SlateDialogs.ChooseAsync(owner, "Saved searches", "Cancel", null, labels);
        var index = Array.IndexOf(labels, selection); if (index < 0) return;
        var search = saved[index];
        var action = await SlateDialogs.ChooseAsync(owner, search.Name, "Cancel", "Delete saved search", "Rename");
        if (action == "Delete saved search") await preferences.DeleteSearchAsync(search.Id);
        if (action == "Rename")
        {
            var name = await SlateDialogs.PromptAsync(owner, "Rename saved search", "Name", maxLength: 80, initialValue: search.Name);
            if (!string.IsNullOrWhiteSpace(name)) await preferences.SaveSearchAsync(search.Id, name, search.Query);
        }
    }

    private static async Task<SearchHit?> ResultsAsync(ContentPage owner, string title, string description, Func<int, Task<SearchPage>> load)
    {
        var items = new ObservableCollection<SearchHit>(); var page = 0;
        var summary = MobileTheme.Label(description, 12, MobileTheme.Secondary);
        var count = MobileTheme.Label("", 12, MobileTheme.Secondary);
        var next = MobileTheme.Button("Next page", "arrow_right.png");
        var previous = MobileTheme.Button("Previous", "arrow_left_20_regular.png");
        var list = new CollectionView { ItemsSource = items, SelectionMode = SelectionMode.Single, HeightRequest = 280,
            EmptyView = MobileTheme.Label("No matching notes.", 14, MobileTheme.Secondary), BackgroundColor = Colors.Transparent };
        list.ItemTemplate = new DataTemplate(() =>
        {
            var label = MobileTheme.Label("", 14); label.SetBinding(Label.TextProperty, nameof(SearchHit.Title)); label.MaxLines = 1;
            var path = MobileTheme.Label("", 11, MobileTheme.Secondary); path.SetBinding(Label.TextProperty, nameof(SearchHit.Path)); path.MaxLines = 1;
            return new VerticalStackLayout { Padding = new Thickness(8, 9), Spacing = 3, Children = { label, path } };
        });
        async Task Load(int requested)
        {
            previous.IsEnabled = next.IsEnabled = false;
            try
            {
                var result = await load(requested); page = requested; items.Clear(); foreach (var hit in result.Results) items.Add(hit);
                count.Text = result.Total == 0 ? "0 notes" : $"{page * 20 + 1}–{page * 20 + items.Count} of {result.Total}";
                previous.IsEnabled = page > 0; next.IsEnabled = (page + 1) * 20 < result.Total && page < 1999;
            }
            catch (Exception e) { count.Text = e is HttpRequestException ? e.Message : "Unable to load this page. Try again."; previous.IsEnabled = page > 0; next.IsEnabled = true; }
        }
        previous.Clicked += async (_, _) => await Load(Math.Max(0, page - 1)); next.Clicked += async (_, _) => await Load(page + 1);
        await Load(0);
        var navigation = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
        navigation.Add(previous); navigation.Add(next, 1);
        var content = new VerticalStackLayout { Spacing = 8, Children = { summary, list, count, navigation } };
        return await SlateDialogs.ContentAsync(owner, title, content, "Open note", "Close") == "accept" ? list.SelectedItem as SearchHit : null;
    }
}

public partial class MainPage
{
    private async Task ShowSmartViewsAsync()
    {
        if (clientState is null) { Status.Text = "Connect to open smart views."; return; }
        var hit = await SmartViewBrowser.OpenAsync(this, api, clientState.Workspace, SearchEntry.Text);
        if (hit is not null) await OpenSearchHitAsync(hit);
    }
    private async void SmartViewsClicked(object? sender, EventArgs e)
    {
        try { await ShowSmartViewsAsync(); } catch (Exception ex) { ShowOperationError(ex, "Smart views could not be opened."); }
    }
}

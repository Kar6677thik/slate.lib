using Microsoft.Maui.Controls.Shapes;

namespace Slate.Lib.App;

internal static class SlateDialogs
{
    internal static async Task<bool> AlertAsync(Page page, string title, string message, string accept, string? cancel = null)
    {
        var target = page as ContentPage ?? (page as TabbedPage)?.CurrentPage as ContentPage
            ?? ((page as TabbedPage)?.CurrentPage as NavigationPage)?.CurrentPage as ContentPage;
        if (target is null) return false;
        var content = MobileTheme.Label(message, 14, MobileTheme.Secondary);
        content.LineBreakMode = LineBreakMode.WordWrap;
#if WINDOWS
        var result = await DesktopDialogs.ShowAsync(target, title, "info_20_regular.png", content, finish =>
#else
        var result = await ShowAsync(target, title, content, finish =>
#endif
        {
            var buttons = new HorizontalStackLayout { HorizontalOptions = LayoutOptions.End, Spacing = 8 };
            if (cancel is not null)
            {
                var dismiss = TextButton(cancel); dismiss.Clicked += (_, _) => finish(null); buttons.Add(dismiss);
            }
            var confirm = TextButton(accept, true); confirm.Clicked += (_, _) => finish("accept"); buttons.Add(confirm);
            return buttons;
        });
        return result == "accept";
    }

    private static readonly Dictionary<Page, Action> active = [];
    internal static Task<string?> ContentAsync(ContentPage page, string title, View body, string accept = "Save", string cancel = "Cancel", string? alternate = null)
    {
        View Footer(Action<string?> finish)
        {
#if ANDROID
            var buttons = new VerticalStackLayout { HorizontalOptions = LayoutOptions.Fill, Spacing = 6 };
#else
            var buttons = new HorizontalStackLayout { HorizontalOptions = LayoutOptions.End, Spacing = 8 };
#endif
            var dismiss = TextButton(cancel); dismiss.Clicked += (_, _) => finish(null); buttons.Add(dismiss);
            if (alternate is not null) { var other = TextButton(alternate); other.Clicked += (_, _) => finish("alternate"); buttons.Add(other); }
            var submit = TextButton(accept, true); submit.Clicked += (_, _) => finish("accept"); buttons.Add(submit); return buttons;
        }
#if WINDOWS
        return DesktopDialogs.ShowAsync(page, title, "edit_20_regular.png", body, Footer, 560);
#else
        return ShowAsync(page, title, body, Footer);
#endif
    }
    internal static bool TryDismiss(Page page)
    {
#if WINDOWS
        if (DesktopDialogs.TryDismiss(page)) return true;
#endif
        if (!active.TryGetValue(page, out var close)) return false;
        close(); return true;
    }

    internal static Task<string?> PromptAsync(ContentPage page, string title, string message,
        string accept = "OK", string cancel = "Cancel", string? placeholder = null,
        int maxLength = -1, Keyboard? keyboard = null, string? initialValue = null)
    {
#if WINDOWS
        return DesktopDialogs.PromptAsync(page, title, message, accept, cancel, placeholder, maxLength, keyboard, initialValue);
#else
        var input = new Entry { Text = initialValue, Placeholder = placeholder, Keyboard = keyboard ?? Keyboard.Default,
            TextColor = MobileTheme.Primary, PlaceholderColor = MobileTheme.Muted, BackgroundColor = Colors.Transparent,
            FontSize = 15, HeightRequest = 44, Margin = new Thickness(10, 0) };
        MobileTheme.Input(input);
        if (maxLength > 0) input.MaxLength = maxLength;
        var field = new Border { Content = input, Stroke = MobileTheme.Divider, StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 }, BackgroundColor = MobileTheme.Canvas };
        input.Focused += (_, _) => field.Stroke = MobileTheme.Accent;
        input.Unfocused += (_, _) => field.Stroke = MobileTheme.Divider;
        var content = new VerticalStackLayout { Spacing = 12 };
        content.Add(MobileTheme.Label(message, 13, MobileTheme.Secondary)); content.Add(field);
        return ShowAsync(page, title, content, finish =>
        {
            var buttons = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
            var cancelButton = TextButton(cancel); cancelButton.Clicked += (_, _) => finish(null);
            var acceptButton = TextButton(accept, true); acceptButton.Clicked += (_, _) => finish(input.Text ?? "");
            input.Completed += (_, _) => finish(input.Text ?? "");
            buttons.Add(cancelButton); buttons.Add(acceptButton, 1);
            input.Loaded += (_, _) => input.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), () => input.Focus());
            return buttons;
        });
#endif
    }

    internal static async Task<string> ChooseAsync(ContentPage page, string title, string cancel, string? destruction, params string[] buttons)
    {
#if WINDOWS
        return await DesktopDialogs.ChooseAsync(page, title, cancel, destruction, buttons);
#else
        var choices = new VerticalStackLayout { Spacing = 4 };
        Action<string?>? finishChoice = null;
        foreach (var caption in buttons.Concat(destruction is null ? [] : new[] { destruction }))
        {
            var item = TextButton(caption);
            item.HorizontalOptions = LayoutOptions.Fill; item.HeightRequest = -1; item.MinimumHeightRequest = 46;
            if (caption == destruction) item.TextColor = Color.FromArgb("#F08080");
            item.Clicked += (_, _) => finishChoice?.Invoke(caption);
            item.Text = "";
            SemanticProperties.SetDescription(item, caption);
            var row = new Grid { ColumnDefinitions = { new(22), new(GridLength.Star) }, ColumnSpacing = 12, Padding = new Thickness(10, 8), MinimumHeightRequest = 46 };
            row.Add(new Image { Source = ActionIcon(caption), WidthRequest = 20, HeightRequest = 20 });
            var label = MobileTheme.Label(caption, 14); if (caption == destruction) label.TextColor = Color.FromArgb("#F18A96");
            row.Add(label, 1); Grid.SetColumnSpan(item, 2); row.Add(item); choices.Add(row);
        }
        var scroll = new ScrollView { Content = choices, MaximumHeightRequest = 360 };
        return await ShowAsync(page, title, scroll, finish =>
        {
            finishChoice = finish;
            var dismiss = TextButton(cancel); dismiss.Clicked += (_, _) => finish(null); return dismiss;
        }) ?? cancel;
#endif
    }

    private static Button TextButton(string text, bool primary = false) => new()
    {
        Text = text, FontSize = 14, HeightRequest = 44, CornerRadius = 12, BorderWidth = 0,
        Padding = new Thickness(14, 6), TextColor = MobileTheme.Primary,
        BackgroundColor = primary ? MobileTheme.Accent : Colors.Transparent
    };

    private static string ActionIcon(string action) => action switch
    {
        "New note" or "Rename" or "Find in note" => "edit_20_regular.png",
        "New folder" => "folder_add.png", "Daily note" => "calendar.png",
        "Quick Thought" => "flash_20_regular.png", "Search" => "search_20_regular.png",
        "Settings" => "settings_20_regular.png", "Sync" => "arrow_sync_20_regular.png",
        "Bookmark / unbookmark" => "bookmark.png", "Version history" or "Recent notes" => "history_20_regular.png",
        "Move" => "folder_open_20_regular.png", "Copy" or "Duplicate" or "Copy path" => "copy.png",
        "Delete note" or "Delete folder" or "Discard" => "delete.png", _ => "document_20_regular.png"
    };

    private static async Task<string?> ShowAsync(ContentPage page, string title, View content, Func<Action<string?>, View> footer)
    {
        if (active.ContainsKey(page)) return null;
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        active.Add(page, () => completion.TrySetResult(null));
        var original = page.Content;
        var host = original as Grid;
        if (host is null) { page.Content = null; host = new Grid(); host.Add(original); page.Content = host; }
        var overlay = new Grid { BackgroundColor = Color.FromArgb("#99000000"), ZIndex = 1000 };
        Grid.SetRowSpan(overlay, Math.Max(1, host.RowDefinitions.Count));
        Grid.SetColumnSpan(overlay, Math.Max(1, host.ColumnDefinitions.Count));
        var backdrop = new BoxView { Color = Colors.Transparent };
        var cancelTap = new TapGestureRecognizer(); cancelTap.Tapped += (_, _) => completion.TrySetResult(null);
        backdrop.GestureRecognizers.Add(cancelTap); overlay.Add(backdrop);
        var layout = new VerticalStackLayout { Spacing = 14, Padding = 20 };
        layout.Add(MobileTheme.Label(title, 18, weight: FontAttributes.Bold)); layout.Add(content);
        layout.Add(footer(value => completion.TrySetResult(value)));
        var scroller = new ScrollView { Content = layout, MaximumHeightRequest = Math.Max(180, page.Height - 40) };
        var panel = new Border { Content = scroller, Stroke = MobileTheme.Divider, StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 20 }, BackgroundColor = MobileTheme.Surface,
            WidthRequest = Math.Min(440, Math.Max(240, page.Width - 24)), Margin = new Thickness(12, 12, 12, 16), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.End };
        overlay.Add(panel);
        void Disappearing(object? sender, EventArgs e) => completion.TrySetResult(null);
        page.Disappearing += Disappearing;
        host.Add(overlay);
        panel.Opacity = 0;
        try
        {
            await panel.FadeToAsync(1, 100, Easing.Linear);
            return await completion.Task;
        }
        finally { host.Remove(overlay); page.Disappearing -= Disappearing; active.Remove(page); }
    }
}

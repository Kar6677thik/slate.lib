#if WINDOWS
using Microsoft.Maui.Controls.Shapes;
namespace Slate.Lib.App;

internal static class DesktopTheme
{
    internal static readonly Color Canvas = Color.FromArgb("#191B22");
    internal static readonly Color Surface = Color.FromArgb("#22242D");
    internal static readonly Color Raised = Color.FromArgb("#2C2F3B");
    internal static readonly Color Divider = Color.FromArgb("#444857");
    internal static readonly Color Text = Color.FromArgb("#F2F3F7");
    internal static readonly Color Muted = Color.FromArgb("#9C9EAE");
    internal static readonly Color Accent = Color.FromArgb("#8FB6FF");
    internal static Label Label(string text, double size = 14, bool bold = false) => new()
    {
        Text = text, FontSize = size, TextColor = Text, FontFamily = "Segoe UI Variable",
        FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None, VerticalOptions = LayoutOptions.Center
    };
    internal static Button Button(string text, bool primary = false) => new()
    {
        Text = text, FontSize = 13, FontFamily = "Segoe UI Variable", HeightRequest = 38,
        CornerRadius = 9, Padding = new Thickness(16, 5), BorderWidth = 0, TextColor = Text,
        BackgroundColor = primary ? Color.FromArgb("#3478F6") : Raised
    };
    internal static Border Field(Entry input)
    {
        input.TextColor = Text; input.PlaceholderColor = Muted; input.BackgroundColor = Colors.Transparent;
        input.FontFamily = "Segoe UI Variable"; input.FontSize = 14; input.HeightRequest = 40;
        input.Margin = new Thickness(10, 0);
        input.HandlerChanged += (_, _) =>
        {
            if (input.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox box)
            {
                box.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
                box.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        };
        var field = new Border { Content = input, Stroke = Divider, StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 9 }, BackgroundColor = Canvas };
        input.Focused += (_, _) => field.Stroke = Accent;
        input.Unfocused += (_, _) => field.Stroke = Divider;
        return field;
    }
    internal static string Icon(string caption) => caption switch
    {
        "Reading view" => "eye_20_regular.png", "Source mode" or "Rename" or "New note" or "New note here" => "edit_20_regular.png",
        "Split view" => "split_vertical_20_regular.png", "Find in note" or "Find a note" => "search_20_regular.png",
        "New folder" or "New folder here" => "folder_add.png", "Daily note" => "calendar.png",
        "Quick Thought" => "flash_20_regular.png", "Bookmarks" or "Bookmark / unbookmark" => "bookmark.png",
        "Version history" => "history_20_regular.png", "Settings" => "settings_20_regular.png",
        "Sync" or "Check for updates" => "arrow_sync_20_regular.png",
        "Toggle file explorer" => "panel_left.png", "Toggle right sidebar" => "panel_right.png",
        "Copy" or "Copy path" or "Duplicate" => "copy.png",
        "Move" or "Cut" or "Paste here" => "folder_open_20_regular.png",
        "Delete…" => "delete.png", _ => "document_20_regular.png"
    };
}

internal static class DesktopDialogs
{
    private static readonly Dictionary<Page, Action> active = [];
    internal static bool IsOpen(Page page) => active.ContainsKey(page);
    internal static bool TryDismiss(Page page)
    {
        if (!active.TryGetValue(page, out var close)) return false;
        close(); return true;
    }
    internal static Task<string?> PromptAsync(ContentPage page, string title, string message, string accept,
        string cancel, string? placeholder, int maxLength, Keyboard? keyboard, string? initialValue)
    {
        var input = new Entry { Text = initialValue, Placeholder = placeholder, Keyboard = keyboard ?? Keyboard.Default };
        if (maxLength > 0) input.MaxLength = maxLength;
        var content = new VerticalStackLayout { Spacing = 12 };
        var description = DesktopTheme.Label(message, 13); description.TextColor = DesktopTheme.Muted;
        content.Add(description); content.Add(DesktopTheme.Field(input));
        return ShowAsync(page, title, DesktopTheme.Icon(title), content, complete =>
        {
            var footer = new HorizontalStackLayout { HorizontalOptions = LayoutOptions.End, Spacing = 8 };
            var dismiss = DesktopTheme.Button(cancel); dismiss.Clicked += (_, _) => complete(null);
            var submit = DesktopTheme.Button(accept, true); submit.Clicked += (_, _) => complete(input.Text ?? "");
            input.Completed += (_, _) => complete(input.Text ?? "");
            input.Loaded += (_, _) => input.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () => input.Focus());
            footer.Add(dismiss); footer.Add(submit); return footer;
        }, 440);
    }
    internal static async Task<string> ChooseAsync(ContentPage page, string title, string cancel, string? destruction, string[] buttons)
    {
        var choices = new VerticalStackLayout { Spacing = 3 };
        var rows = new List<(string Caption, View Row)>();
        Action<string?>? finish = null;
        foreach (var caption in buttons.Concat(destruction is null ? [] : new[] { destruction }))
        {
            var row = new Grid { ColumnDefinitions = { new(22), new(GridLength.Star), new(GridLength.Auto) },
                ColumnSpacing = 11, MinimumHeightRequest = 36, Padding = new Thickness(10, 6), BackgroundColor = Colors.Transparent };
            row.Add(new Image { Source = DesktopTheme.Icon(caption), WidthRequest = 18, HeightRequest = 18, Opacity = .85 });
            var label = DesktopTheme.Label(caption, 13);
            if (caption == destruction || caption.StartsWith("Delete")) label.TextColor = Color.FromArgb("#F18A96");
            row.Add(label, 1);
            var shortcut = caption switch { "New note" => "Ctrl N", "Find a note" => "Ctrl K", "New folder" => "Ctrl Shift N", "Version history" => "History", _ => "" };
            var hint = DesktopTheme.Label(shortcut, 11); hint.TextColor = DesktopTheme.Muted; row.Add(hint, 2);
            var activate = new Button { BackgroundColor = Colors.Transparent, Text = "", BorderWidth = 0, CornerRadius = 8, Padding = 0, MinimumHeightRequest = 36 };
            SemanticProperties.SetDescription(activate, caption);
            activate.Clicked += (_, _) => finish?.Invoke(caption);
            Grid.SetColumnSpan(activate, 3); row.Add(activate);
            var surface = new Border { Content = row, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 8 } };
            var pointer = new PointerGestureRecognizer();
            pointer.PointerEntered += (_, _) => surface.BackgroundColor = DesktopTheme.Raised;
            pointer.PointerExited += (_, _) => surface.BackgroundColor = Colors.Transparent;
            var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => finish?.Invoke(caption);
            surface.GestureRecognizers.Add(pointer); surface.GestureRecognizers.Add(tap);
            SemanticProperties.SetDescription(surface, caption);
            choices.Add(surface); rows.Add((caption, surface));
        }
        var body = new VerticalStackLayout { Spacing = 12 };
        if (title == "Commands")
        {
            var filter = new Entry { Placeholder = "What would you like to do?" };
            filter.TextChanged += (_, e) => { foreach (var row in rows) row.Row.IsVisible = row.Caption.Contains(e.NewTextValue ?? "", StringComparison.OrdinalIgnoreCase); };
            filter.Completed += (_, _) => finish?.Invoke(rows.FirstOrDefault(x => x.Row.IsVisible).Caption);
            body.Add(DesktopTheme.Field(filter));
            filter.Loaded += (_, _) => filter.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () => filter.Focus());
        }
        body.Add(new ScrollView { Content = choices, MaximumHeightRequest = Math.Max(180, Math.Min(460, page.Height - 240)) });
        return await ShowAsync(page, title, title == "Commands" ? "command.png" : "document_20_regular.png", body, complete =>
        {
            finish = complete;
            var dismiss = DesktopTheme.Button(cancel); dismiss.HorizontalOptions = LayoutOptions.End;
            dismiss.Clicked += (_, _) => complete(null); return dismiss;
        }, 460) ?? cancel;
    }
    internal static async Task<string?> ShowAsync(ContentPage page, string title, string icon, View content, Func<Action<string?>,View> footer, double width = 480)
    {
        if (active.ContainsKey(page)) return null;
        var complete = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = (Grid)page.Content;
        var overlay = new Grid { BackgroundColor = Color.FromArgb("#A6000000"), ZIndex = 1000 };
        Grid.SetRowSpan(overlay, Math.Max(1, host.RowDefinitions.Count));
        Grid.SetColumnSpan(overlay, Math.Max(1, host.ColumnDefinitions.Count));
        var backdrop = new BoxView { Color = Colors.Transparent };
        var cancelTap = new TapGestureRecognizer(); cancelTap.Tapped += (_, _) => complete.TrySetResult(null);
        backdrop.GestureRecognizers.Add(cancelTap); overlay.Add(backdrop);
        var heading = new Grid { ColumnDefinitions = { new(24), new(GridLength.Star) }, ColumnSpacing = 12 };
        heading.Add(new Image { Source = icon, WidthRequest = 22, HeightRequest = 22 });
        heading.Add(DesktopTheme.Label(title, 20, true), 1);
        var stack = new VerticalStackLayout { Spacing = 20, Padding = 24 };
        stack.Add(heading); stack.Add(content); stack.Add(footer(result => complete.TrySetResult(result)));
        var scroller = new ScrollView { Content = stack, MaximumHeightRequest = Math.Max(200, page.Height - 48) };
        var panel = new Border { Content = scroller, BackgroundColor = DesktopTheme.Surface, Stroke = DesktopTheme.Divider, StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 }, WidthRequest = Math.Min(width, page.Width - 48),
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            Shadow = new Shadow { Brush = Brush.Black, Opacity = .55f, Radius = 36, Offset = new Point(0, 18) } };
        overlay.Add(panel);
        void Disappearing(object? sender, EventArgs e) => complete.TrySetResult(null);
        active.Add(page, () => complete.TrySetResult(null));
        page.Disappearing += Disappearing; host.Add(overlay);
        panel.Opacity = 0; panel.TranslationY = 8;
        try
        {
            await Task.WhenAll(panel.FadeToAsync(1, 140), panel.TranslateToAsync(0, 0, 140, Easing.CubicOut));
            return await complete.Task;
        }
        finally { host.Remove(overlay); page.Disappearing -= Disappearing; active.Remove(page); }
    }
}
#endif


using Microsoft.Maui.Controls.Shapes;

namespace Slate.Lib.App;

// Native text controls supply editing behavior; the surrounding Border owns the visual surface.
internal static class ClientControls
{
    internal static void RoundPanel(VisualElement panel, double radius = 12)
    {
        panel.SizeChanged += (_, _) =>
        {
            if (panel.Width > 0 && panel.Height > 0)
                panel.Clip = new RoundRectangleGeometry { Rect = new Rect(0, 0, panel.Width, panel.Height), CornerRadius = new CornerRadius(radius) };
        };
    }

    internal static void Configure()
    {
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("SlateInput", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.Background = null;
            handler.PlatformView.SetPadding(12, 0, 12, 0);
#elif WINDOWS
            StripChrome(handler.PlatformView);
#endif
        });
        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("SlateInput", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.Background = null;
            handler.PlatformView.SetPadding(16, 16, 16, 16);
#elif WINDOWS
            StripChrome(handler.PlatformView);
#endif
        });
    }

#if WINDOWS
    private static void StripChrome(Microsoft.UI.Xaml.Controls.Control input)
    {
        input.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
        input.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
                     "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
            input.Resources[key] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }
#endif

    internal static View NavigationRow(string text, Func<Task> activate, string? icon = null, double indent = 0)
    {
        var height = DeviceInfo.Platform == DevicePlatform.WinUI ? 32 : 44;
        var row = new Grid { ColumnDefinitions = { new(icon is null ? 0 : 20), new(GridLength.Star) },
            ColumnSpacing = icon is null ? 0 : 10, Padding = new Thickness(12 + indent, 0, 12, 0), HeightRequest = height };
        if (icon is not null) row.Add(new Image { Source = icon, WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center });
        row.Add(new Label { Text = text, FontSize = 13, TextColor = MobileTheme.Primary,
            VerticalOptions = LayoutOptions.Center, VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 }, 1);
        var hit = new Button { Text = "", BackgroundColor = Colors.Transparent, BorderWidth = 0, CornerRadius = 8,
            Padding = 0, HeightRequest = height, HorizontalOptions = LayoutOptions.Fill };
        SemanticProperties.SetDescription(hit, text);
        ToolTipProperties.SetText(hit, text);
        hit.Clicked += async (_, _) =>
        {
            Element? owner = hit;
            while (owner is not null && owner is not Page) owner = owner.Parent;
            try { await activate(); }
            catch (Exception) { if (owner is Page page) await SlateDialogs.AlertAsync(page, "Unable to open", "This item could not be opened. Please try again.", "OK"); }
        };
        Grid.SetColumnSpan(hit, 2); row.Add(hit);
        var surface = new Border { Content = row, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 8 } };
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => surface.BackgroundColor = MobileTheme.Raised;
        pointer.PointerExited += (_, _) => surface.BackgroundColor = Colors.Transparent;
        surface.GestureRecognizers.Add(pointer);
        return surface;
    }
}

using Microsoft.Maui.Controls.Shapes;
using System.Globalization;

namespace Slate.Lib.App;

internal static class MobileTheme
{
    internal static readonly Color Canvas = Color.FromArgb("#191B22");
    internal static readonly Color Surface = Color.FromArgb("#22242D");
    internal static readonly Color Raised = Color.FromArgb("#2C2F3B");
    internal static readonly Color Control = Color.FromArgb("#343743");
    internal static readonly Color Divider = Color.FromArgb("#363946");
    internal static readonly Color Primary = Color.FromArgb("#F2F3F7");
    internal static readonly Color Secondary = Color.FromArgb("#ADB3C5");
    internal static readonly Color Muted = Color.FromArgb("#8B92A5");
    internal static readonly Color Accent = Color.FromArgb("#3478F6");
    internal static readonly Color AccentSoft = Color.FromArgb("#263A59");
    internal static readonly Color Success = Color.FromArgb("#34D399");

    internal static void Apply(ContentPage page)
    {
        page.BackgroundColor = Canvas;
    }

    internal static Label Label(string text, double size = 14, Color? color = null, FontAttributes weight = FontAttributes.None) => new()
    {
        Text = text,
        FontSize = size,
        TextColor = color ?? Primary,
        FontAttributes = weight,
        VerticalTextAlignment = TextAlignment.Center,
        VerticalOptions = LayoutOptions.Center
    };

    internal static Label Section(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 11,
        FontAttributes = FontAttributes.Bold,
        CharacterSpacing = 0.8,
        TextColor = Secondary
    };

    internal static Button Button(string text, string? icon = null, bool primary = false, bool compact = false)
    {
        var button = new Button
        {
            Text = text,
            ImageSource = icon,
            ContentLayout = icon is null
                ? new Microsoft.Maui.Controls.Button.ButtonContentLayout(Microsoft.Maui.Controls.Button.ButtonContentLayout.ImagePosition.Left, 0)
                : new Microsoft.Maui.Controls.Button.ButtonContentLayout(Microsoft.Maui.Controls.Button.ButtonContentLayout.ImagePosition.Left, 8),
            BackgroundColor = primary ? Accent : Colors.Transparent,
            TextColor = Primary,
            BorderColor = primary ? Accent : Divider,
            BorderWidth = 0,
            CornerRadius = 12,
            FontSize = compact ? 12 : 14,
            FontAttributes = primary ? FontAttributes.Bold : FontAttributes.None,
            Padding = compact ? new Thickness(11, 6) : new Thickness(14, 10),
            HeightRequest = 46
        };
        SemanticProperties.SetDescription(button, text);
        ToolTipProperties.SetText(button, text);
        return button;
    }

    internal static ImageButton IconButton(string source, string description, bool primary = false, double size = 42)
    {
        size = Math.Max(44, size);
        var button = new ImageButton
        {
            Source = source,
            BackgroundColor = primary ? Accent : Colors.Transparent,
            BorderColor = primary ? Accent : Divider,
            BorderWidth = 0,
            CornerRadius = 12,
            Padding = primary ? 13 : 10,
            WidthRequest = size,
            HeightRequest = size
        };
        SemanticProperties.SetDescription(button, description);
        return button;
    }

    internal static void Input(InputView input)
    {
        input.TextColor = Primary;
        input.PlaceholderColor = Muted;
        input.BackgroundColor = Colors.Transparent;
        input.FontSize = 14;
        if (input is Entry) input.MinimumHeightRequest = 48;
    }

    internal static Border Frame(View content, Thickness? padding = null, float radius = 14)
    {
        return new Border
        {
            Content = content,
            BackgroundColor = Surface,
            Stroke = Divider,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(radius) },
            Padding = padding ?? new Thickness(0)
        };
    }

    internal static BoxView Rule() => new() { HeightRequest = 1, Color = Divider, HorizontalOptions = LayoutOptions.Fill };
}

internal sealed class LibraryEntryIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "folder_20_regular.png" : "document_20_regular.png";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

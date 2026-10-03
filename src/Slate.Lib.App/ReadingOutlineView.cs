using System.Text.Json;

namespace Slate.Lib.App;

internal sealed record ReadingHeading(string Id, string Text, int Level, int Depth);
internal static class ReadingOutlineView
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static T? Decode<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "null") return default;
        if (json.StartsWith('"')) json = JsonSerializer.Deserialize<string>(json) ?? "null";
        if (json.StartsWith('%')) json = Uri.UnescapeDataString(json);
        return JsonSerializer.Deserialize<T>(json, Json);
    }
    internal static async Task PopulateAsync(VerticalStackLayout rows, WebView reader, Func<Task>? close = null)
    {
        rows.Clear();
        var raw = await reader.EvaluateJavaScriptAsync("encodeURIComponent(JSON.stringify(window.slateReading?.outline() ?? []))");
        var headings = Decode<ReadingHeading[]>(raw) ?? [];
        var controls = new HorizontalStackLayout { Spacing = 4 };
        foreach (var (icon, caption, script) in new[] { ("arrow_left_20_regular.png", "Previous heading", "window.slateReading?.step(-1)"), ("arrow_right.png", "Next heading", "window.slateReading?.step(1)") })
        {
            var button = MobileTheme.IconButton(icon, caption, size: 38);
            button.Clicked += async (_, _) => { if (close is not null) await close(); await reader.EvaluateJavaScriptAsync(script); }; controls.Add(button);
        }
        var copy = MobileTheme.IconButton("copy.png", "Copy current heading link", size: 38);
        copy.Clicked += async (_, _) => { var value = Decode<string>(await reader.EvaluateJavaScriptAsync("encodeURIComponent(JSON.stringify(window.slateReading?.link() ?? ''))")); if (!string.IsNullOrEmpty(value)) await Clipboard.Default.SetTextAsync(value); }; controls.Add(copy);
        rows.Add(controls);
        var views = new Dictionary<string, View>();
        foreach (var heading in headings)
        {
            var row = ClientControls.NavigationRow(heading.Text, async () =>
            {
                if (close is not null) await close();
                await reader.EvaluateJavaScriptAsync("window.slateReading?.jump(" + JsonSerializer.Serialize(heading.Id) + ")");
            }, indent: Math.Min(heading.Depth, 5) * 14);
            rows.Add(row); views[heading.Id] = row;
        }
        if (headings.Length == 0) rows.Add(MobileTheme.Label("No headings, or the preview is still loading. Open Outline again when ready.", 13, MobileTheme.Secondary));
        if (headings.Length == 1024) rows.Add(MobileTheme.Label("Showing the first 1,024 headings.", 12, MobileTheme.Secondary));
        var timer = rows.Dispatcher.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(1); var reading = false;
        timer.Tick += async (_, _) =>
        {
            if (reading || rows.Handler is null) return;
            Element? parent = rows; while (parent is not null) { if (parent is VisualElement visual && !visual.IsVisible) return; parent = parent.Parent; }
            reading = true;
            try
            {
                var id = Decode<string>(await reader.EvaluateJavaScriptAsync("encodeURIComponent(JSON.stringify(window.slateReading?.active() ?? ''))"));
                foreach (var item in views) item.Value.BackgroundColor = item.Key == id ? MobileTheme.Raised : Colors.Transparent;
            }
            catch { } finally { reading = false; }
        };
        // Stop when the generated control leaves the tree; rebuilding an outline cannot accumulate timers.
        controls.Loaded += (_, _) => timer.Start(); controls.Unloaded += (_, _) => timer.Stop();
        if (controls.Handler is not null) timer.Start();
    }
}

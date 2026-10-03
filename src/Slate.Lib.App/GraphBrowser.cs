using Slate.Lib.Core;

namespace Slate.Lib.App;

internal static class GraphBrowser
{
    internal static async Task<Guid?> RelatedAsync(ContentPage owner, LibraryApiClient api, Guid id)
    {
        var notes = await api.RelatedAsync(id);
        var captions = notes.Select((x, i) => $"{i + 1}. {x.Title}\n{x.Path}\n{string.Join(" · ", x.Reasons)}").ToArray();
        var chosen = await SlateDialogs.ChooseAsync(owner, notes.Count == 0 ? "No related notes yet" : "Related notes · explained scores", "Close", null, captions);
        var index = Array.IndexOf(captions, chosen); return index < 0 ? null : notes[index].Id;
    }
    internal static async Task<Guid?> OpenAsync(ContentPage owner, LibraryApiClient api, Guid id)
    {
        var depth = 1; string? folder = null, type = null;
        while (true)
        {
            var graph = await api.GraphAsync(id, depth, 40, folder, type);
            Guid? selected = null;
            var picture = new GraphDrawing(graph);
            var canvas = new GraphicsView { Drawable = picture, HeightRequest = 260, HorizontalOptions = LayoutOptions.Fill };
            SemanticProperties.SetDescription(canvas, "Nearby linked notes. Use the numbered list below to open a note with keyboard or screen reader.");
            canvas.EndInteraction += (_, e) => { if (e.Touches.Length > 0 && picture.Hit(e.Touches[0]) is { } hit) { selected = hit; SlateDialogs.TryDismiss(owner); } };
            var body = new VerticalStackLayout { Spacing = 8 }; body.Add(canvas);
            body.Add(MobileTheme.Label($"Depth {depth} · {graph.Nodes.Count}/40 notes · {graph.Edges.Count} directed links" + (graph.Limited ? " · Limit reached" : "") + "\nTap a node or choose its numbered row.", 12, MobileTheme.Secondary));
            var list = new VerticalStackLayout { Spacing = 2 };
            foreach (var (node, index) in graph.Nodes.Select((node, index) => (node, index)))
            {
                var button = new Button { Text = $"{index + 1}. {node.Title}", FontSize = 13, MinimumHeightRequest = 44, BackgroundColor = Colors.Transparent, TextColor = MobileTheme.Primary, HorizontalOptions = LayoutOptions.Fill };
                SemanticProperties.SetDescription(button, node.Path);
                button.Clicked += (_, _) => { selected = node.Id; SlateDialogs.TryDismiss(owner); }; list.Add(button);
            }
            body.Add(new ScrollView { Content = list, MaximumHeightRequest = 130 });
            var action = await SlateDialogs.ContentAsync(owner, "Nearby notes", body, "Depth and filters", "Close");
            if (selected is not null) return selected;
            if (action != "accept") return null;
            var setting = await SlateDialogs.ChooseAsync(owner, "Graph controls", "Back", null, "Depth 1", "Depth 2", "Depth 3", "Filter by folder", "Filter by type", "Clear filters");
            if (setting.StartsWith("Depth ")) depth = int.Parse(setting[^1..]);
            if (setting == "Filter by folder") folder = await SlateDialogs.PromptAsync(owner, "Folder filter", "Only traverse notes in this folder; blank shows all", initialValue: folder);
            if (setting == "Filter by type") type = await SlateDialogs.PromptAsync(owner, "Type filter", "Exact metadata type, for example question; blank shows all", initialValue: type);
            if (setting == "Clear filters") folder = type = null;
        }
    }
    private sealed class GraphDrawing(KnowledgeGraph graph) : IDrawable
    {
        private readonly Dictionary<Guid, PointF> points = [];
        internal Guid? Hit(PointF position) => points.Where(x => (x.Value.X-position.X)*(x.Value.X-position.X)+(x.Value.Y-position.Y)*(x.Value.Y-position.Y) <= 324).Select(x => (Guid?)x.Key).FirstOrDefault();
        public void Draw(ICanvas canvas, RectF area)
        {
            points.Clear(); var center = new PointF(area.Width / 2, area.Height / 2); points[graph.Focus] = center;
            var others = graph.Nodes.Where(x => x.Id != graph.Focus).ToArray();
            for (var i = 0; i < others.Length; i++)
            {
                var angle = 2 * Math.PI * i / others.Length - Math.PI / 2;
                var radius = others.Length > 18 && i % 2 == 0 ? .30f : .43f;
                points[others[i].Id] = new(center.X + (float)Math.Cos(angle) * area.Width * radius, center.Y + (float)Math.Sin(angle) * area.Height * radius);
            }
            canvas.StrokeColor = Color.FromArgb("#636A80"); canvas.StrokeSize = 1;
            foreach (var edge in graph.Edges)
            {
                var a = points[edge.Source]; var b = points[edge.Target]; canvas.DrawLine(a, b);
                var angle = Math.Atan2(b.Y-a.Y, b.X-a.X); var tip = new PointF(b.X - 15*(float)Math.Cos(angle), b.Y - 15*(float)Math.Sin(angle));
                canvas.DrawLine(tip, new(tip.X - 6*(float)Math.Cos(angle-.5), tip.Y - 6*(float)Math.Sin(angle-.5)));
                canvas.DrawLine(tip, new(tip.X - 6*(float)Math.Cos(angle+.5), tip.Y - 6*(float)Math.Sin(angle+.5)));
            }
            for (var i = 0; i < graph.Nodes.Count; i++)
            {
                var node = graph.Nodes[i]; var p = points[node.Id];
                canvas.FillColor = node.Id == graph.Focus ? Color.FromArgb("#3478F6") : Color.FromArgb("#3B4153"); canvas.FillCircle(p, 14);
                canvas.FontColor = Colors.White; canvas.FontSize = 11; canvas.DrawString((i + 1).ToString(), p.X-14, p.Y-14, 28, 28, HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }
    }
}

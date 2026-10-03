using Slate.Lib.Core;

namespace Slate.Lib.App;

internal static class RediscoveryBrowser
{
    private static readonly (string Name, string Key)[] Views = [("Something forgotten", "forgotten"), ("Old ideas", "old-idea"), ("Unanswered older questions", "old-question"), ("On this day", "on-this-day"), ("Recently learned", "recently-learned"), ("Continue learning", "continue-learning"), ("Timeline", "timeline"), ("Random note", "random")];
    internal static async Task<Guid?> OpenAsync(ContentPage owner, LibraryApiClient api, ClientStateStore? state)
    {
        while (true)
        {
            var chosen = await SlateDialogs.ChooseAsync(owner, "Rediscover", "Close", null, Views.Select(x => x.Name).Append("Reading history privacy").ToArray());
            if (chosen == "Reading history privacy")
            {
                if (state is null) { await SlateDialogs.AlertAsync(owner, "Reading history", "Connect to a library first.", "Done"); continue; }
                var activity = await state.Activity.ReadAsync();
                var action = await SlateDialogs.ChooseAsync(owner, $"Reading history is {(activity.Enabled ? "on" : "off")}\nOnly on this device. At most 1,000 notes for 180 days. Never sent to the server.", "Back", null, activity.Enabled ? "Turn off" : "Turn on", "Clear history");
                if (action == "Turn on") await state.Activity.ConfigureAsync(true);
                if (action == "Turn off") await state.Activity.ConfigureAsync(false);
                if (action == "Clear history") await state.Activity.ConfigureAsync(activity.Enabled, true);
                continue;
            }
            var view = Views.FirstOrDefault(x => x.Name == chosen); if (view.Key is null) return null;
            var page = 0;
            while (true)
            {
                var result = await api.RediscoverAsync(view.Key, DateOnly.FromDateTime(DateTime.Now), page);
                var activity = state is null ? null : await state.Activity.ReadAsync();
                var hits = result.Results.Where(x => view.Key != "forgotten" || activity?.Enabled != true || !activity.Notes.Any(a => a.NoteId == x.Id && a.LastOpenedAt >= DateTimeOffset.UtcNow.AddDays(-30))).ToArray();
                var labels = hits.Select((x, i) => $"{i + 1}. {x.Title}\n{x.Path}\n{x.Reason}" + (view.Key == "forgotten" ? activity?.Enabled == true ? " · Not opened here in the last 30 days of recorded activity" : " · Reading history is off" : "")).ToArray();
                var options = labels.ToList();
                if (page > 0) options.Add("Previous page"); if ((page + 1) * 20 < result.Total && view.Key != "random") options.Add("Next page");
                if (view.Key == "random") options.Add("Another random note");
                var pick = await SlateDialogs.ChooseAsync(owner, view.Name + (hits.Length == 0 ? " · No matches on this page" : ""), "Back", null, options.ToArray());
                if (pick == "Next page") { page++; continue; } if (pick == "Previous page") { page--; continue; } if (pick == "Another random note") continue;
                var index = Array.IndexOf(labels, pick); if (index >= 0) return hits[index].Id;
                break;
            }
        }
    }
    internal static async Task RecordAsync(ClientStateStore? state, Guid id)
    {
        if (state is null) return;
        try { await state.Activity.OpenedAsync(id); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { System.Diagnostics.Debug.WriteLine("Reading history could not be saved: " + error.GetType().Name); }
    }
}

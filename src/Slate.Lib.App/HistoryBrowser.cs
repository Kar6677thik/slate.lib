using Slate.Lib.Core;

namespace Slate.Lib.App;

internal static class HistoryBrowser
{
    internal static async Task<LibraryNote?> OpenAsync(ContentPage owner, LibraryApiClient api, Guid id)
    {
        var history = await api.HistoryAsync(id, default);
        if (history.Count == 0) { await SlateDialogs.AlertAsync(owner, "History", "No committed versions yet.", "Done"); return null; }
        while (true)
        {
            var entry = await ChooseAsync(owner, history, "Version history · latest 100"); if (entry is null) return null;
            var old = await api.HistoricalNoteAsync(id, entry.Commit, default);
            var action = await SlateDialogs.ChooseAsync(owner, $"{old.Timestamp.ToLocalTime():g} · {entry.Author}\n{old.Message}", "Back", null, "Read historical version", "Compare with current", "Compare two versions", "Restore this version", "Restore as new note");
            if (action == "Read historical version") await ShowTextAsync(owner, old.Path, old.Markdown);
            if (action == "Compare with current")
            {
                var current = await api.ReadAsync(id, default);
                await ShowTextAsync(owner, "Historical → current", await Task.Run(() => TextMerge.VisualDiff(old.Markdown, current.Markdown)));
            }
            if (action == "Compare two versions")
            {
                var other = await ChooseAsync(owner, history, "Choose the comparison version"); if (other is null) continue;
                var right = await api.HistoricalNoteAsync(id, other.Commit, default);
                await ShowTextAsync(owner, $"{old.Timestamp:g} → {right.Timestamp:g}", await Task.Run(() => TextMerge.VisualDiff(old.Markdown, right.Markdown)));
            }
            if (action == "Restore this version")
            {
                var current = await api.ReadAsync(id, default);
                var diff = await Task.Run(() => TextMerge.VisualDiff(current.Markdown, old.Markdown));
                if (await ConfirmAsync(owner, "Restore as a new current revision", "Existing history stays intact.\n\n" + diff)) return await api.RestoreAsync(id, new(old.Commit, "current", current.Revision));
            }
            if (action == "Restore as new note") return await RecoverAtAsync(owner, api, old, "new");
        }
    }
    internal static async Task<LibraryNote?> RecoverAsync(ContentPage owner, LibraryApiClient api)
    {
        var recovery = await api.RecoverableAsync(); var page = 0;
        while (true)
        {
            var notes = recovery.Notes.Skip(page * 20).Take(20).ToArray(); var labels = notes.Select((x, i) => $"{i + 1}. {x.Path} · {x.DeletedAt.ToLocalTime():d}").ToArray(); var choices = labels.ToList();
            if (page > 0) choices.Add("Previous page"); if ((page + 1) * 20 < recovery.Notes.Count) choices.Add("Next page");
            var pick = await SlateDialogs.ChooseAsync(owner, recovery.Bounded ? "Recover deleted notes · recent bounded history" : "Recover deleted notes", "Close", null, choices.ToArray());
            if (pick == "Next page") { page++; continue; } if (pick == "Previous page") { page--; continue; }
            var index = Array.IndexOf(labels, pick); if (index < 0) return null;
            var selected = notes[index]; var historical = await api.HistoricalNoteAsync(selected.Id, selected.SourceCommit, default);
            var action = await SlateDialogs.ChooseAsync(owner, historical.Path, "Back", null, "Read before recovery", "Recover note", "Restore as new note");
            if (action == "Read before recovery") await ShowTextAsync(owner, "Historical Markdown", historical.Markdown);
            else if (action is "Recover note" or "Restore as new note") return await RecoverAtAsync(owner, api, historical, action == "Recover note" ? "recover" : "new");
        }
    }
    private static async Task<LibraryNote?> RecoverAtAsync(ContentPage owner, LibraryApiClient api, HistoricalNote old, string mode)
    {
        var slash = old.Path.LastIndexOf('/');
        var folder = await SlateDialogs.PromptAsync(owner, "Destination", "Existing folder path; blank means Library", initialValue: slash < 0 ? "" : old.Path[..slash]); if (folder is null) return null;
        var name = await SlateDialogs.PromptAsync(owner, "Filename", "Existing files will never be overwritten", initialValue: Path.GetFileNameWithoutExtension(old.Path) + (mode == "new" ? " restored" : "")); if (string.IsNullOrWhiteSpace(name)) return null;
        if (!await ConfirmAsync(owner, "Review recovery", $"Destination: {folder}/{name}\nFrom {old.Timestamp.ToLocalTime():g}\n\n{old.Markdown}")) return null;
        return await api.RestoreAsync(old.Id, new(old.Commit, mode, Folder: folder, Name: name));
    }
    private static async Task<NoteHistoryEntry?> ChooseAsync(ContentPage owner, IReadOnlyList<NoteHistoryEntry> history, string title)
    {
        var page = 0;
        while (true)
        {
            var entries = history.Skip(page * 20).Take(20).ToArray(); var labels = entries.Select((x, i) => $"{i + 1}. {x.Timestamp.ToLocalTime():g} · {x.Message} · {x.Commit[..7]}").ToArray(); var choices = labels.ToList();
            if (page > 0) choices.Add("Previous page"); if ((page + 1) * 20 < history.Count) choices.Add("Next page");
            var chosen = await SlateDialogs.ChooseAsync(owner, title, "Close", null, choices.ToArray());
            if (chosen == "Next page") { page++; continue; } if (chosen == "Previous page") { page--; continue; }
            var index = Array.IndexOf(labels, chosen); return index >= 0 ? entries[index] : null;
        }
    }
    internal static async Task ShowTextAsync(ContentPage owner, string title, string text)
    {
        const int chunk = 16000;
        for (var offset = 0; offset < text.Length; offset += chunk)
        {
            var editor = new Editor { Text = text.Substring(offset, Math.Min(chunk, text.Length - offset)), IsReadOnly = true, FontSize = 13, HeightRequest = 320 }; MobileTheme.Input(editor);
            if (await SlateDialogs.ContentAsync(owner, $"{title} · page {offset / chunk + 1}", editor, offset + chunk < text.Length ? "Next page" : "Done", "Close") != "accept") break;
        }
    }
    private static async Task<bool> ConfirmAsync(ContentPage owner, string title, string text)
    {
        var editor = new Editor { Text = text.Length > 16000 ? text[..16000] + "\n…preview truncated; Read historical version shows the full source." : text, IsReadOnly = true, HeightRequest = 300, FontSize = 13 }; MobileTheme.Input(editor);
        return await SlateDialogs.ContentAsync(owner, title, editor, "Restore", "Keep current") == "accept";
    }
}

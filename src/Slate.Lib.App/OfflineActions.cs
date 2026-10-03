using Slate.Lib.Core;

namespace Slate.Lib.App;

internal sealed class OfflinePendingException(string message) : Exception(message);

internal static class OfflineActions
{
    internal static async Task<LibraryNote> SaveAsync(ClientStateStore state, LibraryApiClient api, DraftRecord draft, ReplayNoteRequest request, string path, CancellationToken token = default)
    {
        await state.SaveDraftAsync(draft with { ReadyToSubmit = true }, token);
        var operation = await state.Offline.Queue.EnqueueAsync(draft.DraftId, request with { LibraryId = state.LibraryId }, path, draft.PendingAssets);
        var results = await state.Offline.Queue.ReplayAsync(api, operation.Id, token);
        var result = results.Single();
        if (result.Result is { } note) { await state.Offline.Queue.AcknowledgeAsync(operation.Id); return note; }
        if (result.State == "conflict") throw new HttpRequestException(result.LastError, null, System.Net.HttpStatusCode.PreconditionFailed);
        throw new OfflinePendingException("Saved on this device · queued for retry. " + result.LastError);
    }
    internal static Task<LibraryNote> EditAsync(ClientStateStore state, LibraryApiClient api, LibraryNote note, string markdown, IReadOnlyList<PendingAsset> assets, CancellationToken token = default) =>
        SaveAsync(state, api, new(note.Id, DraftKind.ExistingNote, markdown, DateTimeOffset.UtcNow, note.Id, note.Path, note.Revision, note.Markdown, PendingAssets: assets),
            new(Guid.NewGuid(), note.Id, "edit", markdown, note.Revision), note.Path, token);
    internal static async Task<LibraryNote> CreateAsync(ClientStateStore state, LibraryApiClient api, CreateNoteRequest request, IReadOnlyList<PendingAsset> assets, CancellationToken token = default)
    {
        var id = request.Id ?? Guid.NewGuid(); var path = (request.FolderPath.Length == 0 ? "" : request.FolderPath + "/") + request.Name;
        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) path += ".md";
        var markdown = request.InitialMarkdown;
        if (string.IsNullOrWhiteSpace(markdown)) markdown = $"---\nid: {id}\n---\n\n# {request.Title ?? request.Name}\n";
        return await SaveAsync(state, api, new(id, DraftKind.NewNote, markdown, DateTimeOffset.UtcNow, id, path, PendingAssets: assets),
            new(Guid.NewGuid(), id, "create", markdown, Create: request with { Id = id, InitialMarkdown = markdown }), path, token);
    }
    internal static async Task<Guid?> OpenAsync(ContentPage owner, ClientStateStore state, LibraryApiClient api, Guid? currentNote = null)
    {
        while (true)
        {
            var usage = await state.Offline.StatusAsync(); var queue = await state.Offline.Queue.ReadAsync();
            var pending = queue.Where(x => x.State is not ("completed" or "cancelled")).ToArray();
            var actions = new List<string> { "Downloaded notes / offline search", "Download folder", "Download pinned folder", "Manage downloads", "Set quota", $"Pending work ({pending.Length})", "Retry pending work" };
            if (currentNote is not null) actions.Insert(0, "Download current note");
            var action = await SlateDialogs.ChooseAsync(owner, $"Offline · {usage.UsedBytes / 1048576d:0.#} / {usage.Manifest.QuotaBytes / 1048576d:0} MiB", "Close", null, actions.ToArray());
            if (action is null or "Close") return null;
            if (action == "Downloaded notes / offline search")
            {
                var query = await SlateDialogs.PromptAsync(owner, "Offline search", "Search downloaded titles, paths and text. Leave blank for all.", maxLength: 200);
                if (query is null) continue;
                var notes = (await state.Offline.SearchAsync(query)).ToArray();
                var page = 0;
                while (true)
                {
                    var items = notes.Skip(page * 20).Take(20).ToArray(); var labels = items.Select((x, i) => $"{i + 1}. {x.Note.Path}").ToArray(); var options = labels.ToList();
                    if (page > 0) options.Add("Previous page"); if ((page + 1) * 20 < notes.Length) options.Add("Next page");
                    var pick = await SlateDialogs.ChooseAsync(owner, $"Downloaded copies · {notes.Length} · may be stale", "Close", null, options.ToArray());
                    if (pick == "Next page") { page++; continue; } if (pick == "Previous page") { page--; continue; }
                    var index = Array.IndexOf(labels, pick); if (index >= 0) return items[index].Note.Id; break;
                }
            }
            else if (action is "Download current note" or "Download folder" or "Download pinned folder")
            {
                string? folder = null; Guid? id = action == "Download current note" ? currentNote : null;
                if (action == "Download folder") folder = await SlateDialogs.PromptAsync(owner, "Download folder", "Canonical folder path; blank downloads the library (maximum 2000 notes).", maxLength: 500);
                if (action == "Download pinned folder")
                {
                    var pins = (await state.Workspace.ReadAsync()).Pins;
                    folder = await SlateDialogs.ChooseAsync(owner, "Pinned folders", "Cancel", null, pins.Select(x => x.Path).ToArray()); if (folder == "Cancel") folder = null;
                }
                if (id is null && folder is null) continue;
                await DownloadAsync(owner, state, api, id, folder);
            }
            else if (action == "Manage downloads")
            {
                var selections = usage.Manifest.Selections;
                var labels = selections.Select((x, i) => $"{i + 1}. {x.Label} · {x.State} · {x.Downloaded} notes").ToArray();
                var pick = await SlateDialogs.ChooseAsync(owner, "Downloads", "Cancel", null, labels); var index = Array.IndexOf(labels, pick); if (index < 0) continue;
                var selected = selections[index];
                var choice = await SlateDialogs.ChooseAsync(owner, $"{selected.Label}\nLast refreshed: {selected.RefreshedAt?.ToLocalTime().ToString("g") ?? "never"}\n{selected.Error}", "Cancel", "Remove downloaded copies", "Refresh download");
                if (choice == "Refresh download") await DownloadAsync(owner, state, api, selected.NoteId, selected.Folder);
                if (choice == "Remove downloaded copies") await state.Offline.RemoveAsync(selected.Key);
            }
            else if (action == "Set quota")
            {
                var input = await SlateDialogs.PromptAsync(owner, "Offline quota", "MiB, 16–2048. Drafts and pending uploads are never evicted.", initialValue: (usage.Manifest.QuotaBytes / 1048576).ToString(), maxLength: 4);
                if (int.TryParse(input, out var amount)) await state.Offline.SetQuotaAsync(amount * 1048576L);
            }
            else if (action == "Retry pending work") await state.ReconcileOfflineAsync(api);
            else if (action.StartsWith("Pending work"))
            {
                var labels = pending.Select((x, i) => $"{i + 1}. {x.TargetPath} · {x.State} · {x.Attempts} attempts").ToArray();
                var pick = await SlateDialogs.ChooseAsync(owner, "Pending work", "Close", null, labels); var index = Array.IndexOf(labels, pick); if (index < 0) continue;
                var selected = pending[index];
                var choice = await SlateDialogs.ChooseAsync(owner, selected.LastError ?? "Waiting to send; attachments are sent first.", "Close", selected.Attempts == 0 ? "Cancel queued save (keep draft)" : null, selected.State == "conflict" && selected.Request.Kind == "edit" ? "Resolve conflict" : "Retry");
                if (choice == "Resolve conflict")
                {
                    var draft = await state.ReadDraftAsync(selected.DraftId);
                    if (draft?.BaseMarkdown is null || draft.BaseRevision is null) throw new InvalidOperationException("The original comparison base is unavailable. Your queued text and draft are preserved.");
                    var basis = new LibraryNote(selected.Request.NoteId, selected.TargetPath, Path.GetFileNameWithoutExtension(selected.TargetPath), draft.BaseMarkdown, draft.BaseRevision);
                    if (await MergeWorkbench.OpenAsync(owner, api, state, basis, draft.Markdown) is { } merged) return merged.Id;
                }
                if (choice == "Retry") await state.Offline.Queue.ReplayAsync(api, selected.Id);
                if (choice == "Cancel queued save (keep draft)") await state.Offline.Queue.CancelAsync(selected.Id);
            }
        }
    }
    private static async Task DownloadAsync(ContentPage owner, ClientStateStore state, LibraryApiClient api, Guid? id, string? folder)
    {
        using var cancel = new CancellationTokenSource(); var label = MobileTheme.Label("Preparing download…", 14);
        var dialog = SlateDialogs.ContentAsync(owner, "Offline download", label, "Close", "Cancel download");
        var download = state.Offline.DownloadAsync(api, id, folder, new Progress<string>(text => label.Text = text), cancel.Token);
        if (await Task.WhenAny(dialog, download) == dialog) cancel.Cancel();
        var result = await download; label.Text = result.State == "ready" ? $"Downloaded {result.Downloaded} notes. Available offline; refresh to check for server changes." : result.Error;
        await dialog;
    }
}

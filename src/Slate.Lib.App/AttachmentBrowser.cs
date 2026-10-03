using Slate.Lib.Core;

namespace Slate.Lib.App;

internal static class AttachmentBrowser
{
    internal static async Task<Guid?> OpenAsync(ContentPage owner, LibraryApiClient api)
    {
        var page = 0; var unused = false;
        while (true)
        {
            var result = await api.AssetsAsync(page, unused);
            var labels = result.Results.Select((x, i) => $"{i + 1}. {x.Metadata.OriginalFilename} · {Size(x.Metadata.ByteSize)} · {x.ReferenceCount} references").ToArray();
            var actions = labels.ToList();
            if (page > 0) actions.Add("Previous page");
            if ((page + 1) * 20 < result.Total && page < 1999) actions.Add("Next page");
            actions.Add(unused ? "Show all attachments" : "Show unreferenced attachments");
            var selected = await SlateDialogs.ChooseAsync(owner, $"Attachments · {result.Total} · {Size(result.TotalBytes)}", "Close", null, actions.ToArray());
            if (selected is null or "Close") return null;
            if (selected == "Previous page") { page--; continue; }
            if (selected == "Next page") { page++; continue; }
            if (selected.StartsWith("Show ")) { unused = !unused; page = 0; continue; }
            var index = Array.IndexOf(labels, selected); if (index < 0) return null;
            if (await InspectAsync(owner, api, result.Results[index]) is { } note) return note;
        }
    }
    private static string Size(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024d:0.#} KiB" : $"{bytes / (1024d * 1024):0.#} MiB";
    private static async Task<Guid?> InspectAsync(ContentPage owner, LibraryApiClient api, AssetListing listing)
    {
        var asset = listing.Metadata;
        var body = new VerticalStackLayout { Spacing = 10 };
        if (asset.InlineImage)
        {
            try { var bytes = await api.AssetThumbnailAsync(asset.Id); body.Add(new Image { Source = ImageSource.FromStream(() => new MemoryStream(bytes)), HeightRequest = 128, Aspect = Aspect.AspectFit }); }
            catch (HttpRequestException) { body.Add(MobileTheme.Label("Thumbnail unavailable. The original attachment is unchanged.", 12, MobileTheme.Secondary)); }
        }
        body.Add(MobileTheme.Label($"{asset.ContentType} · {Size(asset.ByteSize)}\nCreated {asset.CreatedAt.ToLocalTime():g}\n{listing.ReferenceCount} referencing notes", 13));
        body.Add(MobileTheme.Label("SHA-256\n" + asset.Sha256, 11, MobileTheme.Secondary));
        if (await SlateDialogs.ContentAsync(owner, asset.OriginalFilename, body, "Actions", "Close") != "accept") return null;
        var actions = new List<string> { "Referencing notes", "View extracted text" };
        if (asset.ContentType == "application/pdf" || asset.InlineImage) actions.Add(asset.InlineImage ? "Run local image OCR" : "Extract PDF text");
        if (listing.ReferenceCount == 0) actions.Add("Review permanent removal");
        var action = await SlateDialogs.ChooseAsync(owner, asset.OriginalFilename, "Close", null, actions.ToArray());
        if (action == "Referencing notes")
        {
            var page = 0;
            while (true)
            {
                var refs = await api.AssetReferencesAsync(asset.Id, page);
                var labels = refs.Results.Select((x, i) => $"{i + 1}. {x.Path}").ToArray(); var choices = labels.ToList();
                if (page > 0) choices.Add("Previous page"); if ((page + 1) * 20 < refs.Total && page < 1999) choices.Add("Next page");
                var choice = await SlateDialogs.ChooseAsync(owner, $"Referencing notes · {refs.Total}", "Close", null, choices.ToArray());
                if (choice == "Next page") { page++; continue; } if (choice == "Previous page") { page--; continue; }
                var index = Array.IndexOf(labels, choice); return index >= 0 ? refs.Results[index].NoteId : null;
            }
        }
        if (action is "View extracted text" or "Run local image OCR" or "Extract PDF text")
        {
            var state = action == "View extracted text" ? await api.AssetTextAsync(asset.Id) : await api.ExtractAssetAsync(asset.Id);
            var text = state.State == "ready" ? state.Text : state.Error ?? $"State: {state.State}. Choose extraction to generate text.";
            // Bound the visual tree/text control independently from extraction size.
            var page = 0; const int chunk = 12000;
            do
            {
                var part = text.Substring(page * chunk, Math.Min(chunk, text.Length - page * chunk));
                var content = new VerticalStackLayout { Spacing = 8, Children = { MobileTheme.Label("Derived text · not authored Markdown. Extraction can be incomplete or inaccurate.", 12, MobileTheme.Secondary), new ScrollView { HeightRequest = 260, Content = MobileTheme.Label(part, 14) } } };
                if (await SlateDialogs.ContentAsync(owner, $"Extracted text · page {page + 1}", content, (page + 1) * chunk < text.Length ? "Next page" : "Done", "Close") != "accept") break;
                page++;
            } while (page * chunk < text.Length);
        }
        if (action == "Review permanent removal")
        {
            var preview = await api.PreviewAssetCleanupAsync(asset.Id);
            var content = MobileTheme.Label(preview.Warning + $"\n\nRemove {asset.OriginalFilename} permanently and reclaim {Size(preview.ByteSize)}?", 14);
            if (await SlateDialogs.ContentAsync(owner, "Remove unused attachment", content, "Permanently remove", "Keep") == "accept")
            {
                var removed = await api.CleanupAssetAsync(asset.Id, new(preview.Sha256, preview.LibraryVersion));
                await SlateDialogs.AlertAsync(owner, "Attachment removed", $"Reclaimed {Size(removed.ReclaimedBytes)}.", "Done");
            }
        }
        return null;
    }
}

using System.Net;
using Slate.Lib.Core;

namespace Slate.Lib.App;

internal sealed record KnowledgeInput(string Title, string Body, string Url);
internal static class KnowledgeDialogs
{
    internal static async Task<KnowledgeInput?> ComposeAsync(ContentPage page, string kind)
    {
        var title = new Entry { Placeholder = kind == "question" ? "What do you want to understand?" : "Title (optional)" };
        var url = new Entry { Placeholder = "https://…", Keyboard = Keyboard.Url };
        var body = new Editor { Placeholder = kind == "question" ? "Context or details (optional)" : "Your comment (optional)", HeightRequest = 160, AutoSize = EditorAutoSizeOption.Disabled };
        MobileTheme.Input(title); MobileTheme.Input(url); MobileTheme.Input(body);
        var form = new VerticalStackLayout { Spacing = 8 };
        if (kind == "link") form.Add(MobileTheme.Frame(url, 0, 10));
        form.Add(MobileTheme.Frame(title, 0, 10)); form.Add(MobileTheme.Frame(body, 0, 10));
        var result = await SlateDialogs.ContentAsync(page, kind == "question" ? "Question" : "Add Link", form, "Create");
        return result is null ? null : new(title.Text ?? "", body.Text ?? "", url.Text ?? "");
    }
    internal static async Task<string?> AnswerAsync(ContentPage page)
    {
        var editor = new Editor { Placeholder = "Write your answer…", HeightRequest = 220, AutoSize = EditorAutoSizeOption.Disabled };
        MobileTheme.Input(editor);
        return await SlateDialogs.ContentAsync(page, "Answer question", MobileTheme.Frame(editor, 0, 10), "Add answer") is null ? null : editor.Text;
    }
    internal static CreateNoteRequest CreateRequest(KnowledgeInput input, string kind, string folder, Guid id, DateTimeOffset now)
    {
        var markdown = kind == "question" ? KnowledgeWorkflows.Question(id, input.Title, input.Body, now) : KnowledgeWorkflows.Link(id, input.Url, input.Title, input.Body, now);
        var title = NoteDocument.Parse(markdown, "note.md").Title;
        var stem = new string(title.Take(38).Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (stem.Length == 0) stem = kind;
        return new(folder.Length == 0 ? "inbox" : folder, stem + "-" + id.ToString("N")[..8], title, id, markdown);
    }
    internal static async Task<LibraryNote> CreateAsync(LibraryApiClient api, KnowledgeInput input, string kind, string folder, Guid? identity = null, DateTimeOffset? instant = null)
    {
        if (folder.Length == 0) { folder = "inbox"; try { await api.CreateFolderAsync(new("", folder), default); } catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Conflict) { } }
        var request = CreateRequest(input, kind, folder, identity ?? Guid.NewGuid(), instant ?? DateTimeOffset.UtcNow);
        try { return await api.CreateNoteAsync(request, default); }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.Conflict)
        {
            var existing = await api.ReadAsync(request.Id!.Value, default);
            if (existing.Markdown == request.InitialMarkdown && existing.Path == request.FolderPath + "/" + request.Name + ".md") return existing;
            throw;
        }
    }
    internal static async Task<(bool Cancelled, string? Markdown)> TemplateAsync(ContentPage page, LibraryApiClient api, Guid id, string title)
    {
        FolderPage templates;
        try { templates = await api.ListAsync("templates", 0, default); }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound) { return (false, null); }
        catch (HttpRequestException e) when (e.StatusCode is null) { return (false, null); }
        var files = templates.Entries.Where(x => !x.IsDirectory && x.Id is not null).ToArray();
        if (files.Length == 0) return (false, null);
        var labels = files.Select(x => x.Path).ToArray();
        var chosen = await SlateDialogs.ChooseAsync(page, "Start from template", "Cancel", null, ["Blank note", .. labels]);
        if (chosen == "Cancel") return (true, null); if (chosen == "Blank note") return (false, null);
        var file = files.First(x => x.Path == chosen); var source = await api.ReadAsync(file.Id!.Value, default);
        return (false, KnowledgeWorkflows.Template(source.Markdown, id, title, DateOnly.FromDateTime(DateTime.Today), DateTimeOffset.UtcNow));
    }
    internal static async Task<LibraryNote?> AppendAsync(ContentPage page, LibraryApiClient api, LibraryNote capture)
    {
        var path = await SlateDialogs.PromptAsync(page, "Append to note", "Destination note path in Library", placeholder: "projects/overview.md");
        if (string.IsNullOrWhiteSpace(path)) return null;
        var destination = await api.ReadPathAsync(path, default);
        var preview = await api.PreviewAppendAsync(destination.Id, capture.Id);
        var source = new Editor { Text = preview.ProposedMarkdown, IsReadOnly = true, HeightRequest = 260, FontFamily = "monospace", FontSize = 12 };
        MobileTheme.Input(source);
        var form = new VerticalStackLayout { Spacing = 8, Children = { MobileTheme.Label("Append to " + preview.DestinationPath + ". The source capture stays intact.", 13), source } };
        if (await SlateDialogs.ContentAsync(page, "Review append", form, "Append") is null) return null;
        return await api.AppendCaptureAsync(destination.Id, new(capture.Id, preview.DestinationRevision, preview.CaptureRevision));
    }
    internal static async Task<LibraryNote?> AppendDraftAsync(ContentPage page, LibraryApiClient api, ClientStateStore state, CaptureNoteRequest capture, IReadOnlyList<PendingAsset> assets)
    {
        var path = await SlateDialogs.PromptAsync(page, "Append capture", "Destination note path in Library"); if (string.IsNullOrWhiteSpace(path)) return null;
        var destination = await api.ReadPathAsync(path, default); var parts = new List<string>();
        foreach (var asset in assets)
        { var metadata = await PendingAssetStore.UploadAsync(api, asset); parts.Add(AssetReferences.ForNote("capture.md", metadata, metadata.OriginalFilename)); }
        if (!string.IsNullOrWhiteSpace(capture.Content)) parts.Add(capture.Content);
        if (!string.IsNullOrWhiteSpace(capture.Comment)) parts.Add(capture.Comment);
        var source = NoteDocument.AddId(string.Join("\n\n", parts) + "\n", capture.CaptureId);
        var proposed = KnowledgeWorkflows.AppendCapture(destination.Markdown, destination.Path, source, "capture.md");
        var review = new VerticalStackLayout { Spacing = 8, Children = { MobileTheme.Label("Append to " + destination.Path + ". No new note is created.", 13),
            new Editor { Text = proposed, IsReadOnly = true, FontFamily = "monospace", FontSize = 12, HeightRequest = 260 } } };
        if (await SlateDialogs.ContentAsync(page, "Review capture append", review, "Append") is null) return null;
        // Save the merged draft before the network write. A failed save resumes through the existing conflict-safe editor.
        if (await state.ReadDraftAsync(destination.Id) is { } prior && prior.Markdown != destination.Markdown)
            throw new InvalidOperationException("This device already has an unsent draft for that note. Resume it before appending.");
        await state.SaveDraftAsync(new(destination.Id, DraftKind.ExistingNote, proposed, DateTimeOffset.UtcNow, destination.Id, destination.Path, destination.Revision, destination.Markdown));
        var saved = await api.UpdateNoteAsync(destination.Id, new(proposed, destination.Revision), default);
        await state.DeleteDraftAsync(destination.Id); await state.CacheNoteAsync(saved); return saved;
    }
}

public partial class MainPage
{
    private async Task CreateKnowledgeAsync(string kind)
    {
        if (!await ResolveUnsavedAsync()) return;
        var input = await KnowledgeDialogs.ComposeAsync(this, kind); if (input is null) return;
        await RunMutationAsync(async () => { var note = await KnowledgeDialogs.CreateAsync(api, input, kind, TargetFolder()); await ReloadTreeAsync(note.Path); await OpenNote(_ => Task.FromResult(note)); }, "The note could not be created. Your library was preserved.");
    }
    private async Task AnswerQuestionAsync()
    {
        if (currentNote is not { } note || !await ResolveUnsavedAsync()) return;
        note = await api.ReadAsync(note.Id, connection.Token); var answer = await KnowledgeDialogs.AnswerAsync(this); if (string.IsNullOrWhiteSpace(answer)) return;
        await RunMutationAsync(async () => { var saved = await api.AnswerAsync(note.Id, new(answer, note.Revision, DateTimeOffset.UtcNow)); await OpenNote(_ => Task.FromResult(saved)); }, "The question changed; review it before answering.");
    }
    private async Task AppendCurrentNoteAsync()
    {
        if (currentNote is not { } note || !await ResolveUnsavedAsync()) return;
        var appended = await KnowledgeDialogs.AppendAsync(this, api, await api.ReadAsync(note.Id, connection.Token));
        if (appended is not null) await OpenNote(_ => Task.FromResult(appended));
    }
}

public sealed class MobileKnowledgePage : MobilePage
{
    private readonly string kind;
    private readonly Entry title = new() { Placeholder = "Title" };
    private readonly Entry url = new() { Placeholder = "https://…", Keyboard = Keyboard.Url };
    private readonly Editor body = new() { Placeholder = "Context or comment…", AutoSize = EditorAutoSizeOption.Disabled };
    private readonly string folder;
    private readonly Guid id = Guid.NewGuid();
    private readonly DateTimeOffset createdAt = DateTimeOffset.UtcNow;
    private DraftDebouncer? drafts;
    public MobileKnowledgePage(MobileSession session, string kind, string folder = "") : base(session, kind == "question" ? "Question" : "Add Link")
    {
        this.kind = kind; this.folder = folder;
        title.TextChanged += (_, _) => Persist(); url.TextChanged += (_, _) => Persist(); body.TextChanged += (_, _) => Persist();
        MobileTheme.Input(title); MobileTheme.Input(url); MobileTheme.Input(body);
        var form = new Grid { Padding = 16, RowSpacing = 8, RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        form.Add(MobileTheme.Frame(title, 0, 10)); var urlField = MobileTheme.Frame(url, 0, 10); urlField.IsVisible = kind == "link"; form.Add(urlField, 0, 1); form.Add(MobileTheme.Frame(body, 0, 10), 0, 2);
        var save = MobileTheme.Button("Create", "save_20_regular.png", true); save.Clicked += async (_, _) =>
        {
            try
            {
                Persist(); if (drafts is not null) await drafts.FlushAsync();
                var note = await KnowledgeDialogs.CreateAsync(Session.Api, new(title.Text ?? "", body.Text ?? "", url.Text ?? ""), kind, this.folder, id, createdAt);
                drafts?.Clear(); if (Session.State is not null) await Session.State.DeleteDraftAsync(id);
                await Navigation.PushAsync(new MobileNotePage(Session, note.Id));
            }
            catch (Exception e) { await ShowErrorAsync(e, "This note could not be created. Your text is still here."); }
        }; form.Add(save, 0, 3); Content = form;
    }
    protected override void OnAppearing()
    {
        base.OnAppearing(); if (Session.State is null) Session.LoadKnownLocalState();
        if (Session.State is not null) drafts ??= new(Session.State); title.Focus();
    }
    protected override void OnDisappearing() { _ = drafts?.FlushAsync(); base.OnDisappearing(); }
    private void Persist()
    {
        if (drafts is null || string.IsNullOrWhiteSpace(title.Text + body.Text + url.Text)) return;
        CreateNoteRequest request;
        if (kind == "link" && !Uri.TryCreate(url.Text, UriKind.Absolute, out _))
        {
            // Retain incomplete URLs as plain draft text; they are never submitted automatically.
            request = new(folder.Length == 0 ? "inbox" : folder, "link-" + id.ToString("N")[..8], title.Text, id,
                FrontMatter.SetScalars(NoteDocument.AddId("# " + (title.Text ?? "Link") + "\n\n" + url.Text + "\n\n" + body.Text, id), new Dictionary<string, string> { ["type"] = "link" }));
        }
        else
        {
            try { request = KnowledgeDialogs.CreateRequest(new(string.IsNullOrWhiteSpace(title.Text) ? (kind == "question" ? "Question" : "Link") : title.Text, body.Text ?? "", url.Text ?? ""), kind, folder, id, createdAt); }
            catch (ArgumentException) { return; }
        }
        drafts.Schedule(new(id, DraftKind.NewNote, request.InitialMarkdown!, DateTimeOffset.UtcNow,
            TargetPath: request.FolderPath + "/" + request.Name + ".md", ReadyToSubmit: false));
    }
}

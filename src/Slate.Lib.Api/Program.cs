using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Slate.Lib.Api;
using Slate.Lib.Core;

var command = args.FirstOrDefault();
if (command == "--asset-worker")
{
    try { await LocalAssetExtractor.WorkerAsync(args[1], args[2], args[3]); }
    catch { Environment.ExitCode = 1; }
    return;
}
var setup = command is "--initialize-library" or "--create-device-token" or "--list-devices" or "--revoke-device" or "--rotate-device-token" or "--rebuild-index" or "--initialize-git";
var builder = WebApplication.CreateBuilder(setup ? [] : args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
string ConfiguredPath(string key) => Path.GetFullPath(builder.Configuration[key] ?? throw new InvalidOperationException($"Configure {key}."), builder.Environment.ContentRootPath);
var authenticationFile = ConfiguredPath("Authentication:DeviceFile");
var tokens = new DeviceTokens(authenticationFile);
if (command == "--create-device-token")
{
    Console.WriteLine(tokens.Create(args.ElementAtOrDefault(1) ?? "Windows"));
    return;
}
if (command == "--rotate-device-token")
{
    Console.WriteLine(tokens.Rotate(args.ElementAtOrDefault(1) ?? throw new ArgumentException("Provide the device label to rotate.")));
    return;
}
if (command == "--revoke-device")
{
    var revoked = tokens.Revoke(args.ElementAtOrDefault(1) ?? throw new ArgumentException("Provide the device label to revoke."));
    Console.WriteLine($"Revoked {revoked.Label} ({revoked.Id:D}).");
    return;
}
if (command == "--list-devices")
{
    foreach (var device in tokens.Load())
        Console.WriteLine($"{device.Label}\t{device.Status}\tcreated {device.CreatedAt:O}\tlast used {(device.LastUsedAt?.ToString("O") ?? "never")}\t{device.Id:D}");
    return;
}
var paths = new LibraryPaths(ConfiguredPath("Library:RootPath"));
if (command == "--initialize-library")
{
    Console.WriteLine($"Initialized library; added IDs to {LibrarySetup.Initialize(paths)} note(s).");
    return;
}
var derivedRoot = Path.GetFullPath(builder.Configuration["Data:DerivedPath"] ?? "../../.local/derived", builder.Environment.ContentRootPath);
var assetRoot = Path.GetFullPath(builder.Configuration["Data:AssetsPath"] ?? "../../.local/assets", builder.Environment.ContentRootPath);
var assetRelative = Path.GetRelativePath(paths.Root, assetRoot);
if (!assetRelative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && assetRelative != ".." && !Path.IsPathRooted(assetRelative))
    throw new InvalidOperationException("Data:AssetsPath must be outside the Markdown library root.");
var assetOptions = new AssetOptions
{
    RootPath = assetRoot,
    MaximumBytes = builder.Configuration.GetValue("Data:MaximumAssetBytes", 25L * 1024 * 1024)
};
var stateRoot = Path.GetFullPath(builder.Configuration["Git:StatePath"] ?? "../../.local/server/state", builder.Environment.ContentRootPath);
var backupStatusFile = Path.GetFullPath(builder.Configuration["Backup:StatusFile"] ?? Path.Combine(stateRoot, "backup-status.json"), builder.Environment.ContentRootPath);
var updateOptions = new UpdateOptions
{
    ManifestPath = Path.GetFullPath(builder.Configuration["Updates:ManifestPath"] ?? "../../.local/releases/release-manifest.json", builder.Environment.ContentRootPath),
    ArtifactRoot = Path.GetFullPath(builder.Configuration["Updates:ArtifactRoot"] ?? "../../.local/releases", builder.Environment.ContentRootPath)
};
var trustedProxyNetworks = (builder.Configuration["Networking:TrustedProxyNetworks"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var trustedProxies = (builder.Configuration["Networking:TrustedProxies"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var gitOptions = new GitOptions
{
    RemoteName = builder.Configuration["Git:RemoteName"] ?? "origin",
    Branch = builder.Configuration["Git:Branch"] ?? "main",
    StatePath = stateRoot,
    CommitDebounceSeconds = builder.Configuration.GetValue("Git:CommitDebounceSeconds", 30),
    CommitMaximumSeconds = builder.Configuration.GetValue("Git:CommitMaximumSeconds", 120),
    SyncIntervalSeconds = builder.Configuration.GetValue("Git:SyncIntervalSeconds", 60),
    CommandTimeoutSeconds = builder.Configuration.GetValue("Git:CommandTimeoutSeconds", 30),
    AuthorName = builder.Configuration["Git:AuthorName"] ?? "Slate",
    AuthorEmail = builder.Configuration["Git:AuthorEmail"] ?? "slate@localhost"
};
if (command == "--rebuild-index")
{
    using var index = new SearchIndex(Path.Combine(derivedRoot, "search"));
    var library = new LibraryStore(paths);
    Console.WriteLine($"Rebuilt search index with {index.Rebuild(library.AllNotes())} note(s).");
    return;
}
if (command == "--initialize-git")
{
    using var loggerFactory = LoggerFactory.Create(logging => logging.AddSimpleConsole(options => options.SingleLine = true));
    var git = new GitSyncService(paths, Microsoft.Extensions.Options.Options.Create(gitOptions), loggerFactory.CreateLogger<GitSyncService>());
    await git.InitializeAsync();
    Console.WriteLine("Initialized local Git history for the Library.");
    return;
}
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(_ => new SearchIndex(Path.Combine(derivedRoot, "search")));
builder.Services.AddSingleton<LinkIndex>();
builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(assetOptions));
builder.Services.AddSingleton<AssetStore>();
builder.Services.AddSingleton(provider => new AssetDerivatives(provider.GetRequiredService<AssetStore>(), Path.Combine(derivedRoot, "attachments"), new LocalAssetExtractor()));
builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(gitOptions));
builder.Services.AddSingleton<GitSyncService>();
builder.Services.AddSingleton<LibraryStore>();
builder.Services.AddSingleton(tokens);
builder.Services.AddSingleton(new OperationalHealth(paths.Root, assetRoot, authenticationFile, backupStatusFile));
builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(updateOptions));
builder.Services.AddSingleton<UpdateCatalog>();
builder.Services.AddHostedService<GitBackgroundService>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
    foreach (var proxy in trustedProxies) options.KnownProxies.Add(IPAddress.Parse(proxy));
    foreach (var network in trustedProxyNetworks) options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
});
var app = builder.Build();
app.UseForwardedHeaders();
app.Logger.LogInformation("Reading library at {LibraryRoot}", paths.Root);
try
{
    _ = app.Services.GetRequiredService<LibraryStore>();
    if (!tokens.Load().Any(device => device.RevokedAt is null))
        app.Logger.LogWarning("No active devices are configured. API access remains denied until --create-device-token is run.");
}
catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
{
    app.Logger.LogCritical("Library startup failed: {Reason}. Correct the configuration/content and restart.", exception.Message);
    throw;
}

app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    try
    {
        if (context.Request.Path.StartsWithSegments("/health")) { await next(context); return; }
        if (!context.Request.IsHttps && context.Connection.RemoteIpAddress is { } remote && !IPAddress.IsLoopback(remote))
        {
            await Results.Problem("Use HTTPS outside localhost.", statusCode: 403).ExecuteAsync(context); return;
        }
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal) || !tokens.Accepts(authorization[7..]))
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await Results.Problem("A valid device token is required.", statusCode: 401).ExecuteAsync(context); return;
        }
        await next(context);
    }
    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or LibraryConflictException or LibraryPreconditionException)
    {
        var (code, message) = exception switch
        {
            AssetTooLargeException => (413, "The attachment exceeds the configured size limit."),
            FileNotFoundException or DirectoryNotFoundException => (404, "The note or folder no longer exists."),
            InvalidDataException => (422, "The note or library metadata is invalid. Check the server log."),
            LibraryConflictException => (409, exception.Message),
            LibraryPreconditionException => (412, "The note changed since it was opened. Your draft was not saved."),
            SearchQueryException => (400, exception.Message),
            ArgumentException => (400, "Invalid library path or request."),
            InvalidOperationException => (503, exception.Message),
            _ => (503, "The library could not be read. Check the server log.")
        };
        app.Logger.LogWarning("Library request failed with {StatusCode}: {Reason}", code, exception.Message);
        await Results.Problem(message, statusCode: code).ExecuteAsync(context);
    }
});

app.MapGet("/health/live", (OperationalHealth health) => Results.Ok(new { status = "Healthy", version = health.Version }));
app.MapGet("/health/ready", (OperationalHealth health) =>
{
    var ready = health.Readiness();
    return ready.Ready ? Results.Ok(new { status = ready.State }) : Results.Json(new { status = "NotReady", detail = ready.State }, statusCode: 503);
});
app.MapGet("/v1/status", (LibraryStore library, SearchIndex search, LinkIndex links, GitSyncService git, OperationalHealth health) =>
{
    var backup = health.ReadBackupStatus();
    return new LibraryStatus(library.LibraryId, library.NoteCount, library.LibraryVersion, search.SearchVersion, search.State, git.Status,
        health.Version, links.Version, "Ready", health.StartedAt, backup?.CompletedAt, backup?.State);
});
app.MapGet("/v1/devices", (DeviceTokens devices) => devices.Load());
app.MapGet("/v1/updates", (UpdateCatalog updates, string platform, string currentVersion) => updates.Check(platform, currentVersion));
app.MapGet("/v1/releases/{filename}", (UpdateCatalog updates, string filename) =>
{
    var release = updates.Open(filename);
    return Results.File(release.Path, "application/octet-stream", release.Metadata.FileName, enableRangeProcessing: true);
});
app.MapGet("/v1/library", (LibraryStore library, string? path, int? page) => library.List(path ?? "", page ?? 0));
IResult NoteResult(HttpContext context, LibraryNote note)
{
    context.Response.Headers.ETag = note.Revision;
    return context.Request.Headers.IfNoneMatch.ToString() == note.Revision ? Results.StatusCode(304) : Results.Ok(note);
}
app.MapGet("/v1/notes/by-path", (HttpContext context, LibraryStore library, string path) => NoteResult(context, library.ReadPath(path)));
app.MapGet("/v1/notes/{id:guid}", (HttpContext context, LibraryStore library, Guid id) => NoteResult(context, library.Read(id)));
app.MapGet("/v1/notes/{id:guid}/links", (LibraryStore library, Guid id) => library.Links(id));
app.MapGet("/v1/links/issues", (LibraryStore library, int? page) => library.LinkIssues(page ?? 0));
app.MapGet("/v1/notes/{id:guid}/wiki-export", (LibraryStore library, Guid id) => library.PreviewWikiExport(id));
app.MapPost("/v1/notes/{id:guid}/wiki-export", (LibraryStore library, Guid id, WikiExportRequest request) => library.ApplyWikiExport(id, request));
app.MapPost("/v1/notes/{id:guid}/link-repair/preview", (LibraryStore library, Guid id, LinkRepairRequest request) => library.PreviewLinkRepair(id, request));
app.MapPost("/v1/notes/{id:guid}/link-repair/apply", (LibraryStore library, Guid id, LinkRepairRequest request) => library.ApplyLinkRepair(id, request));
app.MapPost("/v1/assets", async (HttpRequest request, AssetStore assets, CancellationToken cancellationToken) =>
{
    if (!Guid.TryParse(request.Query["id"], out var id)) throw new ArgumentException("An asset ID is required.");
    var metadata = await assets.UploadAsync(id, request.Query["filename"], request.ContentType, request.Body,
        request.ContentLength, request.Headers["X-Slate-Sha256"], cancellationToken);
    return Results.Created($"/v1/assets/{metadata.Id:D}/metadata", metadata);
});
app.MapGet("/v1/assets/{id:guid}/metadata", (AssetStore assets, Guid id) => assets.ReadMetadata(id));
app.MapGet("/v1/assets", (LibraryStore library, int? page, bool? unreferenced) => library.ListAssets(page ?? 0, unreferenced ?? false));
app.MapGet("/v1/assets/{id:guid}/references", (LibraryStore library, Guid id, int? page) => library.AssetNotes(id, page ?? 0));
app.MapGet("/v1/assets/{id:guid}/thumbnail", async (AssetDerivatives derived, Guid id, CancellationToken token) => Results.File(await derived.ThumbnailAsync(id, token), "image/png"));
app.MapGet("/v1/assets/{id:guid}/text", (AssetDerivatives derived, Guid id) => derived.Read(id));
app.MapPost("/v1/assets/{id:guid}/extract", (AssetDerivatives derived, Guid id) => Results.Accepted($"/v1/assets/{id:D}/text", derived.Start(id)));
app.MapGet("/v1/assets/{id:guid}/cleanup", (LibraryStore library, Guid id) => library.PreviewAssetCleanup(id));
app.MapPost("/v1/assets/{id:guid}/cleanup", (LibraryStore library, Guid id, AssetCleanupRequest request) => library.CleanupAsset(id, request));
app.MapGet("/v1/assets/{id:guid}", (AssetStore assets, Guid id) =>
{
    var item = assets.Open(id);
    return Results.File(item.Path, item.Metadata.ContentType,
        item.Metadata.InlineImage ? null : item.Metadata.OriginalFilename, enableRangeProcessing: true);
});
app.MapPost("/v1/notes", (LibraryStore library, CreateNoteRequest request) =>
{
    var note = library.CreateNote(request);
    return Results.Created($"/v1/notes/{note.Id:D}", note);
});
app.MapPost("/v1/captures", (LibraryStore library, CaptureNoteRequest request) =>
{
    var note = library.Capture(request);
    return Results.Created($"/v1/notes/{note.Id:D}", note);
});
IResult UpdateNoteResult(HttpContext context, LibraryStore library, Guid id, UpdateNoteRequest request)
{
    var expected = context.Request.Headers.IfMatch.ToString();
    if (string.IsNullOrWhiteSpace(expected)) return Results.Problem("If-Match is required.", statusCode: 428);
    return Results.Ok(library.Update(id, request with { Revision = expected }));
}
app.MapPut("/v1/notes/{id:guid}", UpdateNoteResult);
app.MapPost("/v1/folders", (LibraryStore library, CreateFolderRequest request) => Results.Created("/v1/library?path=" + Uri.EscapeDataString(request.ParentPath), library.CreateFolder(request)));
app.MapGet("/v1/library/item", (LibraryStore library, string path) => library.Details(path));
app.MapPost("/v1/library/rename", (LibraryStore library, RenameItemRequest request) => library.Rename(request));
app.MapPost("/v1/library/move", (LibraryStore library, TransferItemRequest request) => library.Move(request));
app.MapPost("/v1/library/copy", (LibraryStore library, TransferItemRequest request) => library.Copy(request));
app.MapPost("/v1/library/bulk/preview", (LibraryStore library, BulkOperationRequest request) => library.PreviewBulk(request));
app.MapPost("/v1/library/bulk/apply", (LibraryStore library, BulkApplyRequest request) => library.ApplyBulk(request));
app.MapPost("/v1/offline/replay", (LibraryStore library, ReplayNoteRequest request) => library.Replay(request));
app.MapPost("/v1/sync/merge-preview", async (LibraryStore library, GitSyncService git, CancellationToken token) => await git.PreviewSafeMergeAsync(library, token));
app.MapPost("/v1/sync/merge/{id:guid}", async (LibraryStore library, GitSyncService git, Guid id, CancellationToken token) => await git.ApplySafeMergeAsync(library, id, token));
app.MapGet("/v1/history/deleted", async (LibraryStore library, GitSyncService git, CancellationToken token) => await git.RecoverableAsync(library, token));
app.MapGet("/v1/notes/{id:guid}/graph", (LinkIndex links, Guid id, int? depth, int? limit, string? folder, string? type) => links.Graph(id, depth ?? 1, limit ?? 40, folder, type));
app.MapGet("/v1/notes/{id:guid}/related", (LinkIndex links, Guid id) => links.Related(id));
app.MapGet("/v1/rediscovery/{view}", (LinkIndex links, string view, DateOnly today, int? page) => links.Rediscover(view, today, page ?? 0));
app.MapPost("/v1/notes/{id:guid}/restore", async (LibraryStore library, GitSyncService git, Guid id, RestoreNoteRequest request, CancellationToken token) => await git.RestoreAsync(library, id, request, token));
app.MapGet("/v1/library/bulk/{id:guid}", (LibraryStore library, Guid id) => library.BulkStatus(id));
app.MapPost("/v1/workflows/daily", (LibraryStore library, DailyNoteRequest request) => library.Daily(request));
app.MapPost("/v1/notes/{id:guid}/answer", (LibraryStore library, Guid id, AnswerQuestionRequest request) => library.AnswerQuestion(id, request));
app.MapGet("/v1/notes/{id:guid}/append-preview", (LibraryStore library, Guid id, Guid captureId) => library.PreviewCaptureAppend(id, captureId));
app.MapPost("/v1/notes/{id:guid}/append-capture", (LibraryStore library, Guid id, AppendCaptureRequest request) => library.AppendCapture(id, request));
app.MapPost("/v1/library/delete", (LibraryStore library, DeleteItemRequest request) => library.Delete(request));
app.MapPost("/v1/library/refresh", (LibraryStore library) => { library.Refresh(); return Results.NoContent(); });
app.MapGet("/v1/search", (LibraryStore library, string? q, int? page, int? pageSize) => library.Search(q ?? "", page ?? 0, pageSize ?? 20));
app.MapGet("/v1/views/{view}", (LibraryStore library, string view, int? page, int? pageSize) => library.SmartView(view, page ?? 0, pageSize ?? 20));
app.MapPost("/v1/search/rebuild", (LibraryStore library, SearchIndex search) => Results.Ok(new { indexed = search.Rebuild(library.AllNotes()) }));
app.MapPost("/v1/git/flush", async (GitSyncService git, LibraryStore library, CancellationToken cancellationToken) =>
{
    await git.FlushAsync(library, cancellationToken); return Results.Ok(git.Status);
});
app.MapPost("/v1/sync", async (GitSyncService git, LibraryStore library, CancellationToken cancellationToken) =>
    Results.Ok((await git.SynchronizeAsync(library, cancellationToken)).Status));
app.MapGet("/v1/notes/{id:guid}/history", async (Guid id, LibraryStore library, GitSyncService git, CancellationToken cancellationToken) =>
{
    await git.FlushAsync(library, cancellationToken);
    return Results.Ok(await git.HistoryAsync(id, library.Read(id).Path, cancellationToken));
});
app.MapGet("/v1/notes/{id:guid}/history/{commit}", async (Guid id, string commit, GitSyncService git, CancellationToken cancellationToken) =>
    Results.Ok(await git.HistoricalNoteAsync(id, commit, cancellationToken)));
app.Run();

public partial class Program;

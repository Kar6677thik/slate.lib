using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Slate.Lib.Core;

namespace Slate.Lib.Api;

public sealed class GitOptions
{
    public string RemoteName { get; set; } = "origin";
    public string Branch { get; set; } = "main";
    public string StatePath { get; set; } = "";
    public int CommitDebounceSeconds { get; set; } = 30;
    public int CommitMaximumSeconds { get; set; } = 120;
    public int SyncIntervalSeconds { get; set; } = 60;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public string AuthorName { get; set; } = "Slate";
    public string AuthorEmail { get; set; } = "slate@localhost";
}

public sealed record GitSynchronization(bool Imported, IReadOnlyList<string> ChangedPaths, GitSyncState Status);

public sealed partial class GitSyncService
{
    private readonly LibraryPaths paths;
    private readonly GitOptions options;
    private readonly ILogger<GitSyncService> logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock stateGate = new();
    private DateTimeOffset? firstPending;
    private DateTimeOffset? lastPending;
    private string? lastAcceptedRemoteHead;
    private GitSyncState status;

    public GitSyncService(LibraryPaths paths, IOptions<GitOptions> options, ILogger<GitSyncService> logger)
    {
        this.paths = paths;
        this.options = options.Value;
        this.logger = logger;
        if (IsRepository) RecoverPendingImport();
        lastAcceptedRemoteHead = LoadState();
        status = IsRepository
            ? new("LocalOnly", IsWorkingTreeDirty(), "Local Git history is available; no synchronization has run yet.")
            : new("NotInitialized", false, "Run --initialize-git to enable Library history.");
    }

    public bool IsRepository => Directory.Exists(Path.Combine(paths.Root, ".git"));
    internal string OperationStatePath => Path.Combine(options.StatePath, "operations");
    public GitSyncState Status { get { lock (stateGate) return status; } }

    public void MarkPending()
    {
        if (!IsRepository) return;
        lock (stateGate)
        {
            var now = DateTimeOffset.UtcNow;
            firstPending ??= now; lastPending = now;
            status = status with { State = "Pending", Pending = true, Detail = "Saved locally; Git commit pending." };
        }
    }

    public bool CommitIsDue(DateTimeOffset now)
    {
        lock (stateGate)
            return firstPending is { } first && lastPending is { } last &&
                (now - last >= TimeSpan.FromSeconds(Math.Max(1, options.CommitDebounceSeconds)) ||
                 now - first >= TimeSpan.FromSeconds(Math.Max(1, options.CommitMaximumSeconds)));
    }

    public async Task<bool> FlushAsync(LibraryStore library, CancellationToken cancellationToken = default)
    {
        if (!IsRepository) return false;
        await gate.WaitAsync(cancellationToken);
        try { return library.WithWriterLock(() => CommitPendingCoreAsync(cancellationToken).GetAwaiter().GetResult()); }
        finally { gate.Release(); }
    }

    public async Task<GitSynchronization> SynchronizeAsync(LibraryStore library, CancellationToken cancellationToken = default)
    {
        if (!IsRepository) return new(false, [], Status);
        await gate.WaitAsync(cancellationToken);
        try
        {
            SetStatus(new("Syncing", true, "Checking Git history and remote state."));
            library.WithWriterLock(() => CommitPendingCoreAsync(cancellationToken).GetAwaiter().GetResult());
            if (!await HasRemoteAsync(cancellationToken))
            {
                var local = await HeadAsync("HEAD", cancellationToken);
                var localOnly = new GitSyncState("LocalOnly", false, "Committed locally; no configured remote was found.", local);
                SetStatus(localOnly); return new(false, [], localOnly);
            }

            var advertised = await RunAsync(["ls-remote", "--heads", options.RemoteName, $"refs/heads/{options.Branch}"], cancellationToken, allowFailure: true);
            if (advertised.ExitCode != 0)
            {
                var unavailable = new GitSyncState("Error", true, "Remote synchronization failed; local files and commits are preserved.", await HeadAsync("HEAD", cancellationToken));
                SetStatus(unavailable); return new(false, [], unavailable);
            }
            if (string.IsNullOrWhiteSpace(advertised.StdOut))
            {
                var local = await HeadAsync("HEAD", cancellationToken);
                var push = await RunAsync(["push", "-u", options.RemoteName, $"HEAD:{options.Branch}"], cancellationToken, allowFailure: true);
                if (push.ExitCode == 0)
                {
                    SaveAcceptedRemote(local);
                    var initial = ConfirmSuccessfulRemoteState(library, local!, "Local history was published to the configured remote.");
                    return new(false, [], initial);
                }
                var initialFailure = new GitSyncState("Error", true, "Remote push failed; local history is preserved.", local);
                SetStatus(initialFailure); return new(false, [], initialFailure);
            }

            var fetch = await RunAsync(["fetch", "--no-tags", options.RemoteName, options.Branch], cancellationToken, allowFailure: true);
            if (fetch.ExitCode != 0)
            {
                var failed = new GitSyncState("Error", true, "Remote synchronization failed; local files and commits are preserved.", await HeadAsync("HEAD", cancellationToken));
                SetStatus(failed); return new(false, [], failed);
            }

            var remoteRef = $"refs/remotes/{options.RemoteName}/{options.Branch}";
            var remote = await HeadAsync(remoteRef, cancellationToken);
            if (remote is null) throw new InvalidOperationException("The fetched remote branch was not available.");

            if (lastAcceptedRemoteHead is { } accepted &&
                !await IsAncestorAsync(accepted, remote, cancellationToken))
            {
                var rewritten = new GitSyncState("Conflict", true, "The remote history was rewritten. Preserve both histories and choose a new baseline manually.", await HeadAsync("HEAD", cancellationToken), remote);
                SetStatus(rewritten); return new(false, [], rewritten);
            }

            await ValidateCandidateAsync(remote, cancellationToken);
            var decision = library.WithWriterLock(() =>
            {
                var value = SynchronizeUnderWriterLockAsync(remoteRef, remote, cancellationToken).GetAwaiter().GetResult();
                if (value.Imported) library.RefreshAfterGitImport(value.ChangedPaths);
                return value;
            });
            if (decision.Imported)
            {
                if (decision.NormalizedIds) MarkPending();
                SaveAcceptedRemote(remote);
                var imported = ConfirmSuccessfulRemoteState(library, remote, "Remote changes imported and indexed.");
                return new(true, decision.ChangedPaths, imported);
            }

            if (decision.Relationship == "Ahead")
            {
                var push = await RunAsync(["push", options.RemoteName, $"{decision.LocalHead}:{options.Branch}"], cancellationToken, allowFailure: true);
                if (push.ExitCode == 0)
                {
                    SaveAcceptedRemote(decision.LocalHead);
                    var pushed = ConfirmSuccessfulRemoteState(library, decision.LocalHead, "Local history is synchronized.");
                    return new(false, [], pushed);
                }
                var pushFailure = new GitSyncState("Error", true, "Remote push failed; local files and commits are preserved.", decision.LocalHead, remote);
                SetStatus(pushFailure); return new(false, [], pushFailure);
            }
            if (decision.Relationship == "Equal")
            {
                SaveAcceptedRemote(remote);
                var equal = ConfirmSuccessfulRemoteState(library, remote, "Local and remote history are synchronized.");
                return new(false, [], equal);
            }

            var conflict = new GitSyncState("Conflict", true, "Git histories diverged. Resolve them in a separate clone and push a commit containing both heads.", decision.LocalHead, remote);
            SetStatus(conflict); return new(false, [], conflict);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("Git synchronization failed: {Reason}", exception.Message);
            var failed = new GitSyncState("Error", true, "Git synchronization failed; local files and commits are preserved. " + exception.Message);
            SetStatus(failed); return new(false, [], failed);
        }
        finally { gate.Release(); }
    }

    private async Task<SyncDecision> SynchronizeUnderWriterLockAsync(string remoteRef, string remote, CancellationToken cancellationToken)
    {
        await CommitPendingCoreAsync(cancellationToken);
        var local = await HeadAsync("HEAD", cancellationToken) ?? throw new InvalidOperationException("The Library repository has no local commit.");
        if (local == remote) return new("Equal", false, false, [], local);
        if (await IsAncestorAsync(remote, local, cancellationToken)) return new("Ahead", false, false, [], local);
        if (!await IsAncestorAsync(local, remote, cancellationToken)) return new("Diverged", false, false, [], local);

        var changed = await ChangedMarkdownPathsAsync(local, remote, cancellationToken);
        await RunRequiredAsync(["update-ref", "refs/slate/import-safety", local], cancellationToken);
        WritePendingImport(new(local, remote));
        await RunRequiredAsync(["merge", "--ff-only", remoteRef], cancellationToken);
        var normalized = LibrarySetup.Initialize(paths) > 0;
        CompletePendingImport();
        logger.LogInformation("Remote Git fast-forward imported {Count} changed Markdown paths.", changed.Count);
        return new("Imported", true, normalized, changed, await HeadAsync("HEAD", cancellationToken) ?? remote);
    }

    public async Task<IReadOnlyList<NoteHistoryEntry>> HistoryAsync(Guid id, string currentPath, CancellationToken cancellationToken = default)
    {
        if (!IsRepository) return [];
        await gate.WaitAsync(cancellationToken);
        try
        {
            var output = await RunRequiredAsync(["log", "--follow", "-100", "--format=%H%x1f%cI%x1f%s%x1f%an", "--", currentPath], cancellationToken);
            return output.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('\x1f')).Where(parts => parts.Length == 4)
                .Select(parts => new NoteHistoryEntry(parts[0], DateTimeOffset.Parse(parts[1]), parts[2], parts[3])).ToArray();
        }
        finally { gate.Release(); }
    }

    public async Task<HistoricalNote> HistoricalNoteAsync(Guid id, string commit, CancellationToken cancellationToken = default)
    {
        if (!Regex.IsMatch(commit, "^[0-9a-fA-F]{40,64}$")) throw new ArgumentException("Invalid commit identifier.");
        if (!IsRepository) throw new InvalidOperationException("Git history is not initialized.");
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!await IsAncestorAsync(commit, "HEAD", cancellationToken)) throw new ArgumentException("Commit is not in the current Library history.");
            var pathsAtCommit = (await RunRequiredAsync(["ls-tree", "-r", "-z", "--name-only", commit], cancellationToken)).StdOut
                .Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
            foreach (var relative in pathsAtCommit)
            {
                paths.Resolve(relative);
                var source = (await RunRequiredAsync(["show", $"{commit}:{relative}"], cancellationToken)).StdOut;
                if (System.Text.Encoding.UTF8.GetByteCount(source) > NoteDocument.MaxBytes) continue;
                var parsed = NoteDocument.Parse(source, relative, true);
                if (parsed.Id != id) continue;
                var info = (await RunRequiredAsync(["show", "-s", "--format=%cI%x1f%s", commit], cancellationToken)).StdOut.Trim().Split('\x1f', 2);
                return new(id, commit, relative, parsed.Title, source, DateTimeOffset.Parse(info[0]), info.ElementAtOrDefault(1) ?? "");
            }
            throw new FileNotFoundException("The note did not exist at that commit.");
        }
        finally { gate.Release(); }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (IsRepository) throw new InvalidOperationException("The Library already has a Git repository.");
            await RunRequiredAsync(["init", "-b", options.Branch], cancellationToken, allowOutsideRepository: true);
            await RunRequiredAsync(["config", "user.name", options.AuthorName], cancellationToken);
            await RunRequiredAsync(["config", "user.email", options.AuthorEmail], cancellationToken);
            await StageManagedAsync(cancellationToken);
            await RunRequiredAsync(["commit", "-m", "Initialize Slate Library"], cancellationToken);
            SetStatus(new("LocalOnly", false, "Local Git history initialized.", await HeadAsync("HEAD", cancellationToken)));
        }
        finally { gate.Release(); }
    }

    private async Task<bool> CommitPendingCoreAsync(CancellationToken cancellationToken)
    {
        if (!IsRepository) return false;
        await StageManagedAsync(cancellationToken);
        var staged = await RunAsync(["diff", "--cached", "--quiet"], cancellationToken, allowFailure: true);
        if (staged.ExitCode == 0)
        {
            lock (stateGate) { firstPending = lastPending = null; }
            return false;
        }
        await RunRequiredAsync(["commit", "-m", "Update Slate Library"], cancellationToken);
        var head = await HeadAsync("HEAD", cancellationToken);
        lock (stateGate)
        {
            firstPending = lastPending = null;
            status = new("Committed", true, "Committed locally; remote synchronization pending.", head, status.RemoteHead);
        }
        logger.LogInformation("Git batch committed at {Head}.", head);
        return true;
    }

    private async Task StageManagedAsync(CancellationToken cancellationToken)
    {
        var tracked = (await RunAsync(["ls-files", "-z"], cancellationToken, allowFailure: true)).StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(IsManagedPath).ToArray();
        foreach (var group in tracked.Chunk(200))
        {
            var update = new List<string> { "add", "-u", "--" }; update.AddRange(group);
            await RunRequiredAsync(update, cancellationToken);
        }
        var managed = new List<string> { ".slate/library.json" };
        managed.AddRange(LibraryStore.Discover(paths));
        managed.AddRange(DiscoverMarkers(paths.Root));
        foreach (var group in managed.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(200))
        {
            var arguments = new List<string> { "add", "--" }; arguments.AddRange(group);
            await RunRequiredAsync(arguments, cancellationToken);
        }
    }

    private bool IsManagedPath(string relative)
    {
        if (relative == ".slate/library.json") return true;
        if (Path.GetFileName(relative) == ".gitkeep")
        {
            var parent = relative.Contains('/') ? relative[..relative.LastIndexOf('/')] : "";
            try { if (parent.Length > 0) paths.Resolve(parent); return true; } catch (ArgumentException) { return false; }
        }
        if (!relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return false;
        try { paths.Resolve(relative); return true; } catch (ArgumentException) { return false; }
    }

    private static IEnumerable<string> DiscoverMarkers(string root, string relative = "")
    {
        var directory = relative.Length == 0 ? root : Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(child); if (name.StartsWith('.')) continue;
            var next = relative.Length == 0 ? name : relative + "/" + name;
            foreach (var marker in DiscoverMarkers(root, next)) yield return marker;
        }
        if (File.Exists(Path.Combine(directory, ".gitkeep"))) yield return relative.Length == 0 ? ".gitkeep" : relative + "/.gitkeep";
    }

    private async Task ValidateCandidateAsync(string commit, CancellationToken cancellationToken)
    {
        var tree = await RunRequiredAsync(["ls-tree", "-r", "-z", commit], cancellationToken);
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var casing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var currentIdentity = JsonSerializer.Deserialize<LibraryIdentity>(File.ReadAllText(Path.Combine(paths.Root, ".slate", "library.json")), json);
        var sawIdentity = false;
        foreach (var line in tree.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = line.IndexOf('\t');
            if (tab < 0) throw new InvalidDataException("Invalid Git tree entry.");
            var metadata = line[..tab].Split(' ');
            var relative = line[(tab + 1)..];
            if (relative.Length == 0 || relative.Contains('\\') || Path.IsPathRooted(relative) || relative.Any(char.IsControl))
                throw new InvalidDataException("Git import contains an unsafe path.");
            var prefix = "";
            foreach (var segment in relative.Split('/'))
            {
                prefix = prefix.Length == 0 ? segment : prefix + "/" + segment;
                if (casing.TryGetValue(prefix, out var prior) && prior != prefix)
                    throw new InvalidDataException("Git import contains case-colliding paths.");
                casing[prefix] = prefix;
            }
            if (metadata.Length < 3 || metadata[0] != "100644" || metadata[1] != "blob")
                throw new InvalidDataException("Git imports may contain only ordinary non-executable files.");
            if (!names.Add(relative)) throw new InvalidDataException("Git import contains case-colliding paths.");
            var allowedMetadata = relative is ".slate/library.json" or ".gitignore" or ".gitattributes" || Path.GetFileName(relative) == ".gitkeep";
            if (!relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !allowedMetadata)
                throw new InvalidDataException("Git import contains an unsupported file.");
            if (!relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                if (relative == ".slate/library.json")
                {
                    var identitySource = (await RunRequiredAsync(["show", $"{commit}:{relative}"], cancellationToken)).StdOut;
                    var identity = JsonSerializer.Deserialize<LibraryIdentity>(identitySource, json);
                    if (identity is null || currentIdentity is null || identity.SchemaVersion != 1 || identity.LibraryId != currentIdentity.LibraryId)
                        throw new InvalidDataException("Git import belongs to a different or invalid Slate Library.");
                    sawIdentity = true;
                }
                if (relative == ".gitattributes")
                {
                    var attributes = (await RunRequiredAsync(["show", $"{commit}:{relative}"], cancellationToken)).StdOut;
                    if (attributes.Contains("filter", StringComparison.OrdinalIgnoreCase) || attributes.Contains("working-tree-encoding", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Git import contains unsupported working-tree filters.");
                }
                if (Path.GetFileName(relative) == ".gitkeep")
                {
                    var parent = relative.Contains('/') ? relative[..relative.LastIndexOf('/')] : "";
                    if (parent.Length > 0) paths.Resolve(parent);
                }
                continue;
            }
            try { paths.Resolve(relative); }
            catch (ArgumentException exception) { throw new InvalidDataException($"Git import contains an invalid Markdown path: {relative}", exception); }
            var size = long.Parse((await RunRequiredAsync(["cat-file", "-s", metadata[2]], cancellationToken)).StdOut.Trim());
            if (size > NoteDocument.MaxBytes) throw new InvalidDataException("Git import contains an oversized note.");
            var source = (await RunRequiredAsync(["show", $"{commit}:{relative}"], cancellationToken)).StdOut;
            var note = NoteDocument.Parse(source, relative, true);
            if (note.Id is { } id && !ids.Add(id)) throw new InvalidDataException("Git import contains duplicate note IDs.");
        }
        if (!sawIdentity) throw new InvalidDataException("Git import is missing .slate/library.json.");
    }

    private async Task<IReadOnlyList<string>> ChangedMarkdownPathsAsync(string oldHead, string newHead, CancellationToken cancellationToken)
    {
        var output = await RunRequiredAsync(["diff", "--name-status", "-z", "-M", oldHead, newHead, "--", "*.md"], cancellationToken);
        var parts = output.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var changed = new List<string>();
        for (var index = 0; index < parts.Length;)
        {
            var status = parts[index++];
            if (index < parts.Length) changed.Add(parts[index++]);
            if (status.StartsWith('R') && index < parts.Length) changed.Add(parts[index++]);
        }
        return changed.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<bool> HasRemoteAsync(CancellationToken cancellationToken) =>
        (await RunAsync(["remote", "get-url", options.RemoteName], cancellationToken, allowFailure: true)).ExitCode == 0;

    private async Task<string?> HeadAsync(string revision, CancellationToken cancellationToken)
    {
        var result = await RunAsync(["rev-parse", "--verify", revision], cancellationToken, allowFailure: true);
        return result.ExitCode == 0 ? result.StdOut.Trim() : null;
    }

    private async Task<bool> IsAncestorAsync(string ancestor, string descendant, CancellationToken cancellationToken) =>
        (await RunAsync(["merge-base", "--is-ancestor", ancestor, descendant], cancellationToken, allowFailure: true)).ExitCode == 0;

    private bool IsWorkingTreeDirty()
    {
        if (!IsRepository) return false;
        try
        {
            var unstaged = RunAsync(["diff", "--quiet"], default, allowFailure: true).GetAwaiter().GetResult().ExitCode != 0;
            var staged = RunAsync(["diff", "--cached", "--quiet"], default, allowFailure: true).GetAwaiter().GetResult().ExitCode != 0;
            return unstaged || staged;
        }
        catch { return true; }
    }

    private Task<GitResult> RunRequiredAsync(IReadOnlyList<string> args, CancellationToken cancellationToken, bool allowOutsideRepository = false) =>
        RunAsync(args, cancellationToken, false, allowOutsideRepository);

    private async Task<GitResult> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken, bool allowFailure, bool allowOutsideRepository = false)
    {
        if (!allowOutsideRepository && !IsRepository) throw new InvalidOperationException("Library Git repository is not initialized.");
        var hooks = Path.Combine(string.IsNullOrWhiteSpace(options.StatePath) ? Path.Combine(paths.Root, ".slate") : options.StatePath, "disabled-hooks");
        Directory.CreateDirectory(hooks);
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = paths.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.ArgumentList.Add("-c"); start.ArgumentList.Add($"core.hooksPath={hooks}");
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git could not be started.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, options.CommandTimeoutSeconds)));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var result = new GitResult(process.ExitCode, await stdout, await stderr);
        if (!allowFailure && result.ExitCode != 0) throw new InvalidOperationException($"Git command '{args[0]}' failed.");
        return result;
    }

    private string? LoadState()
    {
        try
        {
            var file = StateFile();
            return File.Exists(file) ? JsonSerializer.Deserialize<GitStateFile>(File.ReadAllText(file))?.LastAcceptedRemoteHead : null;
        }
        catch { return null; }
    }

    private void SaveAcceptedRemote(string? head)
    {
        if (head is null) return;
        lastAcceptedRemoteHead = head;
        try
        {
            var file = StateFile(); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temp = file + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temp, JsonSerializer.Serialize(new GitStateFile(head)));
            File.Move(temp, file, true);
        }
        catch (Exception exception) { logger.LogWarning("Git sync state could not be persisted: {Reason}", exception.Message); }
    }

    private string StateFile() => Path.Combine(string.IsNullOrWhiteSpace(options.StatePath) ? Path.Combine(paths.Root, ".slate") : options.StatePath, "git-sync.json");
    private string PendingImportFile() => Path.Combine(string.IsNullOrWhiteSpace(options.StatePath) ? Path.Combine(paths.Root, ".slate") : options.StatePath, "pending", "git-import.json");

    private void WritePendingImport(PendingImport value)
    {
        var file = PendingImportFile(); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + ".tmp-" + Guid.NewGuid().ToString("N");
        var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
        File.Move(temp, file, true);
    }

    private void CompletePendingImport()
    {
        var file = PendingImportFile(); if (File.Exists(file)) File.Delete(file);
    }

    private void RecoverPendingImport()
    {
        var file = PendingImportFile(); if (!File.Exists(file)) return;
        var pending = JsonSerializer.Deserialize<PendingImport>(File.ReadAllText(file)) ?? throw new InvalidDataException("Invalid pending Git import record.");
        var head = HeadAsync("HEAD", default).GetAwaiter().GetResult();
        if (head == pending.NewHead)
        {
            LibrarySetup.Initialize(paths);
            CompletePendingImport();
            logger.LogWarning("Completed recovery of an interrupted Git fast-forward to {Head}.", head);
            return;
        }
        if (head == pending.OldHead)
        {
            CompletePendingImport();
            logger.LogWarning("Cleared a Git import that stopped before activation.");
            return;
        }
        throw new InvalidOperationException("Git import recovery requires operator attention; HEAD matches neither recorded state.");
    }

    private GitSyncState ConfirmSuccessfulRemoteState(LibraryStore library, string remoteHead, string detail)
    {
        return library.WithWriterLock(() =>
        {
            var local = HeadAsync("HEAD", default).GetAwaiter().GetResult();
            bool pending;
            lock (stateGate) pending = firstPending is not null;
            pending = pending || IsWorkingTreeDirty() || local != remoteHead;
            var value = pending
                ? new GitSyncState("Pending", true, "Newer local work remains pending after the last successful remote operation.", local, remoteHead)
                : new GitSyncState("Synced", false, detail, local, remoteHead);
            SetStatus(value);
            return value;
        });
    }
    private void SetStatus(GitSyncState value) { lock (stateGate) status = value; }

    private sealed record GitResult(int ExitCode, string StdOut, string StdErr);
    private sealed record GitStateFile(string LastAcceptedRemoteHead);
    private sealed record PendingImport(string OldHead, string NewHead);
    private sealed record SyncDecision(string Relationship, bool Imported, bool NormalizedIds, IReadOnlyList<string> ChangedPaths, string LocalHead);
}

public sealed class GitBackgroundService(GitSyncService git, LibraryStore library, ILogger<GitBackgroundService> logger, IOptions<GitOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.SyncIntervalSeconds));
        var nextSync = DateTimeOffset.UtcNow + interval;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                if (git.CommitIsDue(DateTimeOffset.UtcNow) && await git.FlushAsync(library, stoppingToken))
                {
                    await git.SynchronizeAsync(library, stoppingToken);
                    nextSync = DateTimeOffset.UtcNow + interval;
                }
                if (DateTimeOffset.UtcNow >= nextSync)
                {
                    await git.SynchronizeAsync(library, stoppingToken);
                    nextSync = DateTimeOffset.UtcNow + interval;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning("Background Git work failed: {Reason}", exception.Message); }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try { await git.FlushAsync(library, cancellationToken); } catch (Exception exception) { logger.LogWarning("Git flush during shutdown failed: {Reason}", exception.Message); }
        await base.StopAsync(cancellationToken);
    }
}

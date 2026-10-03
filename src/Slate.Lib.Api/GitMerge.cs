using System.Text.Json;
using Slate.Lib.Core;

namespace Slate.Lib.Api;

public sealed partial class GitSyncService
{
    private bool HasUntrackedManagedFiles(CancellationToken token) => RunRequiredAsync(["ls-files", "--others", "--exclude-standard", "-z"], token).GetAwaiter().GetResult().StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries).Any(IsManagedPath);
    public async Task<SafeGitMergePreview> PreviewSafeMergeAsync(LibraryStore library, CancellationToken token = default)
    {
        // Normal sync fetches and validates the remote but deliberately leaves divergent heads untouched.
        var sync = await SynchronizeAsync(library, token);
        if (sync.Status.State != "Conflict") throw new LibraryConflictException("There is no divergent history to merge.");
        await gate.WaitAsync(token);
        try
        {
            return library.WithWriterLock(() => PreviewMergeCoreAsync(library.LibraryVersion, token).GetAwaiter().GetResult());
        }
        finally { gate.Release(); }
    }
    private async Task<SafeGitMergePreview> PreviewMergeCoreAsync(string libraryVersion, CancellationToken token)
    {
        if (IsWorkingTreeDirty() || HasUntrackedManagedFiles(token)) throw new LibraryConflictException("Finish saving pending changes, then review synchronization again.");
        var local = await HeadAsync("HEAD", token) ?? throw new InvalidOperationException("Local history is unavailable.");
        var remote = await HeadAsync($"refs/remotes/{options.RemoteName}/{options.Branch}", token) ?? throw new InvalidOperationException("Fetched history is unavailable.");
        if (lastAcceptedRemoteHead is { } accepted && !await IsAncestorAsync(accepted, remote, token)) throw new LibraryConflictException("Remote history was rewritten; preserve both histories and use the recovery runbook.");
        var basis = (await RunAsync(["merge-base", local, remote], token, allowFailure: true)).StdOut.Trim();
        if (basis.Length == 0) throw new LibraryConflictException("These histories have no common baseline. Manual recovery is required.");
        var left = (await RunRequiredAsync(["diff", "--name-only", "-z", basis, local], token)).StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var right = (await RunRequiredAsync(["diff", "--name-only", "-z", basis, remote], token)).StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (left.Intersect(right, StringComparer.OrdinalIgnoreCase).Any()) throw new LibraryConflictException("Both histories changed the same file. Preserve both histories and resolve in a separate clone; Slate will not guess.");
        var tree = await RunAsync(["merge-tree", "--write-tree", local, remote], token, allowFailure: true);
        if (tree.ExitCode != 0) throw new LibraryConflictException("The histories cannot be combined without conflicts. Both heads were preserved.");
        var treeId = tree.StdOut.Split('\n')[0].Trim();
        var candidate = (await RunRequiredAsync(["commit-tree", treeId, "-p", local, "-p", remote, "-m", "Combine reviewed independent Slate changes"], token)).StdOut.Trim();
        await ValidateCandidateAsync(candidate, token);
        var preview = new SafeGitMergePreview(Guid.NewGuid(), local, remote, candidate, right, libraryVersion);
        await AtomicJson.WriteAsync(Path.Combine(OperationStatePath, "merge", preview.Id + ".json"), preview, new(JsonSerializerDefaults.Web), token);
        return preview;
    }
    public async Task<GitSyncState> ApplySafeMergeAsync(LibraryStore library, Guid id, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var preview = JsonSerializer.Deserialize<SafeGitMergePreview>(await File.ReadAllTextAsync(Path.Combine(OperationStatePath, "merge", id + ".json"), token), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Invalid merge preview.");
            var advertised = (await RunRequiredAsync(["ls-remote", "--heads", options.RemoteName, $"refs/heads/{options.Branch}"], token)).StdOut.Split('\t')[0].Trim();
            if (advertised != preview.RemoteHead) throw new LibraryPreconditionException("Remote history changed. Review again.");
            return library.WithWriterLock(() =>
            {
                if (library.LibraryVersion != preview.LibraryVersion || IsWorkingTreeDirty() || HasUntrackedManagedFiles(token) || HeadAsync("HEAD", token).GetAwaiter().GetResult() != preview.LocalHead) throw new LibraryPreconditionException("Local history changed. Review again.");
                ValidateCandidateAsync(preview.Candidate, token).GetAwaiter().GetResult();
                RunRequiredAsync(["update-ref", "refs/slate/import-safety", preview.LocalHead], token).GetAwaiter().GetResult();
                WritePendingImport(new(preview.LocalHead, preview.Candidate));
                RunRequiredAsync(["merge", "--ff-only", preview.Candidate], token).GetAwaiter().GetResult();
                var normalized = LibrarySetup.Initialize(paths) > 0;
                library.RefreshAfterGitImport(preview.RemotePaths); CompletePendingImport();
                SaveAcceptedRemote(preview.RemoteHead); if (normalized) MarkPending();
                var merged = new GitSyncState("Pending", true, "Independent changes combined in a new commit. Synchronize to publish normally.", preview.Candidate, preview.RemoteHead);
                SetStatus(merged); return merged;
            });
        }
        finally { gate.Release(); }
    }
}

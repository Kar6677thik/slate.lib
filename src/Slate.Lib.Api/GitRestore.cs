using Slate.Lib.Core;

namespace Slate.Lib.Api;

public sealed partial class LibraryStore
{
    public bool ContainsNote(Guid id) { lock (gate) return notes.ContainsKey(id); }
}

public sealed partial class GitSyncService
{
    public async Task<LibraryNote> RestoreAsync(LibraryStore library, Guid id, RestoreNoteRequest request, CancellationToken token = default)
    {
        if (request.Mode is not ("current" or "new" or "recover")) throw new ArgumentException("Choose restore, restore as new, or deleted-note recovery.");
        var historical = await HistoricalNoteAsync(id, request.Commit, token);
        await gate.WaitAsync(token);
        try
        {
            return library.WithWriterLock(() =>
            {
                if (!IsAncestorAsync(request.Commit, "HEAD", token).GetAwaiter().GetResult()) throw new LibraryPreconditionException("History changed. Preview again.");
                LibraryNote result;
                if (request.Mode == "current")
                {
                    var current = library.Read(id);
                    var source = PortableNoteLinks.Rewrite(historical.Markdown, historical.Path, current.Path, new Dictionary<string, string> { [historical.Path] = current.Path });
                    result = library.Update(id, new(source, request.Revision ?? ""));
                }
                else
                {
                    if (request.Mode == "recover" && library.ContainsNote(id)) throw new LibraryConflictException("This note already exists. Open it instead or restore as a new note.");
                    if (string.IsNullOrWhiteSpace(request.Name) || request.Folder is null) throw new ArgumentException("Choose a destination folder and filename.");
                    var identity = request.Mode == "new" ? Guid.NewGuid() : id;
                    var filename = request.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? request.Name : request.Name + ".md";
                    var destination = request.Folder.Length == 0 ? filename : request.Folder + "/" + filename;
                    var source = PortableNoteLinks.Rewrite(historical.Markdown, historical.Path, destination, new Dictionary<string, string> { [historical.Path] = destination });
                    if (request.Mode == "new") source = NoteDocument.ReplaceId(source, identity);
                    result = library.CreateNote(new(request.Folder, request.Name, historical.Title, identity, source));
                }
                CommitPendingCoreAsync(token).GetAwaiter().GetResult(); // Ordinary new commit; no reset, checkout of old HEAD, or history rewrite.
                return result;
            });
        }
        finally { gate.Release(); }
    }
    public async Task<RecoveryPage> RecoverableAsync(LibraryStore library, CancellationToken token = default)
    {
        if (!IsRepository) return new([], false);
        await gate.WaitAsync(token);
        try
        {
            var commits = (await RunRequiredAsync(["log", "--diff-filter=D", "--format=%H", "-100", "HEAD", "--", "*.md"], token)).StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var result = new List<RecoverableNote>(); var seen = new HashSet<Guid>(); var scanned = 0;
            foreach (var commitValue in commits)
            {
                var commit = commitValue.Trim();
                var parent = await HeadAsync(commit + "^", token); if (parent is null) continue;
                var deleted = (await RunRequiredAsync(["diff", "--name-only", "--diff-filter=D", "-z", parent, commit, "--", "*.md"], token)).StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries);
                var timestamp = DateTimeOffset.Parse((await RunRequiredAsync(["show", "-s", "--format=%cI", commit], token)).StdOut.Trim());
                foreach (var path in deleted)
                {
                    if (++scanned > 200) return new(result, true);
                    paths.Resolve(path);
                    var spec = $"{parent}:{path}";
                    var size = long.Parse((await RunRequiredAsync(["cat-file", "-s", spec], token)).StdOut.Trim()); if (size > NoteDocument.MaxBytes) continue;
                    var source = (await RunRequiredAsync(["show", spec], token)).StdOut;
                    NoteDocument note; try { note = NoteDocument.Parse(source, path, true); } catch (InvalidDataException) { continue; }
                    if (note.Id is not { } id || !seen.Add(id) || library.ContainsNote(id)) continue;
                    result.Add(new(id, path, note.Title, parent, commit, timestamp));
                }
            }
            return new(result, commits.Length >= 100);
        }
        finally { gate.Release(); }
    }
}

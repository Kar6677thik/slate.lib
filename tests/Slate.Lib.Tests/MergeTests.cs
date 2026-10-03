using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Slate.Lib.Api;
using Slate.Lib.Core;
using Xunit;

namespace Slate.Lib.Tests;

public sealed class MergeTests
{
    [Fact]
    public void NonoverlappingChangesKeepBothSidesAndExactLineBreaks()
    {
        var result = TextMerge.Merge("one\ntwo\nthree\n", "ONE\ntwo\nthree\n", "one\ntwo\nTHREE\n");
        Assert.False(result.HasConflicts); Assert.Equal("ONE\ntwo\nTHREE\n", result.Proposed);
        Assert.Equal("end", TextMerge.Merge("end", "end", "end").Proposed);
        Assert.Equal("", TextMerge.Merge("", "", "").Proposed);
    }
    [Fact]
    public void SameLineAndSamePositionInsertionsRemainExplicitConflicts()
    {
        var merge = TextMerge.Merge("first\nlast\n", "local\nlast\n", "current\nlast\n");
        var conflict = Assert.Single(merge.Blocks, x => x.Conflict); Assert.Equal("first\n", conflict.Base); Assert.Equal("local\n", conflict.Local); Assert.Equal("current\n", conflict.Current);
        Assert.True(TextMerge.Merge("last\n", "A\nlast\n", "B\nlast\n").HasConflicts);
        Assert.False(TextMerge.Merge("last\n", "A\nlast\n", "A\nlast\n").HasConflicts);
    }
    [Fact]
    public void DeleteVersusEditAndLargeChangesNeverSilentlyPickOneSide()
    {
        Assert.True(TextMerge.Merge("a\nb\nc\n", "a\nc\n", "a\nB\nc\n").HasConflicts);
        var basis = string.Concat(Enumerable.Range(0, 1800).Select(x => x + "\n"));
        var left = string.Concat(Enumerable.Range(0, 1800).Select(x => "L" + x + "\n"));
        Assert.True(TextMerge.Merge(basis, left, basis.Replace("900\n", "changed\n")).HasConflicts);
        Assert.Contains("− b", TextMerge.VisualDiff("a\nb\n", "a\nB\n")); Assert.Contains("+ B", TextMerge.VisualDiff("a\nb\n", "a\nB\n"));
    }
    [Fact]
    public void DiffReconstructsRandomSmallDocuments()
    {
        var random = new Random(711);
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var before = Enumerable.Range(0, random.Next(15)).Select(_ => random.Next(5) + "\n").ToArray();
            var after = Enumerable.Range(0, random.Next(15)).Select(_ => random.Next(5) + "\n").ToArray(); var reconstructed = before.ToList();
            foreach (var edit in TextMerge.Diff(string.Concat(before), string.Concat(after)).Reverse()) { reconstructed.RemoveRange(edit.Start, edit.Count); reconstructed.InsertRange(edit.Start, edit.Replacement); }
            Assert.Equal(string.Concat(after), string.Concat(reconstructed));
            Assert.Equal(string.Concat(after), TextMerge.Merge(string.Concat(before), string.Concat(after), string.Concat(before)).Proposed);
        }
    }
    private static string Git(string directory, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEnd(); var error = process.StandardError.ReadToEnd(); process.WaitForExit(); Assert.True(process.ExitCode == 0, error); return output.Trim();
    }
    [Fact]
    public async Task ControlledMergePreservesParentsAndRejectsStalePreview()
    {
        using var f = new TestLibrary(); Directory.CreateDirectory(f.DerivedRoot);
        var git = new GitSyncService(new(f.Root), Options.Create(new GitOptions { StatePath = Path.Combine(f.DerivedRoot, "state") }), NullLogger<GitSyncService>.Instance);
        await git.InitializeAsync(); var library = new LibraryStore(new(f.Root), null, git);
        var bare = Path.Combine(f.DerivedRoot, "remote.git"); var clone = Path.Combine(f.DerivedRoot, "clone");
        Git(f.DerivedRoot, "init", "--bare", "-b", "main", bare); Git(f.Root, "remote", "add", "origin", bare); await git.SynchronizeAsync(library);
        Git(f.DerivedRoot, "clone", bare, clone); Git(clone, "config", "user.name", "Test"); Git(clone, "config", "user.email", "test@example.invalid");
        File.AppendAllText(Path.Combine(clone, "Science/Databases/Note.md"), "\nremote addition\n"); Git(clone, "add", "-A"); Git(clone, "commit", "-m", "Remote"); Git(clone, "push");
        library.CreateNote(new("Empty", "Local", InitialMarkdown: "local separate note")); await git.FlushAsync(library);
        var preview = await git.PreviewSafeMergeAsync(library); var before = Git(f.Root, "rev-parse", "HEAD"); Assert.Equal(preview.LocalHead, before);
        library.CreateNote(new("Empty", "Another")); await Assert.ThrowsAsync<LibraryPreconditionException>(() => git.ApplySafeMergeAsync(library, preview.Id));
        await git.FlushAsync(library); preview = await git.PreviewSafeMergeAsync(library); await git.ApplySafeMergeAsync(library, preview.Id);
        var parents = Git(f.Root, "show", "-s", "--format=%P", "HEAD"); Assert.Contains(preview.LocalHead, parents); Assert.Contains(preview.RemoteHead, parents);
        Assert.Contains("remote addition", library.Read(f.NoteId).Markdown); Assert.Equal(2, library.List("Empty", 0).Entries.Count);
    }
}

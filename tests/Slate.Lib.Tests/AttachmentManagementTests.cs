using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Slate.Lib.Api;
using Slate.Lib.Core;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Core;
using Xunit;

namespace Slate.Lib.Tests;

public sealed class AttachmentManagementTests
{
    private static AssetStore Assets(TestLibrary f) => new(Options.Create(new AssetOptions { RootPath = Path.Combine(f.DerivedRoot, "assets") }));
    private static Task<AssetMetadata> Upload(AssetStore assets, byte[] bytes, string filename = "sample.png") => assets.UploadAsync(Guid.NewGuid(), filename, null, new MemoryStream(bytes), bytes.Length, null, default);
    private static byte[] Image()
    {
        using var bitmap = new SKBitmap(640, 320); bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 90); return data.ToArray();
    }
    private static void Age(TestLibrary f, AssetMetadata metadata) => File.WriteAllText(Path.Combine(f.DerivedRoot, "assets", "metadata", metadata.Id + ".json"), JsonSerializer.Serialize(metadata with { CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    [Fact]
    public async Task ReferenceCatalogTracksReferenceStyleLinksMovesAndRemovedNotes()
    {
        using var f = new TestLibrary(); var assets = Assets(f); var asset = await Upload(assets, Image());
        var library = new LibraryStore(new(f.Root), null, null, new(), assets);
        var note = library.CreateNote(new("Empty", "Reference", InitialMarkdown: $"![photo][image]\n\n[image]: ../.assets/{asset.Id}.png"));
        Assert.Equal(1, Assert.Single(library.ListAssets(0, false).Results).ReferenceCount);
        Assert.Equal(note.Id, Assert.Single(library.AssetNotes(asset.Id, 0).Results).NoteId);
        library.Move(new(note.Path, "Science"));
        Assert.Equal("Science/Reference.md", Assert.Single(library.AssetNotes(asset.Id, 0).Results).Path);
        library.Delete(new("Science/Reference.md", true));
        Assert.Equal(0, Assert.Single(library.ListAssets(0, true).Results).ReferenceCount);
    }
    [Fact]
    public async Task CleanupRejectsYoungReferencedStaleAndUnparseableLibraries()
    {
        using var f = new TestLibrary(); var assets = Assets(f); var asset = await Upload(assets, Image());
        var library = new LibraryStore(new(f.Root), null, null, null, assets);
        Assert.Throws<LibraryConflictException>(() => library.PreviewAssetCleanup(asset.Id));
        Age(f, asset); var preview = library.PreviewAssetCleanup(asset.Id);
        library.CreateNote(new("Empty", "Another"));
        Assert.Throws<LibraryPreconditionException>(() => library.CleanupAsset(asset.Id, new(preview.Sha256, preview.LibraryVersion)));
        f.Write("broken.md", "---\nid: [broken\n---\nbody");
        Assert.ThrowsAny<Exception>(() => library.PreviewAssetCleanup(asset.Id));
        f.Write("broken.md", $"---\nid: {Guid.NewGuid()}\n---\n`../.assets/{asset.Id}.png`");
        Assert.Throws<LibraryConflictException>(() => library.PreviewAssetCleanup(asset.Id));
        Assert.True(File.Exists(assets.Open(asset.Id).Path));
    }
    [Fact]
    public async Task CleanupReceiptSurvivesRestartAndPreventsIdentityReuse()
    {
        using var f = new TestLibrary(); var assets = Assets(f); var bytes = Image(); var asset = await Upload(assets, bytes); Age(f, asset);
        var library = new LibraryStore(new(f.Root), null, null, null, assets); var preview = library.PreviewAssetCleanup(asset.Id);
        Assert.Equal(bytes.Length, library.CleanupAsset(asset.Id, new(preview.Sha256, preview.LibraryVersion)).ReclaimedBytes);
        var restarted = Assets(f); Assert.Empty(restarted.List(0, _ => 0).Results);
        Assert.True(restarted.IsRemoved(asset.Id)); Assert.Equal(0, restarted.RemoveUnreferenced(asset.Id, asset.Sha256).ReclaimedBytes);
        await Assert.ThrowsAsync<LibraryConflictException>(() => restarted.UploadAsync(asset.Id, "sample.png", null, new MemoryStream(bytes), bytes.Length, null, default));
    }
    [Fact]
    public async Task RealThumbnailIsBoundedAndCanonicalBytesUnchanged()
    {
        using var f = new TestLibrary(); var assets = Assets(f); var bytes = Image(); var asset = await Upload(assets, bytes);
        var output = Path.Combine(f.DerivedRoot, "thumb.png");
        await LocalAssetExtractor.WorkerAsync("thumbnail", assets.Open(asset.Id).Path, output);
        using var thumb = SKBitmap.Decode(File.ReadAllBytes(output)); Assert.Equal(256, thumb.Width); Assert.Equal(128, thumb.Height);
        Assert.Equal(bytes, File.ReadAllBytes(assets.Open(asset.Id).Path));
        await Assert.ThrowsAsync<IOException>(() => LocalAssetExtractor.WorkerAsync("thumbnail", assets.Open(asset.Id).Path, output));
    }
    [Fact]
    public async Task RealPdfExtractsTextAndRejectsPageLimit()
    {
        using var f = new TestLibrary(); var assets = Assets(f);
        var pdf = new PdfDocumentBuilder(); var font = pdf.AddStandard14Font(Standard14Font.Helvetica);
        pdf.AddPage(300, 300).AddText("Portable Slate attachment", 12, new PdfPoint(20, 200), font);
        var bytes = pdf.Build(); var asset = await Upload(assets, bytes, "note.pdf"); var output = Path.Combine(f.DerivedRoot, "pdf.txt");
        await LocalAssetExtractor.WorkerAsync("pdf", assets.Open(asset.Id).Path, output);
        Assert.Contains("Portable Slate attachment", File.ReadAllText(output)); Assert.Equal(bytes, File.ReadAllBytes(assets.Open(asset.Id).Path));
        var large = new PdfDocumentBuilder(); for (var i = 0; i < 101; i++) large.AddPage(100, 100);
        var tooMany = await Upload(assets, large.Build(), "pages.pdf");
        await Assert.ThrowsAsync<InvalidDataException>(() => LocalAssetExtractor.WorkerAsync("pdf", assets.Open(tooMany.Id).Path, output + ".new"));
    }
    [Fact]
    public async Task ExtractionFailureIsDurableAndDoesNotChangeOriginal()
    {
        using var f = new TestLibrary(); var assets = Assets(f); var bytes = "%PDF-damaged"u8.ToArray(); var asset = await Upload(assets, bytes, "broken.pdf");
        var derived = new AssetDerivatives(assets, Path.Combine(f.DerivedRoot, "extraction"), new InProcessExtractor());
        Assert.Equal("failed", (await derived.ExtractAsync(asset.Id, default)).State);
        var restarted = new AssetDerivatives(assets, Path.Combine(f.DerivedRoot, "extraction"), new InProcessExtractor());
        Assert.Equal("failed", restarted.Read(asset.Id).State); Assert.Equal(bytes, File.ReadAllBytes(assets.Open(asset.Id).Path));
    }
    private sealed class InProcessExtractor : IAssetExtractor
    {
        public Task ExtractAsync(string kind, string input, string output, CancellationToken token) => LocalAssetExtractor.WorkerAsync(kind, input, output);
    }
    [Fact]
    public async Task IsolatedWorkerRunsRealThumbnailWithoutServerConfiguration()
    {
        using var f = new TestLibrary(); var assets = Assets(f); var asset = await Upload(assets, Image());
        var derived = new AssetDerivatives(assets, Path.Combine(f.DerivedRoot, "worker"), new LocalAssetExtractor());
        var thumbnail = await derived.ThumbnailAsync(asset.Id, default);
        using var image = SKBitmap.Decode(File.ReadAllBytes(thumbnail)); Assert.Equal(256, image.Width);
        Assert.Equal(thumbnail, await derived.ThumbnailAsync(asset.Id, default));
    }
}

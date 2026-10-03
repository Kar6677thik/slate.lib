using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SkiaSharp;
using Slate.Lib.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Slate.Lib.Api;

public interface IAssetExtractor
{
    Task ExtractAsync(string kind, string input, string output, CancellationToken token);
}

// Parsing untrusted binaries happens in a disposable process, never on the API's request thread.
public sealed class LocalAssetExtractor : IAssetExtractor
{
    public async Task ExtractAsync(string kind, string input, string output, CancellationToken token)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { typeof(LocalAssetExtractor).Assembly.Location, "--asset-worker", kind, input, output }) start.ArgumentList.Add(argument);
        await RunBoundedAsync(start, output, token);
    }

    internal static async Task RunBoundedAsync(ProcessStartInfo start, string output, CancellationToken token)
    {
        using var process = Process.Start(start) ?? throw new IOException("Unable to start the local extraction worker.");
        var deadline = Stopwatch.StartNew();
        try
        {
            while (!process.HasExited)
            {
                token.ThrowIfCancellationRequested(); process.Refresh();
                if (deadline.Elapsed > TimeSpan.FromSeconds(45) || process.WorkingSet64 > 512L * 1024 * 1024 ||
                    (File.Exists(output) && new FileInfo(output).Length > 2 * 1024 * 1024))
                    throw new InvalidDataException("Extraction exceeded its time, memory, or output limit.");
                await Task.Delay(100, token);
            }
            if (process.ExitCode != 0) throw new InvalidDataException("Local extraction failed. The file may be unsupported, encrypted, or damaged; image OCR also requires Tesseract with English language data.");
            if (!File.Exists(output) || new FileInfo(output).Length > 2 * 1024 * 1024) throw new InvalidDataException("Invalid extraction output.");
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); } }
    }

    public static async Task WorkerAsync(string kind, string input, string output)
    {
        if (new FileInfo(input).Length > 25L * 1024 * 1024) throw new InvalidDataException("Extraction is limited to 25 MiB.");
        if (File.Exists(output)) throw new IOException("Worker output already exists.");
        if (kind == "pdf")
        {
            using var pdf = PdfDocument.Open(input);
            if (pdf.NumberOfPages > 100) throw new InvalidDataException("PDF extraction is limited to 100 pages.");
            var text = new StringBuilder();
            for (var page = 1; page <= pdf.NumberOfPages; page++)
            {
                text.AppendLine($"— Page {page} —").AppendLine(ContentOrderTextExtractor.GetText(pdf.GetPage(page)));
                if (text.Length > 500_000) throw new InvalidDataException("PDF text exceeds the extraction limit.");
            }
            await File.WriteAllTextAsync(output, text.ToString(), new UTF8Encoding(false)); return;
        }
        using var encoded = SKData.CreateCopy(await File.ReadAllBytesAsync(input));
        using var codec = SKCodec.Create(encoded) ?? throw new InvalidDataException("Unsupported image.");
        var info = codec.Info;
        if (info.Width > 8192 || info.Height > 8192 || (long)info.Width * info.Height > 16_000_000) throw new InvalidDataException("Image extraction is limited to 16 megapixels and 8192 pixels per side.");
        if (kind == "thumbnail")
        {
            using var original = SKBitmap.Decode(codec) ?? throw new InvalidDataException("Unable to decode image.");
            var scale = Math.Min(1d, 256d / Math.Max(info.Width, info.Height));
            using var bitmap = new SKBitmap(Math.Max(1, (int)(info.Width * scale)), Math.Max(1, (int)(info.Height * scale)));
            using (var canvas = new SKCanvas(bitmap)) canvas.DrawBitmap(original, new SKRect(0, 0, bitmap.Width, bitmap.Height));
            using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 85);
            using var stream = new FileStream(output, FileMode.CreateNew); data.SaveTo(stream); return;
        }
        if (kind != "ocr") throw new ArgumentException("Unknown extraction kind.");
        var start = new ProcessStartInfo("tesseract") { UseShellExecute = false, CreateNoWindow = true };
        // Tesseract writes a bounded derived text file; it never modifies the supplied binary.
        foreach (var argument in new[] { input, output[..^4], "-l", "eng", "--psm", "3" }) start.ArgumentList.Add(argument);
        start.Environment["OMP_THREAD_LIMIT"] = "1";
        await RunBoundedAsync(start, output, CancellationToken.None);
    }
}

public sealed class AssetDerivatives(AssetStore assets, string root, IAssetExtractor extractor)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock startGate = new();
    private Task<AssetDerivedText>? activeJob;
    public AssetDerivedText Start(Guid id)
    {
        lock (startGate)
        {
            if (activeJob is { IsCompleted: false } || gate.CurrentCount == 0) throw new LibraryConflictException("Another attachment is being processed. Try again when it finishes.");
            var asset = assets.Open(id).Metadata;
            if (asset.ContentType != "application/pdf" && !asset.InlineImage) throw new ArgumentException("Choose a PDF or image.");
            var jobId = Guid.NewGuid();
            activeJob = ExtractAsync(id, CancellationToken.None, jobId);
            _ = activeJob.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            return new(id, asset.Sha256, asset.InlineImage ? "ocr" : "pdf", "processing", "", DateTimeOffset.UtcNow, JobId: jobId);
        }
    }
    private string DirectoryPath { get; } = CreateRoot(root);
    private static string CreateRoot(string path) { Directory.CreateDirectory(path); return Path.GetFullPath(path); }
    private string StatePath(Guid id) => Path.Combine(DirectoryPath, id + ".json");
    public AssetDerivedText Read(Guid id)
    {
        var asset = assets.ReadMetadata(id); var path = StatePath(id);
        var state = File.Exists(path) ? JsonSerializer.Deserialize<AssetDerivedText>(File.ReadAllText(path), Json) : null;
        if (state?.State == "processing" && gate.CurrentCount == 1)
            state = state with { State = "failed", Error = "The previous extraction was interrupted. Run extraction again." };
        return state?.Sha256 == asset.Sha256 ? state : new(id, asset.Sha256, "", "not-extracted", "", DateTimeOffset.UtcNow);
    }
    public async Task<AssetDerivedText> ExtractAsync(Guid id, CancellationToken token, Guid? jobId = null)
    {
        if (!await gate.WaitAsync(0, token)) throw new LibraryConflictException("Another attachment is being processed. Try again when it finishes.");
        try
        {
            var asset = assets.Open(id);
            var kind = asset.Metadata.ContentType == "application/pdf" ? "pdf" : asset.Metadata.InlineImage ? "ocr" : throw new ArgumentException("Choose a PDF or image.");
            var result = new AssetDerivedText(id, asset.Metadata.Sha256, kind, "processing", "", DateTimeOffset.UtcNow, JobId: jobId ?? Guid.NewGuid());
            await AtomicJson.WriteAsync(StatePath(id), result, Json, token);
            var output = Path.Combine(DirectoryPath, Guid.NewGuid() + ".txt");
            try
            {
                await extractor.ExtractAsync(kind, asset.Path, output, token);
                if (new FileInfo(output).Length > 2 * 1024 * 1024) throw new InvalidDataException("Extracted text is too large.");
                result = result with { State = "ready", Text = await File.ReadAllTextAsync(output, token), UpdatedAt = DateTimeOffset.UtcNow };
            }
            catch (Exception e) when (e is not OutOfMemoryException) { result = result with { State = "failed", Error = e is OperationCanceledException ? "Extraction cancelled; retry when ready." : e.Message, UpdatedAt = DateTimeOffset.UtcNow }; }
            finally { if (File.Exists(output)) File.Delete(output); }
            await AtomicJson.WriteAsync(StatePath(id), result, Json, CancellationToken.None); return result;
        }
        finally { gate.Release(); }
    }
    public async Task<string> ThumbnailAsync(Guid id, CancellationToken token)
    {
        var asset = assets.Open(id); if (!asset.Metadata.InlineImage) throw new ArgumentException("This attachment is not an image.");
        var output = Path.Combine(DirectoryPath, asset.Metadata.Sha256 + ".png"); if (File.Exists(output)) return output;
        if (!await gate.WaitAsync(0, token)) throw new LibraryConflictException("Another attachment is being processed. Try again shortly.");
        try
        {
            if (File.Exists(output)) return output;
            var files = new DirectoryInfo(DirectoryPath).GetFiles("*.png").OrderBy(x => x.LastWriteTimeUtc).ToArray();
            var size = files.Sum(x => x.Length);
            foreach (var file in files) { if (size < 128L * 1024 * 1024 - 2 * 1024 * 1024) break; size -= file.Length; file.Delete(); }
            var temporary = Path.Combine(DirectoryPath, Guid.NewGuid() + ".part");
            try { await extractor.ExtractAsync("thumbnail", asset.Path, temporary, token); File.Move(temporary, output); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return output;
        }
        finally { gate.Release(); }
    }
}

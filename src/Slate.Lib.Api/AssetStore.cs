using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Slate.Lib.Core;

namespace Slate.Lib.Api;

public sealed record AssetOptions
{
    public string RootPath { get; init; } = "";
    public long MaximumBytes { get; init; } = 25L * 1024 * 1024;
}

public sealed partial class AssetStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string objects;
    private readonly string metadata;
    private readonly string staging;
    private readonly long maximumBytes;
    private readonly SemaphoreSlim gate = new(1, 1);

    public AssetStore(Microsoft.Extensions.Options.IOptions<AssetOptions> options)
    {
        var configured = options.Value;
        if (configured.MaximumBytes is < 1024 or > 1024L * 1024 * 1024) throw new InvalidOperationException("Asset maximum size is invalid.");
        var root = Path.GetFullPath(configured.RootPath);
        objects = Path.Combine(root, "objects"); metadata = Path.Combine(root, "metadata"); staging = Path.Combine(root, "staging");
        Directory.CreateDirectory(objects); Directory.CreateDirectory(metadata); Directory.CreateDirectory(staging);
        maximumBytes = configured.MaximumBytes;
        InitializeCatalog(root);
    }

    public long MaximumBytes => maximumBytes;

    public async Task<AssetMetadata> UploadAsync(Guid id, string? originalFilename, string? claimedContentType, Stream source, long? declaredLength, string? expectedSha256, CancellationToken token)
    {
        if (id == Guid.Empty) throw new ArgumentException("An asset ID is required.");
        if (declaredLength is < 0 || declaredLength > maximumBytes) throw new AssetTooLargeException();
        var filename = SafeFilename(originalFilename);
        var temporary = Path.Combine(staging, ".slate-tmp-" + Guid.NewGuid().ToString("N"));
        long length = 0;
        string sha;
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, token);
                    if (read == 0) break;
                    length += read;
                    if (length > maximumBytes) throw new AssetTooLargeException();
                    hash.AppendData(buffer.AsSpan(0, read));
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                }
                await output.FlushAsync(token); output.Flush(true);
            }
            sha = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (declaredLength.HasValue && declaredLength.Value != length) throw new InvalidDataException("Asset length did not match Content-Length.");
            if (!string.IsNullOrWhiteSpace(expectedSha256) && !sha.Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Asset SHA-256 did not match the supplied hash.");

            var (contentType, extension, inline) = DetectType(temporary, claimedContentType);
            var record = new AssetMetadata(id, filename, contentType, length, DateTimeOffset.UtcNow, sha, extension, inline);
            await gate.WaitAsync(token);
            try
            {
                if (IsRemoved(id)) throw new LibraryConflictException("This attachment was explicitly removed. Upload it with a new identity.");
                if (ReadMetadataUnsafe(id) is { } existing)
                {
                    if (existing.Sha256 == sha && existing.ByteSize == length) return existing;
                    throw new LibraryConflictException("That asset ID already belongs to different bytes.");
                }
                if (File.Exists(Path.Combine(cleanup, id.ToString("D") + ".json"))) throw new LibraryConflictException("This attachment was explicitly removed. Upload it with a new identity.");
                var destination = ObjectPath(id, extension);
                var existingObject = Directory.EnumerateFiles(objects, id.ToString("D") + ".*").SingleOrDefault();
                if (existingObject is not null)
                {
                    var existingHash = await HashFileAsync(existingObject, token);
                    if (existingHash != sha) throw new LibraryConflictException("That asset ID already belongs to different bytes.");
                    destination = existingObject;
                    record = record with { Extension = Path.GetExtension(existingObject) };
                }
                else File.Move(temporary, destination);
                await AtomicJson.WriteAsync(MetadataPath(id), record, Json, token);
                lock (catalogGate) { catalog[id] = record; catalogOrder = null; }
                return record;
            }
            finally { gate.Release(); }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public AssetMetadata ReadMetadata(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Invalid asset ID.");
        if (IsRemoved(id)) throw new FileNotFoundException();
        return ReadMetadataUnsafe(id) ?? throw new FileNotFoundException();
    }

    public (AssetMetadata Metadata, string Path) Open(Guid id)
    {
        var record = ReadMetadata(id);
        var path = ObjectPath(record.Id, record.Extension);
        if (!File.Exists(path)) throw new FileNotFoundException();
        return (record, path);
    }

    private AssetMetadata? ReadMetadataUnsafe(Guid id)
    {
        var path = MetadataPath(id);
        if (!File.Exists(path)) return null;
        var value = JsonSerializer.Deserialize<AssetMetadata>(File.ReadAllText(path), Json);
        if (value is null || value.Id != id || value.ByteSize < 0 || !Regex.IsMatch(value.Sha256, "^[0-9a-f]{64}$") || !Regex.IsMatch(value.Extension, @"^\.[a-z0-9]{1,8}$"))
            throw new InvalidDataException("Asset metadata is invalid.");
        return value;
    }

    private string MetadataPath(Guid id) => Path.Combine(metadata, id.ToString("D") + ".json");
    private string ObjectPath(Guid id, string extension) => Path.Combine(objects, id.ToString("D") + extension);

    private static string SafeFilename(string? value)
    {
        var filename = Path.GetFileName(value ?? "attachment.bin").Normalize(NormalizationForm.FormC);
        filename = new string(filename.Where(c => !char.IsControl(c) && c is not '/' and not '\\').ToArray()).Trim();
        if (filename.Length == 0) filename = "attachment.bin";
        return filename[..Math.Min(filename.Length, 180)];
    }

    private static (string ContentType, string Extension, bool Inline) DetectType(string path, string? claimed)
    {
        Span<byte> prefix = stackalloc byte[16];
        using var stream = File.OpenRead(path);
        var read = stream.Read(prefix);
        prefix = prefix[..read];
        if (prefix.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a })) return ("image/png", ".png", true);
        if (prefix.StartsWith(new byte[] { 0xff, 0xd8, 0xff })) return ("image/jpeg", ".jpg", true);
        if (prefix.StartsWith("GIF87a"u8) || prefix.StartsWith("GIF89a"u8)) return ("image/gif", ".gif", true);
        if (prefix.Length >= 12 && prefix[..4].SequenceEqual("RIFF"u8) && prefix[8..12].SequenceEqual("WEBP"u8)) return ("image/webp", ".webp", true);
        if (prefix.StartsWith("%PDF-"u8)) return ("application/pdf", ".pdf", false);
        var normalized = claimed?.Split(';')[0].Trim().ToLowerInvariant();
        return normalized switch
        {
            "text/plain" => ("text/plain", ".txt", false),
            "application/zip" => ("application/zip", ".zip", false),
            "application/json" => ("application/json", ".json", false),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => (normalized, ".docx", false),
            _ => ("application/octet-stream", ".bin", false)
        };
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
    }
}

public sealed class AssetTooLargeException : IOException;

using System.Text.Json;
using Slate.Lib.Core;

namespace Slate.Lib.Api;

public sealed record UpdateOptions
{
    public string ManifestPath { get; init; } = "";
    public string ArtifactRoot { get; init; } = "";
}

public sealed class UpdateCatalog(Microsoft.Extensions.Options.IOptions<UpdateOptions> options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string manifestPath = Path.GetFullPath(options.Value.ManifestPath);
    private readonly string artifactRoot = Path.GetFullPath(options.Value.ArtifactRoot);

    public UpdateAvailability Check(string platform, string currentVersion)
    {
        var current = ReleaseVersions.Parse(currentVersion);
        var manifest = ReadManifest();
        var latest = ReleaseVersions.Parse(manifest.LatestVersion);
        var artifact = platform.ToLowerInvariant() switch
        {
            "windows" => manifest.Windows,
            "android" => manifest.Android,
            _ => throw new ArgumentException("Platform must be windows or android.")
        };
        return new(currentVersion, manifest.LatestVersion, manifest.ReleasedAt, manifest.ReleaseNotes, artifact, latest > current);
    }

    public (ReleaseArtifact Metadata, string Path) Open(string filename)
    {
        if (filename != Path.GetFileName(filename) || string.IsNullOrWhiteSpace(filename)) throw new ArgumentException("Invalid release filename.");
        var manifest = ReadManifest();
        var artifact = new[] { manifest.Windows, manifest.Android }.SingleOrDefault(value => value.FileName.Equals(filename, StringComparison.Ordinal))
            ?? throw new FileNotFoundException();
        var path = Path.Combine(artifactRoot, filename);
        if (!File.Exists(path)) throw new FileNotFoundException();
        return (artifact, path);
    }

    public ReleaseManifest ReadManifest()
    {
        if (!File.Exists(manifestPath)) throw new FileNotFoundException("Release metadata is not configured.");
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(manifestPath), Json)
            ?? throw new InvalidDataException("Release manifest is empty.");
        Validate(manifest);
        return manifest;
    }

    private static void Validate(ReleaseManifest manifest)
    {
        if (manifest.SchemaVersion != 1 || manifest.ReleasedAt == default || manifest.ReleaseNotes.Length > 16_384)
            throw new InvalidDataException("Release manifest metadata is invalid.");
        _ = ReleaseVersions.Parse(manifest.LatestVersion);
        ValidateArtifact(manifest.Windows); ValidateArtifact(manifest.Android);
    }

    private static void ValidateArtifact(ReleaseArtifact artifact)
    {
        if (artifact.FileName != Path.GetFileName(artifact.FileName) || artifact.ByteSize < 1 || artifact.Sha256.Length != 64 || !artifact.Sha256.All(Uri.IsHexDigit) ||
            !artifact.Url.Equals("/v1/releases/" + Uri.EscapeDataString(artifact.FileName), StringComparison.Ordinal))
            throw new InvalidDataException("Release artifact metadata is invalid.");
    }
}

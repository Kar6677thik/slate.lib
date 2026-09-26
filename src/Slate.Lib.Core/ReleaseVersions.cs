using System.Security.Cryptography;

namespace Slate.Lib.Core;

public static class ReleaseVersions
{
    public static Version Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('v') || !Version.TryParse(value, out var version) ||
            version.Major < 0 || version.Minor < 0 || version.Build < 0 || version.Revision >= 0 || value != $"{version.Major}.{version.Minor}.{version.Build}")
            throw new InvalidDataException("Release versions must use major.minor.patch numeric form.");
        return version;
    }

    public static bool IsNewer(string current, string candidate) => Parse(candidate) > Parse(current);

    public static async Task VerifyFileAsync(string path, ReleaseArtifact artifact, CancellationToken token = default)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != artifact.ByteSize) throw new InvalidDataException("Downloaded release size did not match its manifest.");
        await using var stream = info.OpenRead();
        var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
        if (!actual.Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Downloaded release checksum did not match its manifest.");
    }
}

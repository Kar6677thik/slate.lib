using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Slate.Lib.Core;

namespace Slate.Lib.Api;

public sealed class DeviceTokens
{
    private sealed record StoredDevice(Guid Id, string Label, string TokenHash, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt);
    private sealed record TokenDocument(int SchemaVersion, IReadOnlyList<StoredDevice> Devices);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string file;
    private readonly Lock gate = new();

    public DeviceTokens(string file) => this.file = Path.GetFullPath(file);

    public bool Accepts(string token)
    {
        if (token.Length != 64) return false;
        byte[] digest;
        try { digest = SHA256.HashData(Encoding.UTF8.GetBytes(token)); }
        catch { return false; }
        lock (gate)
        {
            var document = LoadDocument();
            var matched = document.Devices.FirstOrDefault(device => device.RevokedAt is null &&
                IsHash(device.TokenHash) && CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(device.TokenHash)));
            if (matched is null) return false;
            if (matched.LastUsedAt is null || DateTimeOffset.UtcNow - matched.LastUsedAt > TimeSpan.FromMinutes(1))
            {
                var now = DateTimeOffset.UtcNow;
                Write(document with { Devices = document.Devices.Select(device => device.Id == matched.Id ? device with { LastUsedAt = now } : device).ToArray() });
            }
            return true;
        }
    }

    public IReadOnlyList<DeviceSummary> Load()
    {
        lock (gate) return LoadDocument().Devices.Select(ToSummary).OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public string Create(string label)
    {
        label = ValidateLabel(label);
        lock (gate)
        {
            var document = LoadDocument();
            if (document.Devices.Any(device => device.Label.Equals(label, StringComparison.OrdinalIgnoreCase) && device.RevokedAt is null))
                throw new InvalidOperationException("An active device with that label already exists. Revoke or rotate it first.");
            var (token, stored) = NewDevice(label);
            Write(document with { Devices = document.Devices.Append(stored).ToArray() });
            return token;
        }
    }

    public string Rotate(string label)
    {
        label = ValidateLabel(label);
        lock (gate)
        {
            var document = LoadDocument();
            if (!document.Devices.Any(device => device.Label.Equals(label, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("That device label does not exist.");
            var now = DateTimeOffset.UtcNow;
            var revoked = document.Devices.Select(device => device.Label.Equals(label, StringComparison.OrdinalIgnoreCase) && device.RevokedAt is null
                ? device with { RevokedAt = now } : device).ToList();
            var (token, stored) = NewDevice(label);
            revoked.Add(stored);
            Write(document with { Devices = revoked });
            return token;
        }
    }

    public DeviceSummary Revoke(string label)
    {
        label = ValidateLabel(label);
        lock (gate)
        {
            var document = LoadDocument();
            var active = document.Devices.LastOrDefault(device => device.Label.Equals(label, StringComparison.OrdinalIgnoreCase) && device.RevokedAt is null)
                ?? throw new InvalidOperationException("No active device with that label exists.");
            var now = DateTimeOffset.UtcNow;
            Write(document with { Devices = document.Devices.Select(device => device.Id == active.Id ? device with { RevokedAt = now } : device).ToArray() });
            return ToSummary(active with { RevokedAt = now });
        }
    }

    private TokenDocument LoadDocument()
    {
        if (!File.Exists(file)) return new(1, []);
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        if (json.RootElement.TryGetProperty("schemaVersion", out _))
        {
            var document = json.RootElement.Deserialize<TokenDocument>(Json) ?? throw new InvalidDataException("Device token file is invalid.");
            if (document.SchemaVersion != 1 || document.Devices.Any(device => device.Id == Guid.Empty || !IsHash(device.TokenHash)))
                throw new InvalidDataException("Device token file is invalid.");
            return document;
        }
        var legacy = json.RootElement.Deserialize<Dictionary<string, string>>(Json) ?? [];
        var created = File.GetLastWriteTimeUtc(file);
        return new(1, legacy.Select(pair => new StoredDevice(Guid.NewGuid(), ValidateLabel(pair.Key), pair.Value,
            new DateTimeOffset(created, TimeSpan.Zero), null, null)).ToArray());
    }

    private void Write(TokenDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, Json), new UTF8Encoding(false));
            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) stream.Flush(true);
            File.Move(temporary, file, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static (string Token, StoredDevice Device) NewDevice(string label)
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return (token, new(Guid.NewGuid(), label, hash, DateTimeOffset.UtcNow, null, null));
    }

    private static DeviceSummary ToSummary(StoredDevice device) => new(device.Id, device.Label, device.CreatedAt, device.LastUsedAt, device.RevokedAt);
    private static bool IsHash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    private static string ValidateLabel(string value)
    {
        var label = value.Trim();
        if (label.Length is < 1 or > 80 || label.Any(char.IsControl)) throw new ArgumentException("Device label must contain 1 to 80 printable characters.");
        return label;
    }
}

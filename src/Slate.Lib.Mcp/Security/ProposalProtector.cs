using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Slate.Lib.Mcp.Configuration;
using Slate.Lib.Mcp.Models;

namespace Slate.Lib.Mcp.Security;

public sealed class ProposalProtector
{
    private readonly byte[] key;
    private readonly SlateMcpOptions options;

    public ProposalProtector(IConfiguration configuration, IOptions<SlateMcpOptions> options, IHostEnvironment environment)
    {
        this.options = options.Value;
        var configured = configuration["Secrets:ProposalSigningKey"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
                throw new InvalidOperationException("Secrets:ProposalSigningKey is required outside Development.");
            configured = "development-only-slate-mcp-proposal-key-change-me";
        }
        key = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
    }

    public (ProposalPayload Payload, string Token) Create(Guid noteId, string revision, string proposedMarkdown)
    {
        var payload = new ProposalPayload(noteId, revision, Hash(proposedMarkdown), DateTimeOffset.UtcNow.AddMinutes(options.ProposalLifetimeMinutes));
        return (payload, Protect(new { kind = "note", payload }));
    }

    public ProposalPayload Validate(string token, string proposedMarkdown)
    {
        using var document = Unprotect(token, "edit");
        if (document.RootElement.GetProperty("kind").GetString() != "note") throw new ArgumentException("The edit proposal token has the wrong purpose.");
        var payload = document.RootElement.GetProperty("payload").Deserialize<ProposalPayload>() ?? throw new ArgumentException("The edit proposal token is invalid.");
        if (payload.ExpiresAt <= DateTimeOffset.UtcNow) throw new ArgumentException("The edit proposal has expired. Preview it again.");
        if (!string.Equals(payload.ProposedSha256, Hash(proposedMarkdown), StringComparison.Ordinal))
            throw new ArgumentException("The proposed Markdown differs from the reviewed preview.");
        return payload;
    }

    public (BulkProposalPayload Payload, string Token) CreateBulk(Guid operationId, string operation, string fingerprint)
    {
        var payload = new BulkProposalPayload(operationId, operation, fingerprint, DateTimeOffset.UtcNow.AddMinutes(options.ProposalLifetimeMinutes));
        return (payload, Protect(new { kind = "bulk", payload }));
    }

    public BulkProposalPayload ValidateBulk(string token, Guid operationId, string fingerprint)
    {
        using var document = Unprotect(token, "bulk");
        if (document.RootElement.GetProperty("kind").GetString() != "bulk") throw new ArgumentException("The bulk proposal token has the wrong purpose.");
        var payload = document.RootElement.GetProperty("payload").Deserialize<BulkProposalPayload>() ?? throw new ArgumentException("The bulk proposal token is invalid.");
        if (payload.ExpiresAt <= DateTimeOffset.UtcNow) throw new ArgumentException("The bulk preview has expired. Preview it again.");
        if (payload.OperationId != operationId || !string.Equals(payload.Fingerprint, fingerprint, StringComparison.Ordinal))
            throw new ArgumentException("The bulk operation differs from the reviewed preview.");
        return payload;
    }

    private string Protect<T>(T payload)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        var signature = HMACSHA256.HashData(key, json);
        return WebEncoders.Base64UrlEncode(json) + "." + WebEncoders.Base64UrlEncode(signature);
    }

    private JsonDocument Unprotect(string token, string label)
    {
        var parts = token.Split('.', 2);
        if (parts.Length != 2) throw new ArgumentException($"The {label} proposal token is malformed.");
        byte[] payloadBytes;
        byte[] signature;
        try { payloadBytes = WebEncoders.Base64UrlDecode(parts[0]); signature = WebEncoders.Base64UrlDecode(parts[1]); }
        catch (FormatException) { throw new ArgumentException($"The {label} proposal token is malformed."); }
        var expected = HMACSHA256.HashData(key, payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(expected, signature)) throw new ArgumentException($"The {label} proposal token is invalid.");
        return JsonDocument.Parse(payloadBytes);
    }

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

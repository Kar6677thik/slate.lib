using Microsoft.IdentityModel.Tokens;

namespace Slate.Lib.Mcp.Security;

/// <summary>
/// Bearer token rules for Orbit Identity, the same as Sprawl's: an RFC 9068 access token (<c>typ: at+jwt</c>) signed
/// with ES256, for exactly this resource, with an expiry. Issuer and signing keys come from the authority's discovery
/// document. Tokens of any other type or algorithm are refused even when signed by a key the server trusts.
/// </summary>
public static class OrbitTokenValidation
{
    public const string AccessTokenType = "at+jwt";

    public static TokenValidationParameters Parameters(string audience) => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidAudiences = [audience],
        ValidateLifetime = true,
        RequireExpirationTime = true,
        ValidateIssuerSigningKey = true,
        ValidTypes = [AccessTokenType],
        ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
        ClockSkew = TimeSpan.FromMinutes(1),
        NameClaimType = "sub"
    };
}

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Slate.Lib.Mcp.Security;

public sealed class DevelopmentBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expected = configuration["Development:BearerToken"];
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(expected) || !authorization.StartsWith("Bearer ", StringComparison.Ordinal) ||
            !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(authorization[7..])))
            return Task.FromResult(AuthenticateResult.Fail("A valid development bearer token is required."));
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "local-development"),
            new Claim("client_id", "local-development"),
            new Claim("scope", string.Join(' ', SlateScopes.All))
        ], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var origin = configuration["Mcp:PublicOrigin"]?.TrimEnd('/') ?? "http://localhost:5081";
        Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{origin}/.well-known/oauth-protected-resource\", scope=\"{SlateScopes.Discovery}\"";
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}

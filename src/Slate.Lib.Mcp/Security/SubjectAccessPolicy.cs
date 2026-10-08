using System.Security.Claims;
using Slate.Lib.Mcp.Configuration;

namespace Slate.Lib.Mcp.Security;

public static class SubjectAccessPolicy
{
    public static void ValidateProductionConfiguration(OAuthOptions options)
    {
        if (options.AllowedSubjectIds.Length == 0 || options.AllowedSubjectIds.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("OAuth:AllowedSubjectIds must contain at least one approved Auth0 user subject outside Development.");
        if (!string.Equals(options.Audience, options.Resource, StringComparison.Ordinal))
            throw new InvalidOperationException("OAuth:Audience must exactly match OAuth:Resource for the Auth0 API identifier.");
        if (options.AuthorizationServers.Length != 1 || !string.Equals(options.AuthorizationServers[0], options.Authority, StringComparison.Ordinal))
            throw new InvalidOperationException("OAuth:AuthorizationServers must contain the exact Auth0 issuer configured in OAuth:Authority.");
    }

    public static bool IsAllowed(ClaimsPrincipal? principal, IReadOnlyCollection<string> allowedSubjectIds)
    {
        if (principal is null || allowedSubjectIds.Count == 0) return false;

        var subject = principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject) || IsMachineIdentity(principal, subject)) return false;

        return allowedSubjectIds.Contains(subject, StringComparer.Ordinal);
    }

    private static bool IsMachineIdentity(ClaimsPrincipal principal, string subject)
    {
        if (subject.EndsWith("@clients", StringComparison.OrdinalIgnoreCase)) return true;

        var grantType = principal.FindFirstValue("gty") ?? principal.FindFirstValue("grant_type");
        return string.Equals(grantType, "client-credentials", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(grantType, "client_credentials", StringComparison.OrdinalIgnoreCase);
    }
}

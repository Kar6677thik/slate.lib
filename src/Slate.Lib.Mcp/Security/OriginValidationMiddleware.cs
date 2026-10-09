using Microsoft.Extensions.Options;
using Slate.Lib.Mcp.Configuration;

namespace Slate.Lib.Mcp.Security;

public sealed class OriginValidationMiddleware(RequestDelegate next, IOptions<SlateMcpOptions> options)
{
    private readonly HashSet<string> allowedOrigins = options.Value.AllowedOrigins
        .Append(options.Value.PublicOrigin)
        .Select(NormalizeOrigin)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/mcp") && context.Request.Headers.Origin is { Count: > 0 } origins)
        {
            if (!Uri.TryCreate(origins[0], UriKind.Absolute, out var origin) ||
                origin.UserInfo.Length > 0 ||
                origin.Query.Length > 0 ||
                origin.Fragment.Length > 0 ||
                origin.AbsolutePath != "/" ||
                !allowedOrigins.Contains(NormalizeOrigin(origin)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "The request origin is not allowed." });
                return;
            }
        }
        await next(context);
    }

    private static string NormalizeOrigin(string value) => NormalizeOrigin(new Uri(value));

    private static string NormalizeOrigin(Uri value) => $"{value.Scheme}://{value.Authority}";
}

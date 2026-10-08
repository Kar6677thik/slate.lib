using Microsoft.Extensions.Options;
using Slate.Lib.Mcp.Configuration;

namespace Slate.Lib.Mcp.Security;

public sealed class OriginValidationMiddleware(RequestDelegate next, IOptions<SlateMcpOptions> options)
{
    private readonly Uri publicOrigin = new(options.Value.PublicOrigin);

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/mcp") && context.Request.Headers.Origin is { Count: > 0 } origins)
        {
            if (!Uri.TryCreate(origins[0], UriKind.Absolute, out var origin) ||
                !string.Equals(origin.Scheme, publicOrigin.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(origin.Authority, publicOrigin.Authority, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "The request origin is not allowed." });
                return;
            }
        }
        await next(context);
    }
}

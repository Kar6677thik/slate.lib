using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;
using Slate.Lib.Mcp.Clients;
using Slate.Lib.Mcp.Configuration;
using Slate.Lib.Mcp.Security;
using Slate.Lib.Mcp.Services;
using Slate.Lib.Mcp.Tools;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
builder.Services.AddOptions<SlateMcpOptions>().BindConfiguration(SlateMcpOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<OAuthOptions>().BindConfiguration(OAuthOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<RateLimitOptions>().BindConfiguration(RateLimitOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

var mcpSettings = builder.Configuration.GetSection(SlateMcpOptions.SectionName).Get<SlateMcpOptions>() ?? new();
var oauthSettings = builder.Configuration.GetSection(OAuthOptions.SectionName).Get<OAuthOptions>() ?? new();
ValidateUpstream(mcpSettings.CanonicalBaseUrl, mcpSettings.AllowedCanonicalHosts, nameof(mcpSettings.CanonicalBaseUrl), builder.Environment);
if (!string.IsNullOrWhiteSpace(mcpSettings.IntelligenceBaseUrl)) ValidateUpstream(mcpSettings.IntelligenceBaseUrl, mcpSettings.AllowedIntelligenceHosts, nameof(mcpSettings.IntelligenceBaseUrl), builder.Environment);
ValidatePublicUrl(mcpSettings.PublicOrigin, nameof(mcpSettings.PublicOrigin), builder.Environment, requireOrigin: true);
ValidatePublicUrl(mcpSettings.CanonicalPublicUrl, nameof(mcpSettings.CanonicalPublicUrl), builder.Environment, requireOrigin: true);
foreach (var allowedOrigin in mcpSettings.AllowedOrigins)
    ValidatePublicUrl(allowedOrigin, nameof(mcpSettings.AllowedOrigins), builder.Environment, requireOrigin: true);
ValidatePublicUrl(oauthSettings.Resource, nameof(oauthSettings.Resource), builder.Environment, requireOrigin: false);
if (!string.IsNullOrWhiteSpace(oauthSettings.Authority))
    ValidatePublicUrl(oauthSettings.Authority, nameof(oauthSettings.Authority), builder.Environment, requireOrigin: false);
foreach (var authorizationServer in oauthSettings.AuthorizationServers)
    ValidatePublicUrl(authorizationServer, nameof(oauthSettings.AuthorizationServers), builder.Environment, requireOrigin: false);

var isProductionSecurityBoundary = !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing");
if (isProductionSecurityBoundary)
    SubjectAccessPolicy.ValidateProductionConfiguration(oauthSettings);

const string authScheme = "SlateBearer";
if ((builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")) && string.IsNullOrWhiteSpace(oauthSettings.Authority))
{
    builder.Services.AddAuthentication(authScheme).AddScheme<AuthenticationSchemeOptions, DevelopmentBearerHandler>(authScheme, _ => { });
}
else
{
    if (string.IsNullOrWhiteSpace(oauthSettings.Authority)) throw new InvalidOperationException("OAuth:Authority is required outside Development.");
    builder.Services.AddAuthentication(authScheme).AddJwtBearer(authScheme, options =>
    {
        options.Authority = oauthSettings.Authority.TrimEnd('/');
        options.Audience = oauthSettings.Audience;
        options.RequireHttpsMetadata = true;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "sub"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (!SubjectAccessPolicy.IsAllowed(context.Principal, oauthSettings.AllowedSubjectIds))
                {
                    context.Fail("The Auth0 user is not approved for this Slate library.");
                    return Task.CompletedTask;
                }
                if (oauthSettings.AllowedCallerIds.Length == 0) return Task.CompletedTask;
                var caller = context.Principal?.FindFirstValue("azp") ?? context.Principal?.FindFirstValue("client_id");
                if (caller is null || !oauthSettings.AllowedCallerIds.Contains(caller, StringComparer.Ordinal)) context.Fail("The OAuth caller is not allowed.");
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{mcpSettings.PublicOrigin.TrimEnd('/')}/.well-known/oauth-protected-resource\"";
                return Task.CompletedTask;
            }
        };
    });
}

builder.Services.AddAuthorization(options =>
{
    foreach (var scope in SlateScopes.All)
        options.AddPolicy(SlateScopes.Policy(scope), policy => policy.RequireAuthenticatedUser().RequireAssertion(context => HasScope(context.User, scope) || HasScope(context.User, SlateScopes.Admin)));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("mcp", context =>
    {
        var rate = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
        var key = context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rate.PermitLimit,
            Window = TimeSpan.FromSeconds(rate.WindowSeconds),
            QueueLimit = rate.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.ForwardLimit = 1;
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
    foreach (var proxy in builder.Configuration.GetSection("Networking:TrustedProxies").Get<string[]>() ?? []) options.KnownProxies.Add(IPAddress.Parse(proxy));
    foreach (var network in builder.Configuration.GetSection("Networking:TrustedProxyNetworks").Get<string[]>() ?? []) options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
});

builder.Services.AddSingleton<ProposalProtector>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<LibraryContext>();
builder.Services.AddSingleton<ToolExecutor>();
builder.Services.AddSingleton<LearningService>();

builder.Services.AddHttpClient<CanonicalSlateClient>(client =>
{
    client.BaseAddress = new Uri(mcpSettings.CanonicalBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(mcpSettings.RequestTimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Slate.Lib.Mcp/1.0");
}).ConfigurePrimaryHttpMessageHandler(() => SecureHandler());
builder.Services.AddHttpClient<IntelligenceClient>(client =>
{
    if (!string.IsNullOrWhiteSpace(mcpSettings.IntelligenceBaseUrl)) client.BaseAddress = new Uri(mcpSettings.IntelligenceBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(Math.Max(mcpSettings.RequestTimeoutSeconds, 70));
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Slate.Lib.Mcp/1.0");
}).ConfigurePrimaryHttpMessageHandler(() => SecureHandler());

builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .AddAuthorizationFilters()
    .WithTools<LibraryReadTools>()
    .WithTools<LibraryMutationTools>()
    .WithTools<IntelligenceTools>()
    .WithTools<LearningTools>();

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);
var app = builder.Build();
app.UseForwardedHeaders();
app.UseMiddleware<OriginValidationMiddleware>();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing") && !context.Request.IsHttps && !context.Request.Path.StartsWithSegments("/health"))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "HTTPS is required." });
        return;
    }
    await next(context);
});
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" })).AllowAnonymous();
app.MapGet("/health/ready", async (LibraryContext library, CancellationToken token) =>
{
    try { var status = await library.Status(token); return Results.Ok(new { status = "Ready", libraryId = status.LibraryId }); }
    catch { return Results.Json(new { status = "NotReady" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
}).AllowAnonymous();

object ProtectedResourceMetadata() => new
{
    resource = oauthSettings.Resource,
    authorization_servers = oauthSettings.AuthorizationServers.Length > 0 ? oauthSettings.AuthorizationServers : string.IsNullOrWhiteSpace(oauthSettings.Authority) ? [] : new[] { oauthSettings.Authority.TrimEnd('/') },
    scopes_supported = mcpSettings.EnableDestructiveOperations ? SlateScopes.All : SlateScopes.All.Where(scope => scope != SlateScopes.Delete).ToArray(),
    bearer_methods_supported = new[] { "header" },
    resource_name = "Slate knowledge library"
};
app.MapGet("/.well-known/oauth-protected-resource", ProtectedResourceMetadata).AllowAnonymous();
app.MapGet("/.well-known/oauth-protected-resource/mcp", ProtectedResourceMetadata).AllowAnonymous();
app.MapMcp("/mcp").RequireAuthorization().RequireRateLimiting("mcp");
app.Run();

static bool HasScope(ClaimsPrincipal principal, string required) => principal.Claims
    .Where(claim => claim.Type is "scope" or "scp")
    .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    .Contains(required, StringComparer.Ordinal);

static SocketsHttpHandler SecureHandler() => new()
{
    AllowAutoRedirect = false,
    AutomaticDecompression = DecompressionMethods.None,
    ConnectTimeout = TimeSpan.FromSeconds(5),
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    MaxConnectionsPerServer = 32,
    UseCookies = false
};

static void ValidateUpstream(string value, string[] allowedHosts, string name, IHostEnvironment environment)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath.Trim('/') != "")
        throw new InvalidOperationException($"{name} must be an origin URL without credentials, path, query, or fragment.");
    if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && (environment.IsDevelopment() || environment.IsEnvironment("Testing") || allowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))))
        throw new InvalidOperationException($"{name} must use HTTPS unless its explicit host is an approved private service.");
    if (!allowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException($"{name} host is not in the configured allowlist.");
}

static void ValidatePublicUrl(string value, string name, IHostEnvironment environment, bool requireOrigin)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || (requireOrigin && uri.AbsolutePath.Trim('/').Length > 0))
        throw new InvalidOperationException($"{name} must be an origin URL without credentials, path, query, or fragment.");
    if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing") && uri.Scheme != Uri.UriSchemeHttps)
        throw new InvalidOperationException($"{name} must use HTTPS outside Development.");
}

public partial class Program;

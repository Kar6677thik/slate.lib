using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Slate.Lib.Mcp.Configuration;

namespace Slate.Lib.Mcp.Clients;

public sealed class IntelligenceClient(HttpClient http, IOptions<SlateMcpOptions> options, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SlateMcpOptions settings = options.Value;
    private readonly string deviceToken = configuration["Secrets:CanonicalDeviceToken"] ?? "";

    public bool Configured => !string.IsNullOrWhiteSpace(settings.IntelligenceBaseUrl);

    public async Task<JsonElement> Post(string path, object body, CancellationToken cancellationToken)
    {
        if (!Configured) throw new SlateUpstreamException(503, "intelligence_not_configured", "The derived intelligence service is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/intelligence/" + path)
        {
            Content = JsonContent.Create(body, options: Json)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
        request.Headers.TryAddWithoutValidation("X-Slate-Server", settings.CanonicalPublicUrl.TrimEnd('/'));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var bytes = await BoundedHttpContent.ReadAsync(response.Content, settings.MaximumResponseBytes, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var code = response.StatusCode == System.Net.HttpStatusCode.Forbidden ? "intelligence_upstream_rejected" : "intelligence_unavailable";
            throw new SlateUpstreamException((int)response.StatusCode, code, "The derived intelligence service could not complete the request.", (int)response.StatusCode >= 500);
        }
        if (bytes.Length == 0) return JsonSerializer.SerializeToElement(new { accepted = true }, Json);
        return JsonSerializer.Deserialize<JsonElement>(bytes, Json);
    }

    public async Task Notify(object mutationEvent, CancellationToken cancellationToken)
    {
        if (!Configured) return;
        try { _ = await Post("events", mutationEvent, cancellationToken); }
        catch (Exception exception) when (exception is SlateUpstreamException or HttpRequestException or TaskCanceledException or JsonException)
        {
            // Derived intelligence is disposable and must never change the outcome of a canonical write.
        }
    }
}

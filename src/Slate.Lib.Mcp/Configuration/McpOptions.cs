using System.ComponentModel.DataAnnotations;

namespace Slate.Lib.Mcp.Configuration;

public sealed class SlateMcpOptions
{
    public const string SectionName = "Mcp";

    [Required, Url] public string PublicOrigin { get; set; } = "http://localhost:5081";
    [Required, Url] public string CanonicalBaseUrl { get; set; } = "http://localhost:5080";
    [Required, Url] public string CanonicalPublicUrl { get; set; } = "http://localhost:5080";
    [Url] public string? IntelligenceBaseUrl { get; set; }
    public string[] AllowedOrigins { get; set; } = ["https://chatgpt.com", "https://chat.openai.com"];
    public Guid? ExpectedLibraryId { get; set; }
    public string[] AllowedCanonicalHosts { get; set; } = ["localhost"];
    public string[] AllowedIntelligenceHosts { get; set; } = ["localhost"];
    [Range(1, 120)] public int RequestTimeoutSeconds { get; set; } = 28;
    [Range(1024, 8 * 1024 * 1024)] public int MaximumResponseBytes { get; set; } = 2 * 1024 * 1024;
    [Range(1024, 2 * 1024 * 1024)] public int MaximumMarkdownCharacters { get; set; } = 512 * 1024;
    [Range(1, 100)] public int MaximumBatchSize { get; set; } = 20;
    [Range(1, 500)] public int MaximumBulkItems { get; set; } = 100;
    public bool EnableWrites { get; set; } = true;
    public bool EnableDestructiveOperations { get; set; }
    [Range(1, 60)] public int ProposalLifetimeMinutes { get; set; } = 15;
}

public sealed class OAuthOptions
{
    public const string SectionName = "OAuth";
    public string Authority { get; set; } = "";
    [Required] public string Audience { get; set; } = "http://localhost:5081/mcp";
    [Required, Url] public string Resource { get; set; } = "http://localhost:5081/mcp";
    public string[] AuthorizationServers { get; set; } = [];
    public string[] AllowedSubjectIds { get; set; } = [];
    public string[] AllowedCallerIds { get; set; } = [];
}

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";
    [Range(1, 1000)] public int PermitLimit { get; set; } = 90;
    [Range(1, 3600)] public int WindowSeconds { get; set; } = 60;
    [Range(0, 100)] public int QueueLimit { get; set; } = 4;
}

namespace Slate.Lib.Mcp.Tools;

/// <summary>
/// OpenAI-compatible OAuth declarations mirrored into each MCP tool descriptor's
/// <c>_meta.securitySchemes</c> field. Runtime authorization remains enforced by
/// ASP.NET Core policies and JWT validation.
/// </summary>
internal static class SlateToolSecurity
{
    public const string Read = """[{"type":"oauth2","scopes":["slate.read"]}]""";
    public const string Analyze = """[{"type":"oauth2","scopes":["slate.analyze"]}]""";
    public const string Write = """[{"type":"oauth2","scopes":["slate.write"]}]""";
    public const string Organize = """[{"type":"oauth2","scopes":["slate.organize"]}]""";
}

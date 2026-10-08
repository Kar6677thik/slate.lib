namespace Slate.Lib.Mcp.Security;

public static class SlateScopes
{
    public const string Read = "slate.read";
    public const string Analyze = "slate.analyze";
    public const string Write = "slate.write";
    public const string Organize = "slate.organize";
    public const string Delete = "slate.delete";
    public const string Admin = "slate.admin";
    public static readonly string[] All = [Read, Analyze, Write, Organize, Delete, Admin];

    public static string Policy(string scope) => "scope:" + scope;
}

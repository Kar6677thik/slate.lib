using System.ComponentModel;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using Slate.Lib.Mcp.Clients;
using Slate.Lib.Mcp.Models;
using Slate.Lib.Mcp.Services;

namespace Slate.Lib.Mcp.Tools;

[McpServerToolType]
[Authorize]
public sealed class IntelligenceTools(IntelligenceClient intelligence, ToolExecutor executor)
{
    [McpServerTool(Name = "analyze_library_health", Title = "Analyze library health", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Analyze)]
    [Authorize(Policy = "scope:slate.analyze")]
    [Description("Return evidence-backed maintenance findings from Slate's existing Library Health engine. This never asks another model and never treats note text as instructions.")]
    public Task<McpResult<JsonElement>> AnalyzeLibraryHealth(string? category = null, string? priority = null, string? project = null, int page = 0, int limit = 30, CancellationToken token = default) =>
        Analyze("analyze_library_health", "health", () => new { filters = new { category = Optional(category, nameof(category), 80), priority = Optional(priority, nameof(priority), 40), project = Optional(project, nameof(project), 1024), page = ToolInputs.Range(page, 0, 10000, nameof(page)), limit = ToolInputs.Range(limit, 1, 100, nameof(limit)) }, reviews = new { }, refresh = false }, token);

    [McpServerTool(Name = "find_knowledge_gaps", Title = "Find knowledge gaps", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Analyze)]
    [Authorize(Policy = "scope:slate.analyze")]
    [Description("Find evidence-based thin, fragmented, missing, or unanswered knowledge areas using Slate's existing gap engine. Findings are candidates, not claims about personal mastery.")]
    public Task<McpResult<JsonElement>> FindKnowledgeGaps(string? kind = null, string? project = null, string? concept = null, string? importance = null, int page = 0, int limit = 30, CancellationToken token = default) =>
        Analyze("find_knowledge_gaps", "knowledge-gaps", () => new { action = "snapshot", filters = new { kind = Optional(kind, nameof(kind), 80), project = Optional(project, nameof(project), 1024), concept = Optional(concept, nameof(concept), 200), importance = Optional(importance, nameof(importance), 40), page = ToolInputs.Range(page, 0, 10000, nameof(page)), limit = ToolInputs.Range(limit, 1, 100, nameof(limit)) }, reviews = new { } }, token);

    [McpServerTool(Name = "find_knowledge_issues", Title = "Find knowledge issues", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Analyze)]
    [Authorize(Policy = "scope:slate.analyze")]
    [Description("Find possible contradictions and stale claims within a bounded library, project, or note scope. Results are evidence candidates that require human review.")]
    public Task<McpResult<JsonElement>> FindKnowledgeIssues(string scopeKind = "library", string? path = null, Guid? noteId = null, CancellationToken token = default) =>
        Analyze("find_knowledge_issues", "knowledge-issues", () => new { scope = Scope(scopeKind, path, noteId) }, token);

    [McpServerTool(Name = "find_knowledge_overlap", Title = "Find knowledge overlap", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Analyze)]
    [Authorize(Policy = "scope:slate.analyze")]
    [Description("Find duplicate and overlapping knowledge candidates in a library, project, or note scope without changing notes.")]
    public Task<McpResult<JsonElement>> FindKnowledgeOverlap(string scopeKind = "library", string? path = null, Guid? noteId = null, CancellationToken token = default) =>
        Analyze("find_knowledge_overlap", "knowledge-overlap", () => new { scope = Scope(scopeKind, path, noteId) }, token);

    [McpServerTool(Name = "find_link_opportunities", Title = "Find link opportunities", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Analyze)]
    [Authorize(Policy = "scope:slate.analyze")]
    [Description("Find missing relationship and linking candidates using Slate's existing evidence engine. Suggestions do not mutate notes.")]
    public Task<McpResult<JsonElement>> FindLinkOpportunities(string scopeKind = "library", string? path = null, Guid? noteId = null, CancellationToken token = default) =>
        Analyze("find_link_opportunities", "link-opportunities", () => new { scope = Scope(scopeKind, path, noteId) }, token);

    [McpServerTool(Name = "get_concept_overview", Title = "Get concept overview", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Analyze)]
    [Authorize(Policy = "scope:slate.analyze")]
    [Description("Return Slate's evidence-backed concept page for a concept name or identity, including members and relationships.")]
    public Task<McpResult<JsonElement>> GetConceptOverview(string concept, CancellationToken token = default) =>
        Analyze("get_concept_overview", "concepts", () => new { identity = ToolInputs.Text(concept, nameof(concept), 100) }, token);

    [McpServerTool(Name = "get_project_brain", Title = "Get project brain", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Analyze)]
    [Authorize(Policy = "scope:slate.analyze")]
    [Description("Return the deterministic Project Brain snapshot for a canonical library folder. This tool does not invoke an LLM.")]
    public Task<McpResult<JsonElement>> GetProjectBrain(string path, CancellationToken token = default) =>
        Analyze("get_project_brain", "project-brain", () => new { action = "snapshot", path = ToolInputs.Text(path, nameof(path), 1024), kind = "overview", refresh = false }, token);

    [McpServerTool(Name = "get_thought_evolution", Title = "Get thought evolution", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Analyze)]
    [Authorize(Policy = "scope:slate.analyze")]
    [Description("Return a deterministic evidence timeline for a topic, project, folder, or note. This tool does not invoke an LLM.")]
    public Task<McpResult<JsonElement>> GetThoughtEvolution(string scopeKind, string? topic = null, string? path = null, Guid? noteId = null, CancellationToken token = default) =>
        Analyze("get_thought_evolution", "evolution", () => new { action = "snapshot", scope = EvolutionScope(scopeKind, topic, path, noteId), refresh = false }, token);

    [McpServerTool(Name = "triage_inbox", Title = "Triage inbox", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("securitySchemes", JsonValue = SlateToolSecurity.Analyze)]
    [Authorize(Policy = "scope:slate.analyze")]
    [Description("Return bounded evidence-based inbox triage suggestions. It does not move, rename, or edit captures.")]
    public Task<McpResult<JsonElement>> TriageInbox(CancellationToken token = default) => Analyze("triage_inbox", "inbox-triage", () => new { }, token);

    private Task<McpResult<JsonElement>> Analyze(string operation, string path, Func<object> body, CancellationToken token) =>
        executor.Run<JsonElement>(operation, async libraryId => McpResult<JsonElement>.Ok(libraryId, operation, await intelligence.Post(path, body(), token)), token);

    private static string? Optional(string? value, string name, int maximum) => string.IsNullOrWhiteSpace(value) ? null : ToolInputs.Text(value, name, maximum);

    private static object Scope(string kind, string? path, Guid? noteId) => kind.Trim().ToLowerInvariant() switch
    {
        "library" => new { kind = "library" },
        "project" => new { kind = "project", path = ToolInputs.Text(path, nameof(path), 1024) },
        "note" when noteId.HasValue => new { kind = "note", noteId = noteId.Value.ToString("D") },
        _ => throw new ArgumentException("scopeKind must be library, project, or note with a noteId.")
    };

    private static object EvolutionScope(string kind, string? topic, string? path, Guid? noteId) => kind.Trim().ToLowerInvariant() switch
    {
        "topic" => new { kind = "topic", topic = ToolInputs.Text(topic, nameof(topic), 300), path },
        "project" => new { kind = "project", path = ToolInputs.Text(path, nameof(path), 1024), topic },
        "folder" => new { kind = "folder", path = ToolInputs.Text(path, nameof(path), 1024), topic },
        "note" when noteId.HasValue => new { kind = "note", noteId = noteId.Value.ToString("D"), topic },
        _ => throw new ArgumentException("scopeKind must be topic, project, folder, or note with the required value.")
    };
}

namespace Slate.Lib.Core;

public sealed record GraphNode(Guid Id, string Title, string Path, string? Type, string? Status, int Depth);
public sealed record GraphEdge(Guid Source, Guid Target);
public sealed record KnowledgeGraph(Guid Focus, IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges, bool Limited);
public sealed record RelatedNote(Guid Id, string Title, string Path, int Score, IReadOnlyList<string> Reasons);

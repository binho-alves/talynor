namespace Talynor.Api.Contracts;

public sealed record KnowledgeMapResponse(
    string Id, string Title, string Description,
    IReadOnlyList<KnowledgeNodeResponse> Nodes,
    IReadOnlyList<KnowledgeEdgeResponse> Edges);

public sealed record KnowledgeNodeResponse(
    string Id, string Title, string Description,
    int Progress, int StudyMinutes, string? ChildMapId);

public sealed record KnowledgeEdgeResponse(string Id, string SourceNodeId, string TargetNodeId);

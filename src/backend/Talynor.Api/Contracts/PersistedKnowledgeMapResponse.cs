namespace Talynor.Api.Contracts;

public sealed record PersistedKnowledgeMapResponse(Guid Id, string Title, string? Description,
    IReadOnlyList<PersistedKnowledgeNodeResponse> Nodes, IReadOnlyList<PersistedKnowledgeEdgeResponse> Edges);
public sealed record PersistedKnowledgeNodeResponse(Guid Id, string Title, string? Description,
    NodePositionResponse Position, Guid? ChildMapId);
public sealed record NodePositionResponse(double X, double Y);
public sealed record PersistedKnowledgeEdgeResponse(Guid Id, Guid SourceNodeId, Guid TargetNodeId);

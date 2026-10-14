namespace Talynor.Application.KnowledgeMaps;

public sealed record PersistedKnowledgeMap(Guid Id, string Title, string? Description,
    IReadOnlyList<PersistedKnowledgeNode> Nodes, IReadOnlyList<PersistedKnowledgeEdge> Edges);
public sealed record PersistedKnowledgeNode(Guid Id, string Title, string? Description,
    double PositionX, double PositionY, Guid? ChildMapId);
public sealed record PersistedKnowledgeEdge(Guid Id, Guid SourceNodeId, Guid TargetNodeId);

public enum KnowledgeMapReadStatus { Success, AuthenticationRequired, NotFound }
public sealed record KnowledgeMapReadResult(KnowledgeMapReadStatus Status, PersistedKnowledgeMap? Map = null);

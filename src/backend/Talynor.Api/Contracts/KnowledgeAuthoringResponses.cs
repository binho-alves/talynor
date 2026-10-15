namespace Talynor.Api.Contracts;

public sealed record KnowledgeMapAuthoringResponse(Guid Id, string Title, string? Description,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record KnowledgeNodeAuthoringResponse(Guid Id, string Title, string? Description,
    NodePositionResponse Position, Guid? ChildMapId, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record KnowledgeEdgeAuthoringResponse(Guid Id, Guid SourceNodeId, Guid TargetNodeId, DateTimeOffset CreatedAtUtc);

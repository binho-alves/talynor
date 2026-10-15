namespace Talynor.Application.KnowledgeMaps;

public enum KnowledgeWriteStatus { Success, AuthenticationRequired, NotFound, InvalidInput, InvalidChildMap, InvalidEdgeNodes, MapInUse, NodeInUse }
public enum KnowledgeWriteOperation { CreateMap, UpdateMap, DeleteMap, CreateNode, UpdateNode, DeleteNode, CreateEdge, DeleteEdge }
public sealed record KnowledgeWriteAttempt(KnowledgeWriteOperation Operation, Guid WorkspaceId, Guid MapId, Guid? ResourceId = null);
public sealed record KnowledgeWriteResult<T>(KnowledgeWriteStatus Status, T? Value = default);
public sealed record KnowledgeMapDetails(Guid Id, string Title, string? Description, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record KnowledgeNodeDetails(Guid Id, string Title, string? Description, double PositionX, double PositionY, Guid? ChildMapId, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record KnowledgeEdgeDetails(Guid Id, Guid SourceNodeId, Guid TargetNodeId, DateTimeOffset CreatedAtUtc);
public sealed record CreateMapInput(string Title, string? Description);
public sealed record UpdateMapInput(string? Title, string? Description, bool ClearDescription);
public sealed record CreateNodeInput(string Title, string? Description, double X, double Y, Guid? ChildMapId);
public sealed record NodePosition(double X, double Y);
public sealed record UpdateNodeInput(string? Title, string? Description, bool ClearDescription,
    NodePosition? Position, Guid? ChildMapId, bool ClearChildMap);
public sealed record CreateEdgeInput(Guid SourceNodeId, Guid TargetNodeId);

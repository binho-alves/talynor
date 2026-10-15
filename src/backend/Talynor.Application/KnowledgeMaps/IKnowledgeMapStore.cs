using Talynor.Domain.KnowledgeMaps;

namespace Talynor.Application.KnowledgeMaps;

public interface IKnowledgeMapStore
{
    Task<KnowledgeMap?> GetMapAsync(Guid mapId, Guid workspaceId, CancellationToken cancellationToken);
    Task<KnowledgeNode?> GetNodeAsync(Guid nodeId, Guid mapId, Guid workspaceId, CancellationToken cancellationToken);
    Task<KnowledgeEdge?> GetEdgeAsync(Guid edgeId, Guid mapId, Guid workspaceId, CancellationToken cancellationToken);
    Task<bool> MapInUseAsync(Guid mapId, Guid workspaceId, CancellationToken cancellationToken);
    Task<bool> NodeInUseAsync(Guid nodeId, Guid mapId, Guid workspaceId, CancellationToken cancellationToken);
    void AddMap(KnowledgeMap map);
    void AddNode(KnowledgeNode node);
    void AddEdge(KnowledgeEdge edge);
    void RemoveMap(KnowledgeMap map);
    void RemoveNode(KnowledgeNode node);
    void RemoveEdge(KnowledgeEdge edge);
    Task<KnowledgeWriteStatus> SaveAsync(KnowledgeWriteAttempt attempt, CancellationToken cancellationToken);
}

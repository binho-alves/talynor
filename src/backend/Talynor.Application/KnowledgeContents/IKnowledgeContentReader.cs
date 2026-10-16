namespace Talynor.Application.KnowledgeContents;

public interface IKnowledgeContentReader
{
    // Null means the scoped parent is absent; an empty collection means an existing empty node.
    Task<IReadOnlyList<KnowledgeContentSummary>?> ListForNodeAsync(Guid workspaceId, Guid mapId, Guid nodeId, CancellationToken ct);
    Task<KnowledgeContentDetail?> GetForNodeAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid contentId, CancellationToken ct);
}

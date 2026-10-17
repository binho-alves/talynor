using Talynor.Domain.KnowledgeContents;

namespace Talynor.Application.KnowledgeContents;

public interface IKnowledgeContentStore
{
    Task<bool> NodeExistsAsync(Guid workspaceId, Guid mapId, Guid nodeId, CancellationToken ct);
    Task<KnowledgeContent?> GetForNodeAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid contentId, CancellationToken ct);
    void Add(KnowledgeContent content);
    void Remove(KnowledgeContent content);
    Task<KnowledgeContentWriteStatus> SaveAsync(KnowledgeContentWriteAttempt attempt, CancellationToken ct);
}

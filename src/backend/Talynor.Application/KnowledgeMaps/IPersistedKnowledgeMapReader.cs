namespace Talynor.Application.KnowledgeMaps;

public interface IPersistedKnowledgeMapReader
{
    Task<IReadOnlyList<KnowledgeMapSummary>> ListAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<PersistedKnowledgeMap?> GetAsync(Guid mapId, Guid workspaceId, CancellationToken cancellationToken);
}

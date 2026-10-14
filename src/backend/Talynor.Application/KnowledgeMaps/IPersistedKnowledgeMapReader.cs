namespace Talynor.Application.KnowledgeMaps;

public interface IPersistedKnowledgeMapReader
{
    Task<PersistedKnowledgeMap?> GetAsync(Guid mapId, Guid workspaceId, CancellationToken cancellationToken);
}

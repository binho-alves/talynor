namespace Talynor.Application.KnowledgeMaps;

public interface IKnowledgeMapReader
{
    Task<KnowledgeMapReadModel?> GetByIdAsync(string mapId, CancellationToken cancellationToken);
}

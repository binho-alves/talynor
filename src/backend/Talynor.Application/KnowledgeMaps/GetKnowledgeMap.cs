namespace Talynor.Application.KnowledgeMaps;

public sealed class GetKnowledgeMap(IKnowledgeMapReader reader)
{
    public Task<KnowledgeMapReadModel?> ExecuteAsync(string mapId, CancellationToken cancellationToken) =>
        reader.GetByIdAsync(mapId, cancellationToken);
}

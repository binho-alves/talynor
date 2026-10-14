namespace Talynor.Domain.KnowledgeMaps;

public sealed class KnowledgeEdge
{
    private KnowledgeEdge() { }

    public Guid Id { get; private set; }
    public Guid KnowledgeMapId { get; private set; }
    public Guid SourceNodeId { get; private set; }
    public Guid TargetNodeId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static KnowledgeEdge Create(Guid knowledgeMapId, Guid sourceNodeId, Guid targetNodeId, TimeProvider timeProvider)
    {
        KnowledgeValidation.Id(knowledgeMapId, nameof(knowledgeMapId));
        KnowledgeValidation.Id(sourceNodeId, nameof(sourceNodeId));
        KnowledgeValidation.Id(targetNodeId, nameof(targetNodeId));
        ArgumentNullException.ThrowIfNull(timeProvider);
        return new() { Id = Guid.NewGuid(), KnowledgeMapId = knowledgeMapId,
            SourceNodeId = sourceNodeId, TargetNodeId = targetNodeId,
            CreatedAtUtc = timeProvider.GetUtcNow().ToUniversalTime() };
    }
}

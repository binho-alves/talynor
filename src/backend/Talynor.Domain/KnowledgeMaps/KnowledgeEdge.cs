namespace Talynor.Domain.KnowledgeMaps;

public sealed class KnowledgeEdge
{
    private KnowledgeEdge() { }

    public Guid Id { get; private set; }
    public Guid KnowledgeMapId { get; private set; }
    public Guid SourceNodeId { get; private set; }
    public Guid TargetNodeId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static KnowledgeEdge Create(Guid knowledgeMapId, Guid sourceNodeId, Guid targetNodeId, DateTimeOffset createdAtUtc)
    {
        KnowledgeValidation.Id(knowledgeMapId, nameof(knowledgeMapId));
        KnowledgeValidation.Id(sourceNodeId, nameof(sourceNodeId));
        KnowledgeValidation.Id(targetNodeId, nameof(targetNodeId));
        return new() { Id = Guid.NewGuid(), KnowledgeMapId = knowledgeMapId,
            SourceNodeId = sourceNodeId, TargetNodeId = targetNodeId,
            CreatedAtUtc = createdAtUtc.ToUniversalTime() };
    }
}

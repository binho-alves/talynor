namespace Talynor.Domain.KnowledgeMaps;

public sealed class KnowledgeNode
{
    private KnowledgeNode() { }

    public Guid Id { get; private set; }
    public Guid KnowledgeMapId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public double PositionX { get; private set; }
    public double PositionY { get; private set; }
    public Guid? ChildMapId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static KnowledgeNode Create(Guid knowledgeMapId, string title, string? description,
        double positionX, double positionY, Guid? childMapId, TimeProvider timeProvider)
    {
        KnowledgeValidation.Id(knowledgeMapId, nameof(knowledgeMapId));
        title = KnowledgeValidation.Title(title);
        KnowledgeValidation.Description(description);
        if (!double.IsFinite(positionX))
            throw new ArgumentOutOfRangeException(nameof(positionX), "Positions must be finite.");
        if (!double.IsFinite(positionY))
            throw new ArgumentOutOfRangeException(nameof(positionY), "Positions must be finite.");
        if (childMapId.HasValue) KnowledgeValidation.Id(childMapId.Value, nameof(childMapId));
        ArgumentNullException.ThrowIfNull(timeProvider);
        var now = timeProvider.GetUtcNow().ToUniversalTime();
        return new() { Id = Guid.NewGuid(), KnowledgeMapId = knowledgeMapId, Title = title,
            Description = description, PositionX = positionX, PositionY = positionY,
            ChildMapId = childMapId, CreatedAtUtc = now, UpdatedAtUtc = now };
    }
}

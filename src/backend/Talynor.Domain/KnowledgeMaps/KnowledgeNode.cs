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
        double positionX, double positionY, Guid? childMapId, DateTimeOffset createdAtUtc)
    {
        KnowledgeValidation.Id(knowledgeMapId, nameof(knowledgeMapId));
        title = KnowledgeValidation.Title(title);
        KnowledgeValidation.Description(description);
        if (!double.IsFinite(positionX))
            throw new ArgumentOutOfRangeException(nameof(positionX), "Positions must be finite.");
        if (!double.IsFinite(positionY))
            throw new ArgumentOutOfRangeException(nameof(positionY), "Positions must be finite.");
        if (childMapId.HasValue) KnowledgeValidation.Id(childMapId.Value, nameof(childMapId));
        var now = createdAtUtc.ToUniversalTime();
        return new() { Id = Guid.NewGuid(), KnowledgeMapId = knowledgeMapId, Title = title,
            Description = description, PositionX = positionX, PositionY = positionY,
            ChildMapId = childMapId, CreatedAtUtc = now, UpdatedAtUtc = now };
    }

    // Validate the complete candidate before changing any field.
    public void Update(string title, string? description, double positionX, double positionY,
        Guid? childMapId, DateTimeOffset updatedAtUtc)
    {
        title = KnowledgeValidation.Title(title);
        KnowledgeValidation.Description(description);
        if (!double.IsFinite(positionX)) throw new ArgumentOutOfRangeException(nameof(positionX), "Positions must be finite.");
        if (!double.IsFinite(positionY)) throw new ArgumentOutOfRangeException(nameof(positionY), "Positions must be finite.");
        if (childMapId.HasValue) KnowledgeValidation.Id(childMapId.Value, nameof(childMapId));
        Title = title;
        Description = description;
        PositionX = positionX;
        PositionY = positionY;
        ChildMapId = childMapId;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
    }

    public void UpdateDetails(string title, string? description, DateTimeOffset now) =>
        Update(title, description, PositionX, PositionY, ChildMapId, now);
    public void Move(double x, double y, DateTimeOffset now) => Update(Title, Description, x, y, ChildMapId, now);
    public void SetChildMap(Guid childMapId, DateTimeOffset now) => Update(Title, Description, PositionX, PositionY, childMapId, now);
    public void ClearChildMap(DateTimeOffset now) => Update(Title, Description, PositionX, PositionY, null, now);
}

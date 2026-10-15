namespace Talynor.Domain.KnowledgeMaps;

public sealed class KnowledgeMap
{
    private KnowledgeMap() { }

    public Guid Id { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static KnowledgeMap Create(Guid workspaceId, string title, string? description, DateTimeOffset createdAtUtc)
    {
        KnowledgeValidation.Id(workspaceId, nameof(workspaceId));
        title = KnowledgeValidation.Title(title);
        KnowledgeValidation.Description(description);
        var now = createdAtUtc.ToUniversalTime();
        return new() { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Title = title,
            Description = description, CreatedAtUtc = now, UpdatedAtUtc = now };
    }

    public void UpdateDetails(string title, string? description, DateTimeOffset updatedAtUtc)
    {
        title = KnowledgeValidation.Title(title);
        KnowledgeValidation.Description(description);
        Title = title;
        Description = description;
        UpdatedAtUtc = updatedAtUtc.ToUniversalTime();
    }
}

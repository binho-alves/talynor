namespace Talynor.Domain.Workspaces;

public sealed class PersonalWorkspace
{
    private PersonalWorkspace() { }

    public Guid Id { get; private set; }
    public string OwnerUserId { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static PersonalWorkspace Create(string ownerUserId, DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserId);
        return new PersonalWorkspace
        {
            Id = Guid.NewGuid(), OwnerUserId = ownerUserId,
            CreatedAtUtc = createdAtUtc.ToUniversalTime()
        };
    }
}

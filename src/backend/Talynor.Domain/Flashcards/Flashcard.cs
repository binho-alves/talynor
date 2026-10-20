namespace Talynor.Domain.Flashcards;

public sealed class Flashcard
{
    private Flashcard() { }

    public Guid Id { get; private set; }
    public Guid KnowledgeNodeId { get; private set; }
    public string Front { get; private set; } = null!;
    public string Back { get; private set; } = null!;
    public int ContentRevision { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Flashcard Create(Guid knowledgeNodeId, string front, string back, DateTimeOffset now)
    {
        if (knowledgeNodeId == Guid.Empty) throw new ArgumentException("A node ID is required.", nameof(knowledgeNodeId));
        Validate(front, back);
        return new() { Id = Guid.NewGuid(), KnowledgeNodeId = knowledgeNodeId, Front = front, Back = back,
            ContentRevision = 1, CreatedAtUtc = now.ToUniversalTime(), UpdatedAtUtc = now.ToUniversalTime() };
    }

    public bool Update(string front, string back, DateTimeOffset now)
    {
        Validate(front, back);
        if (string.Equals(Front, front, StringComparison.Ordinal) && string.Equals(Back, back, StringComparison.Ordinal))
            return false;
        // Compute before changing any field; overflow must leave the entire candidate intact.
        var revision = checked(ContentRevision + 1);
        Front = front;
        Back = back;
        ContentRevision = revision;
        UpdatedAtUtc = now.ToUniversalTime();
        return true;
    }

    private static void Validate(string front, string back)
    {
        // String.Length is UTF-16 code units. Preserve the exact submitted text.
        if (string.IsNullOrWhiteSpace(front) || front.Length > 2000)
            throw new ArgumentException("A bounded nonblank front is required.", nameof(front));
        if (string.IsNullOrWhiteSpace(back) || back.Length > 4000)
            throw new ArgumentException("A bounded nonblank back is required.", nameof(back));
    }
}

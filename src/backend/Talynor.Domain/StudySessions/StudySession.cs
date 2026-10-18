namespace Talynor.Domain.StudySessions;

public enum StudySessionStatus { Active, Completed }

public sealed class StudySession
{
    private StudySession() { }

    // Foundation construction for test fixtures only; no runtime start/complete operation yet.
    internal StudySession(Guid id, Guid knowledgeNodeId, DateTimeOffset startedAtUtc, DateTimeOffset? endedAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("A session ID is required.", nameof(id));
        if (knowledgeNodeId == Guid.Empty) throw new ArgumentException("A node ID is required.", nameof(knowledgeNodeId));
        var start = startedAtUtc.ToUniversalTime();
        var end = endedAtUtc?.ToUniversalTime();
        if (end < start) throw new ArgumentException("End must not precede start.", nameof(endedAtUtc));
        Id = id;
        KnowledgeNodeId = knowledgeNodeId;
        StartedAtUtc = start;
        EndedAtUtc = end;
    }

    public Guid Id { get; private set; }
    public Guid KnowledgeNodeId { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? EndedAtUtc { get; private set; }
    public TimeSpan? Duration => EndedAtUtc - StartedAtUtc;
    // Completed describes an ended time record only, without a learning-completion claim.
    public StudySessionStatus Status => EndedAtUtc.HasValue ? StudySessionStatus.Completed : StudySessionStatus.Active;
}

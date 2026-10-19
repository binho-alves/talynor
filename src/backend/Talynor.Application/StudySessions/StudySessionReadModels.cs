namespace Talynor.Application.StudySessions;

// EndedAtUtc means the temporal record ended; it does not measure learning completion.
public sealed record StudySessionReadModel(Guid Id, Guid KnowledgeNodeId,
    DateTimeOffset StartedAtUtc, DateTimeOffset? EndedAtUtc)
{
    public double? DurationSeconds => (EndedAtUtc - StartedAtUtc)?.TotalSeconds;
}
public sealed record StudySessionPage(IReadOnlyList<StudySessionReadModel> Sessions, bool HasMore);
public sealed record StudySessionHistory(IReadOnlyList<StudySessionReadModel> Sessions, int Page, int PageSize, bool HasMore);
public enum StudySessionReadStatus { Success, AuthenticationRequired, NotFound, Invalid }
public sealed record StudySessionListResult(StudySessionReadStatus Status, StudySessionHistory? History = null);
public sealed record StudySessionDetailResult(StudySessionReadStatus Status, StudySessionReadModel? Session = null);
public sealed record ActiveStudySession(StudySessionReadModel Session, Guid MapId, Guid NodeId, string NodeTitle);
public sealed record ActiveStudySessionResult(StudySessionReadStatus Status, ActiveStudySession? Active = null,
    DateTimeOffset ServerTimeUtc = default);

using Talynor.Application.StudySessions;

namespace Talynor.Api.Contracts;

public sealed record StudySessionResponse(Guid Id, Guid KnowledgeNodeId, DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc, double? DurationSeconds);
public sealed record StudySessionHistoryResponse(IReadOnlyList<StudySessionResponse> Sessions, int Page, int PageSize, bool HasMore);
public sealed record ActiveStudySessionContextResponse(StudySessionResponse Session, Guid MapId, Guid NodeId, string NodeTitle);
public sealed record ActiveStudySessionResponse(ActiveStudySessionContextResponse? Active, DateTimeOffset ServerTimeUtc);

internal static class StudySessionResponseMapping
{
    public static StudySessionResponse ToResponse(StudySessionReadModel session) => new(session.Id, session.KnowledgeNodeId,
        session.StartedAtUtc, session.EndedAtUtc, session.DurationSeconds);
}

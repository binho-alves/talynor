using Talynor.Domain.StudySessions;

namespace Talynor.Application.StudySessions;

public enum StudySessionWriteStatus { Success, AuthenticationRequired, NotFound, ActiveSessionExists, ClockConflict }
public sealed record StartStudySessionResult(StudySessionWriteStatus Status, StudySessionReadModel? Session = null);

// Operational failures propagate to the existing sanitized HTTP boundary, never as business conflicts.
public interface IStudySessionStore
{
    Task<StartStudySessionResult> StartAsync(Guid workspaceId, Guid mapId, StudySession session, CancellationToken ct);
    Task<StudySessionWriteStatus> CompleteAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid sessionId,
        DateTimeOffset now, CancellationToken ct);
    Task<StudySessionWriteStatus> DeleteAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid sessionId, CancellationToken ct);
}

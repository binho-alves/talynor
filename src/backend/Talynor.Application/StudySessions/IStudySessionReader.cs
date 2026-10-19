namespace Talynor.Application.StudySessions;

public interface IStudySessionReader
{
    Task<ActiveStudySession?> GetActiveAsync(Guid workspaceId, CancellationToken ct);
    // Null means an absent scoped parent; an empty page means an owned node without completed history.
    Task<StudySessionPage?> ListCompletedForNodeAsync(Guid workspaceId, Guid mapId, Guid nodeId,
        int offset, int pageSize, CancellationToken ct);
    Task<StudySessionReadModel?> GetForNodeAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid sessionId, CancellationToken ct);
}

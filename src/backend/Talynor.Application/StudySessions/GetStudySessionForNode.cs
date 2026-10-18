using Talynor.Application.Workspaces;

namespace Talynor.Application.StudySessions;

public sealed class GetStudySessionForNode(GetOrCreatePersonalWorkspace getWorkspace, IStudySessionReader reader)
{
    public async Task<StudySessionDetailResult> ExecuteAsync(Guid mapId, Guid nodeId, Guid sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(StudySessionReadStatus.AuthenticationRequired);
        var session = await reader.GetForNodeAsync(workspace.Id, mapId, nodeId, sessionId, ct);
        return session is null ? new(StudySessionReadStatus.NotFound) : new(StudySessionReadStatus.Success, session);
    }
}

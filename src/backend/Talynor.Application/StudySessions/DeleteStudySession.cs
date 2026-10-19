using Talynor.Application.Workspaces;

namespace Talynor.Application.StudySessions;

public sealed class DeleteStudySession(GetOrCreatePersonalWorkspace getWorkspace, IStudySessionStore store)
{
    public async Task<StudySessionWriteStatus> ExecuteAsync(Guid mapId, Guid nodeId, Guid sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        return workspace is null ? StudySessionWriteStatus.AuthenticationRequired :
            await store.DeleteAsync(workspace.Id, mapId, nodeId, sessionId, ct);
    }
}

using Talynor.Application.Workspaces;
using Talynor.Domain.StudySessions;

namespace Talynor.Application.StudySessions;

public sealed class StartStudySession(GetOrCreatePersonalWorkspace getWorkspace, IStudySessionStore store, TimeProvider time)
{
    public async Task<StartStudySessionResult> ExecuteAsync(Guid mapId, Guid nodeId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(StudySessionWriteStatus.AuthenticationRequired);
        if (nodeId == Guid.Empty) return new(StudySessionWriteStatus.NotFound);
        return await store.StartAsync(workspace.Id, mapId, StudySession.Start(nodeId, time.GetUtcNow()), ct);
    }
}

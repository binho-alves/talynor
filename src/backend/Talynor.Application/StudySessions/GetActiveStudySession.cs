using Talynor.Application.Workspaces;

namespace Talynor.Application.StudySessions;

public sealed class GetActiveStudySession(GetOrCreatePersonalWorkspace getWorkspace, IStudySessionReader reader, TimeProvider time)
{
    public async Task<ActiveStudySessionResult> ExecuteAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(StudySessionReadStatus.AuthenticationRequired);
        var active = await reader.GetActiveAsync(workspace.Id, ct);
        return new(StudySessionReadStatus.Success, active, time.GetUtcNow().ToUniversalTime());
    }
}

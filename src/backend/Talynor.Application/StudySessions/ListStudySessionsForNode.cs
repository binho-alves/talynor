using Talynor.Application.Workspaces;

namespace Talynor.Application.StudySessions;

public sealed class ListStudySessionsForNode(GetOrCreatePersonalWorkspace getWorkspace, IStudySessionReader reader)
{
    public async Task<StudySessionListResult> ExecuteAsync(Guid mapId, Guid nodeId, int page, int pageSize, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(StudySessionReadStatus.AuthenticationRequired);
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return new(StudySessionReadStatus.Invalid);
        var result = await reader.ListCompletedForNodeAsync(workspace.Id, mapId, nodeId, (page - 1) * pageSize, pageSize, ct);
        return result is null ? new(StudySessionReadStatus.NotFound) :
            new(StudySessionReadStatus.Success, new(result.Sessions, page, pageSize, result.HasMore));
    }
}

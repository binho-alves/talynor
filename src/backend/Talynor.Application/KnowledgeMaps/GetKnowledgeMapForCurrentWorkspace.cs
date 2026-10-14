using Talynor.Application.Workspaces;

namespace Talynor.Application.KnowledgeMaps;

public sealed class GetKnowledgeMapForCurrentWorkspace(GetOrCreatePersonalWorkspace getWorkspace,
    IPersistedKnowledgeMapReader maps)
{
    public async Task<KnowledgeMapReadResult> ExecuteAsync(Guid mapId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Reuse the current-user/account check and approved lazy workspace resolution.
        var workspace = await getWorkspace.ExecuteAsync(cancellationToken);
        if (workspace is null) return new(KnowledgeMapReadStatus.AuthenticationRequired);
        var map = await maps.GetAsync(mapId, workspace.Id, cancellationToken);
        return map is null ? new(KnowledgeMapReadStatus.NotFound) : new(KnowledgeMapReadStatus.Success, map);
    }
}

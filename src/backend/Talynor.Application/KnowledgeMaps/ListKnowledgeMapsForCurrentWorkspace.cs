using Talynor.Application.Workspaces;

namespace Talynor.Application.KnowledgeMaps;

public sealed class ListKnowledgeMapsForCurrentWorkspace(GetOrCreatePersonalWorkspace getWorkspace,
    IPersistedKnowledgeMapReader maps)
{
    public async Task<KnowledgeMapListResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(cancellationToken);
        if (workspace is null) return new(KnowledgeMapListStatus.AuthenticationRequired);
        return new(KnowledgeMapListStatus.Success, await maps.ListAsync(workspace.Id, cancellationToken));
    }
}

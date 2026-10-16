using Talynor.Application.Workspaces;

namespace Talynor.Application.KnowledgeContents;

public sealed class ListKnowledgeContentsForNode(GetOrCreatePersonalWorkspace getWorkspace, IKnowledgeContentReader reader)
{
    public async Task<KnowledgeContentListResult> ExecuteAsync(Guid mapId, Guid nodeId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(KnowledgeContentReadStatus.AuthenticationRequired);
        var contents = await reader.ListForNodeAsync(workspace.Id, mapId, nodeId, ct);
        return contents is null ? new(KnowledgeContentReadStatus.NotFound) : new(KnowledgeContentReadStatus.Success, contents);
    }
}

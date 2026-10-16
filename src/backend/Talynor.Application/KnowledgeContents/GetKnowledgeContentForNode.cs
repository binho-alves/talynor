using Talynor.Application.Workspaces;

namespace Talynor.Application.KnowledgeContents;

public sealed class GetKnowledgeContentForNode(GetOrCreatePersonalWorkspace getWorkspace, IKnowledgeContentReader reader)
{
    public async Task<KnowledgeContentDetailResult> ExecuteAsync(Guid mapId, Guid nodeId, Guid contentId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(KnowledgeContentReadStatus.AuthenticationRequired);
        var content = await reader.GetForNodeAsync(workspace.Id, mapId, nodeId, contentId, ct);
        return content is null ? new(KnowledgeContentReadStatus.NotFound) : new(KnowledgeContentReadStatus.Success, content);
    }
}

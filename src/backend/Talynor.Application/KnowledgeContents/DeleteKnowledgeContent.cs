using Talynor.Application.Workspaces;

namespace Talynor.Application.KnowledgeContents;

public sealed class DeleteKnowledgeContent(GetOrCreatePersonalWorkspace getWorkspace, IKnowledgeContentStore store)
{
    public async Task<KnowledgeContentWriteStatus> ExecuteAsync(Guid mapId, Guid nodeId, Guid contentId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return KnowledgeContentWriteStatus.AuthenticationRequired;
        var content = await store.GetForNodeAsync(workspace.Id, mapId, nodeId, contentId, ct);
        if (content is null) return KnowledgeContentWriteStatus.NotFound;
        store.Remove(content);
        return await store.SaveAsync(new(KnowledgeContentWriteOperation.Delete, workspace.Id, mapId, nodeId, contentId), ct);
    }
}

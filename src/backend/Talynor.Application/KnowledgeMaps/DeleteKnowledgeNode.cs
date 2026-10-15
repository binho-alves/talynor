using Talynor.Application.Workspaces;

namespace Talynor.Application.KnowledgeMaps;

public sealed class DeleteKnowledgeNode(GetOrCreatePersonalWorkspace getWorkspace, IKnowledgeMapStore store)
{
    public async Task<KnowledgeWriteStatus> ExecuteAsync(Guid mapId, Guid nodeId, CancellationToken ct)
    {
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return KnowledgeWriteStatus.AuthenticationRequired;
        var resource = await store.GetNodeAsync(nodeId, mapId, workspace.Id, ct);
        if (resource is null) return KnowledgeWriteStatus.NotFound;
        if (await store.NodeInUseAsync(nodeId, mapId, workspace.Id, ct)) return KnowledgeWriteStatus.NodeInUse;
        store.RemoveNode(resource);
        return await store.SaveAsync(new(KnowledgeWriteOperation.DeleteNode, workspace.Id, mapId, nodeId), ct);
    }
}

using Talynor.Application.Workspaces;

namespace Talynor.Application.KnowledgeMaps;

public sealed class DeleteKnowledgeEdge(GetOrCreatePersonalWorkspace getWorkspace, IKnowledgeMapStore store)
{
    public async Task<KnowledgeWriteStatus> ExecuteAsync(Guid mapId, Guid edgeId, CancellationToken ct)
    {
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return KnowledgeWriteStatus.AuthenticationRequired;
        var resource = await store.GetEdgeAsync(edgeId, mapId, workspace.Id, ct);
        if (resource is null) return KnowledgeWriteStatus.NotFound;
        store.RemoveEdge(resource);
        return await store.SaveAsync(new(KnowledgeWriteOperation.DeleteEdge, workspace.Id, mapId, edgeId), ct);
    }
}

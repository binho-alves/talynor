using Talynor.Application.Workspaces;

namespace Talynor.Application.KnowledgeMaps;

public sealed class DeleteKnowledgeMap(GetOrCreatePersonalWorkspace getWorkspace, IKnowledgeMapStore store)
{
    public async Task<KnowledgeWriteStatus> ExecuteAsync(Guid mapId, CancellationToken ct)
    {
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return KnowledgeWriteStatus.AuthenticationRequired;
        var resource = await store.GetMapAsync(mapId, workspace.Id, ct);
        if (resource is null) return KnowledgeWriteStatus.NotFound;
        if (await store.MapInUseAsync(mapId, workspace.Id, ct)) return KnowledgeWriteStatus.MapInUse;
        store.RemoveMap(resource);
        return await store.SaveAsync(new(KnowledgeWriteOperation.DeleteMap, workspace.Id, mapId), ct);
    }
}

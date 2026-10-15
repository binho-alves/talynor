using Talynor.Application.Workspaces;

namespace Talynor.Application.KnowledgeMaps;

public sealed class UpdateKnowledgeMap(GetOrCreatePersonalWorkspace getWorkspace, IKnowledgeMapStore store, TimeProvider time)
{
    public async Task<KnowledgeWriteStatus> ExecuteAsync(Guid mapId, UpdateMapInput input, CancellationToken ct)
    {
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return KnowledgeWriteStatus.AuthenticationRequired;
        var map = await store.GetMapAsync(mapId, workspace.Id, ct);
        if (map is null) return KnowledgeWriteStatus.NotFound;
        if (input.ClearDescription && input.Description is not null ||
            input.Title is null && input.Description is null && !input.ClearDescription) return KnowledgeWriteStatus.InvalidInput;
        try { map.UpdateDetails(input.Title ?? map.Title, input.ClearDescription ? null : input.Description ?? map.Description, time.GetUtcNow()); }
        catch (ArgumentException) { return KnowledgeWriteStatus.InvalidInput; }
        return await store.SaveAsync(new(KnowledgeWriteOperation.UpdateMap, workspace.Id, map.Id), ct);
    }
}

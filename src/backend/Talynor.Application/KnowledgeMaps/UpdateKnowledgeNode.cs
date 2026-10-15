using Talynor.Application.Workspaces;

namespace Talynor.Application.KnowledgeMaps;

public sealed class UpdateKnowledgeNode(GetOrCreatePersonalWorkspace getWorkspace, IKnowledgeMapStore store, TimeProvider time)
{
    public async Task<KnowledgeWriteStatus> ExecuteAsync(Guid mapId, Guid nodeId, UpdateNodeInput input, CancellationToken ct)
    {
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return KnowledgeWriteStatus.AuthenticationRequired;
        var node = await store.GetNodeAsync(nodeId, mapId, workspace.Id, ct);
        if (node is null) return KnowledgeWriteStatus.NotFound;
        if (input.ClearDescription && input.Description is not null || input.ClearChildMap && input.ChildMapId.HasValue ||
            input.Title is null && input.Description is null && !input.ClearDescription && input.Position is null &&
            !input.ChildMapId.HasValue && !input.ClearChildMap) return KnowledgeWriteStatus.InvalidInput;
        if (input.ChildMapId == Guid.Empty) return KnowledgeWriteStatus.InvalidInput;
        var child = input.ClearChildMap ? null : input.ChildMapId ?? node.ChildMapId;
        if (child.HasValue && await store.GetMapAsync(child.Value, workspace.Id, ct) is null)
            return KnowledgeWriteStatus.InvalidChildMap;
        try { node.Update(input.Title ?? node.Title, input.ClearDescription ? null : input.Description ?? node.Description,
            input.Position?.X ?? node.PositionX, input.Position?.Y ?? node.PositionY, child, time.GetUtcNow()); }
        catch (ArgumentException) { return KnowledgeWriteStatus.InvalidInput; }
        return await store.SaveAsync(new(KnowledgeWriteOperation.UpdateNode, workspace.Id, mapId, node.Id), ct);
    }
}

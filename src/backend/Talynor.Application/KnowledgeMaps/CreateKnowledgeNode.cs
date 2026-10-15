using Talynor.Application.Workspaces;
using Talynor.Domain.KnowledgeMaps;

namespace Talynor.Application.KnowledgeMaps;

public sealed class CreateKnowledgeNode(GetOrCreatePersonalWorkspace getWorkspace, IKnowledgeMapStore store, TimeProvider time)
{
    public async Task<KnowledgeWriteResult<KnowledgeNodeDetails>> ExecuteAsync(Guid mapId, CreateNodeInput input, CancellationToken ct)
    {
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(KnowledgeWriteStatus.AuthenticationRequired);
        if (await store.GetMapAsync(mapId, workspace.Id, ct) is null) return new(KnowledgeWriteStatus.NotFound);
        KnowledgeNode node;
        try { node = KnowledgeNode.Create(mapId, input.Title, input.Description, input.X, input.Y, input.ChildMapId, time.GetUtcNow()); }
        catch (ArgumentException) { return new(KnowledgeWriteStatus.InvalidInput); }
        if (input.ChildMapId.HasValue && await store.GetMapAsync(input.ChildMapId.Value, workspace.Id, ct) is null)
            return new(KnowledgeWriteStatus.InvalidChildMap);
        store.AddNode(node);
        var status = await store.SaveAsync(new(KnowledgeWriteOperation.CreateNode, workspace.Id, mapId, node.Id), ct);
        return new(status, status == KnowledgeWriteStatus.Success ? new(node.Id, node.Title, node.Description,
            node.PositionX, node.PositionY, node.ChildMapId, node.CreatedAtUtc, node.UpdatedAtUtc) : null);
    }
}

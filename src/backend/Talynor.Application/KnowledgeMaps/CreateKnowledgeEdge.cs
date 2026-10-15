using Talynor.Application.Workspaces;
using Talynor.Domain.KnowledgeMaps;

namespace Talynor.Application.KnowledgeMaps;

public sealed class CreateKnowledgeEdge(GetOrCreatePersonalWorkspace getWorkspace, IKnowledgeMapStore store, TimeProvider time)
{
    public async Task<KnowledgeWriteResult<KnowledgeEdgeDetails>> ExecuteAsync(Guid mapId, CreateEdgeInput input, CancellationToken ct)
    {
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(KnowledgeWriteStatus.AuthenticationRequired);
        if (await store.GetMapAsync(mapId, workspace.Id, ct) is null) return new(KnowledgeWriteStatus.NotFound);
        KnowledgeEdge edge;
        try { edge = KnowledgeEdge.Create(mapId, input.SourceNodeId, input.TargetNodeId, time.GetUtcNow()); }
        catch (ArgumentException) { return new(KnowledgeWriteStatus.InvalidInput); }
        if (await store.GetNodeAsync(input.SourceNodeId, mapId, workspace.Id, ct) is null ||
            await store.GetNodeAsync(input.TargetNodeId, mapId, workspace.Id, ct) is null)
            return new(KnowledgeWriteStatus.InvalidEdgeNodes);
        store.AddEdge(edge);
        var status = await store.SaveAsync(new(KnowledgeWriteOperation.CreateEdge, workspace.Id, mapId, edge.Id), ct);
        return new(status, status == KnowledgeWriteStatus.Success ? new(edge.Id, edge.SourceNodeId, edge.TargetNodeId, edge.CreatedAtUtc) : null);
    }
}

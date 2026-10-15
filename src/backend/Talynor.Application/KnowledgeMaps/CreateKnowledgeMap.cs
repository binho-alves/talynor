using Talynor.Application.Workspaces;
using Talynor.Domain.KnowledgeMaps;

namespace Talynor.Application.KnowledgeMaps;

public sealed class CreateKnowledgeMap(GetOrCreatePersonalWorkspace getWorkspace, IKnowledgeMapStore store, TimeProvider time)
{
    public async Task<KnowledgeWriteResult<KnowledgeMapDetails>> ExecuteAsync(CreateMapInput input, CancellationToken ct)
    {
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(KnowledgeWriteStatus.AuthenticationRequired);
        KnowledgeMap map;
        try { map = KnowledgeMap.Create(workspace.Id, input.Title, input.Description, time.GetUtcNow()); }
        catch (ArgumentException) { return new(KnowledgeWriteStatus.InvalidInput); }
        store.AddMap(map);
        var status = await store.SaveAsync(new(KnowledgeWriteOperation.CreateMap, workspace.Id, map.Id), ct);
        return new(status, status == KnowledgeWriteStatus.Success ? new(map.Id, map.Title, map.Description, map.CreatedAtUtc, map.UpdatedAtUtc) : null);
    }
}

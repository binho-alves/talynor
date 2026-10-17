using Talynor.Application.Workspaces;
using Talynor.Domain.KnowledgeContents;

namespace Talynor.Application.KnowledgeContents;

public sealed class CreateKnowledgeContent(GetOrCreatePersonalWorkspace getWorkspace, IKnowledgeContentStore store, TimeProvider time)
{
    public async Task<CreateKnowledgeContentResult> ExecuteAsync(Guid mapId, Guid nodeId, CreateKnowledgeContentInput input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(KnowledgeContentWriteStatus.AuthenticationRequired);
        if (!await store.NodeExistsAsync(workspace.Id, mapId, nodeId, ct)) return new(KnowledgeContentWriteStatus.NotFound);
        var type = input.Type switch { "note" => KnowledgeContentType.Note, "link" => KnowledgeContentType.Link, _ => (KnowledgeContentType?)null };
        if (type is null || type == KnowledgeContentType.Note && input.UrlSpecified ||
            type == KnowledgeContentType.Link && input.BodySpecified) return new(KnowledgeContentWriteStatus.InvalidInput);
        KnowledgeContent content;
        try { content = KnowledgeContent.Create(nodeId, type.Value, input.Title, input.Body, input.Url, time.GetUtcNow()); }
        catch (ArgumentException) { return new(KnowledgeContentWriteStatus.InvalidInput); }
        store.Add(content);
        var status = await store.SaveAsync(new(KnowledgeContentWriteOperation.Create, workspace.Id, mapId, nodeId, content.Id), ct);
        return new(status, status == KnowledgeContentWriteStatus.Success ? new(content.Id, content.Type, content.Title,
            content.Body, content.Url, content.CreatedAtUtc, content.UpdatedAtUtc) : null);
    }
}

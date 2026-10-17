using Talynor.Application.Workspaces;
using Talynor.Domain.KnowledgeContents;

namespace Talynor.Application.KnowledgeContents;

public sealed class UpdateKnowledgeContent(GetOrCreatePersonalWorkspace getWorkspace, IKnowledgeContentStore store, TimeProvider time)
{
    public async Task<KnowledgeContentWriteStatus> ExecuteAsync(Guid mapId, Guid nodeId, Guid contentId,
        UpdateKnowledgeContentInput input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return KnowledgeContentWriteStatus.AuthenticationRequired;
        var content = await store.GetForNodeAsync(workspace.Id, mapId, nodeId, contentId, ct);
        if (content is null) return KnowledgeContentWriteStatus.NotFound;
        if (input.ClearTitle && input.Title is not null ||
            content.Type == KnowledgeContentType.Note && (input.UrlSpecified || input.Url is not null) ||
            content.Type == KnowledgeContentType.Link && (input.BodySpecified || input.Body is not null))
            return KnowledgeContentWriteStatus.InvalidInput;
        try
        {
            if (!content.UpdateDetails(input.ClearTitle ? null : input.Title ?? content.Title,
                input.Body ?? content.Body, input.Url ?? content.Url, time.GetUtcNow()))
                return KnowledgeContentWriteStatus.InvalidInput;
        }
        catch (ArgumentException) { return KnowledgeContentWriteStatus.InvalidInput; }
        return await store.SaveAsync(new(KnowledgeContentWriteOperation.Update, workspace.Id, mapId, nodeId, contentId), ct);
    }
}

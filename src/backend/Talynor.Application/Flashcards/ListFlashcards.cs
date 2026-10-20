using Talynor.Application.Workspaces;

namespace Talynor.Application.Flashcards;

public sealed class ListFlashcards(GetOrCreatePersonalWorkspace getWorkspace, IFlashcardReader reader)
{
    public async Task<FlashcardListResult> ExecuteAsync(Guid mapId, Guid nodeId, int page, int pageSize, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(FlashcardStatus.AuthenticationRequired);
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return new(FlashcardStatus.InvalidInput);
        var result = await reader.ListAsync(workspace.Id, mapId, nodeId, (page - 1) * pageSize, pageSize, ct);
        return result is null ? new(FlashcardStatus.NotFound) :
            new(FlashcardStatus.Success, new(result.Flashcards, page, pageSize, result.HasMore));
    }
}

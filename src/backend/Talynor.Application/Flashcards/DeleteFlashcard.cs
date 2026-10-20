using Talynor.Application.Workspaces;

namespace Talynor.Application.Flashcards;

public sealed class DeleteFlashcard(GetOrCreatePersonalWorkspace getWorkspace, IFlashcardStore store)
{
    public async Task<FlashcardStatus> ExecuteAsync(Guid mapId, Guid nodeId, Guid cardId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        return workspace is null ? FlashcardStatus.AuthenticationRequired :
            await store.DeleteAsync(workspace.Id, mapId, nodeId, cardId, ct);
    }
}

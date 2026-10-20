using Talynor.Application.Workspaces;

namespace Talynor.Application.Flashcards;

public sealed class UpdateFlashcard(GetOrCreatePersonalWorkspace getWorkspace, IFlashcardStore store, TimeProvider time)
{
    public async Task<FlashcardStatus> ExecuteAsync(Guid mapId, Guid nodeId, Guid cardId, UpdateFlashcardInput input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return FlashcardStatus.AuthenticationRequired;
        return await store.UpdateAsync(workspace.Id, mapId, nodeId, cardId, input, time.GetUtcNow(), ct);
    }
}

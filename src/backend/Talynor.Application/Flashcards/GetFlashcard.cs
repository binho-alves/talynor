using Talynor.Application.Workspaces;

namespace Talynor.Application.Flashcards;

public sealed class GetFlashcard(GetOrCreatePersonalWorkspace getWorkspace, IFlashcardReader reader)
{
    public async Task<FlashcardResult> ExecuteAsync(Guid mapId, Guid nodeId, Guid cardId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(FlashcardStatus.AuthenticationRequired);
        var card = await reader.GetAsync(workspace.Id, mapId, nodeId, cardId, ct);
        return card is null ? new(FlashcardStatus.NotFound) : new(FlashcardStatus.Success, card);
    }
}

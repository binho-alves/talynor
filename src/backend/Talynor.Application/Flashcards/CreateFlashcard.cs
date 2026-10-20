using Talynor.Application.Workspaces;
using Talynor.Domain.Flashcards;

namespace Talynor.Application.Flashcards;

public sealed class CreateFlashcard(GetOrCreatePersonalWorkspace getWorkspace, IFlashcardStore store, TimeProvider time)
{
    public async Task<FlashcardResult> ExecuteAsync(Guid mapId, Guid nodeId, CreateFlashcardInput input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(FlashcardStatus.AuthenticationRequired);
        if (!await store.NodeExistsAsync(workspace.Id, mapId, nodeId, ct)) return new(FlashcardStatus.NotFound);
        Flashcard card;
        try { card = Flashcard.Create(nodeId, input.Front, input.Back, time.GetUtcNow()); }
        catch (ArgumentException) { return new(FlashcardStatus.InvalidInput); }
        var status = await store.CreateAsync(workspace.Id, mapId, card, ct);
        return new(status, status == FlashcardStatus.Success ? FlashcardDetail.From(card) : null);
    }
}

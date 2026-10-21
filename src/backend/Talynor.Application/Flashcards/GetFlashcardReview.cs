using Talynor.Application.Workspaces;

namespace Talynor.Application.Flashcards;

public sealed class GetFlashcardReview(GetOrCreatePersonalWorkspace getWorkspace, IFlashcardStudyReader reader)
{
    public async Task<FlashcardReviewOperationResult> ExecuteAsync(Guid mapId, Guid nodeId, Guid cardId,
        Guid reviewId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (cardId == Guid.Empty || reviewId == Guid.Empty) return new(FlashcardStatus.InvalidInput);
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(FlashcardStatus.AuthenticationRequired);
        var result = await reader.GetReviewAsync(workspace.Id, mapId, nodeId, cardId, reviewId, ct);
        return result is null ? new(FlashcardStatus.NotFound) : new(FlashcardStatus.Success, result);
    }
}

using Talynor.Domain.Flashcards;

namespace Talynor.Application.Flashcards;

public interface IFlashcardStudyStore
{
    Task<FlashcardReviewOperationResult> ReviewAsync(Guid workspaceId, Guid mapId, Guid nodeId,
        Guid cardId, Guid reviewId, int contentRevision, FlashcardAnswerResult result, CancellationToken ct);
}

namespace Talynor.Application.Flashcards;

public interface IFlashcardReader
{
    Task<FlashcardPage?> ListAsync(Guid workspaceId, Guid mapId, Guid nodeId, int offset, int pageSize, CancellationToken ct);
    Task<FlashcardDetail?> GetAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid cardId, CancellationToken ct);
}

public interface IFlashcardStudyReader
{
    Task<FlashcardStudyNextRead> GetNextForStudyAsync(Guid workspaceId, Guid mapId, Guid nodeId, CancellationToken ct);
    Task<FlashcardRevealRead?> GetRevealAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid cardId,
        int contentRevision, CancellationToken ct);
    Task<FlashcardReviewView?> GetReviewAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid cardId,
        Guid reviewId, CancellationToken ct);
}

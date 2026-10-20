using Talynor.Domain.Flashcards;

namespace Talynor.Application.Flashcards;

public interface IFlashcardStore
{
    Task<bool> NodeExistsAsync(Guid workspaceId, Guid mapId, Guid nodeId, CancellationToken ct);
    Task<FlashcardStatus> CreateAsync(Guid workspaceId, Guid mapId, Flashcard card, CancellationToken ct);
    // The official writer reads and increments the revision inside one SQLite write transaction.
    Task<FlashcardStatus> UpdateAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid cardId,
        UpdateFlashcardInput input, DateTimeOffset now, CancellationToken ct);
    Task<FlashcardStatus> DeleteAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid cardId, CancellationToken ct);
}

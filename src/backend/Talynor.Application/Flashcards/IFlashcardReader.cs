namespace Talynor.Application.Flashcards;

public interface IFlashcardReader
{
    Task<FlashcardPage?> ListAsync(Guid workspaceId, Guid mapId, Guid nodeId, int offset, int pageSize, CancellationToken ct);
    Task<FlashcardDetail?> GetAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid cardId, CancellationToken ct);
}

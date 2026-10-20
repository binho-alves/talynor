using Microsoft.EntityFrameworkCore;
using Talynor.Application.Flashcards;
using Talynor.Infrastructure.Persistence;

namespace Talynor.Infrastructure.Flashcards;

public sealed class FlashcardReader(TalynorDbContext db) : IFlashcardReader
{
    public async Task<FlashcardPage?> ListAsync(Guid workspaceId, Guid mapId, Guid nodeId, int offset, int pageSize, CancellationToken ct)
    {
        // Parent validation and page share a read snapshot, not a snapshot across requests.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await db.KnowledgeNodes.AsNoTracking().AnyAsync(node => node.Id == nodeId && node.KnowledgeMapId == mapId &&
            db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId), ct)) return null;
        var cards = await Scoped(workspaceId, mapId, nodeId).OrderBy(card => card.CreatedAtUtc).ThenBy(card => card.Id)
            .Skip(offset).Take(pageSize + 1).Select(card => new FlashcardDetail(card.Id, card.KnowledgeNodeId,
                card.Front, card.Back, card.ContentRevision, card.CreatedAtUtc, card.UpdatedAtUtc)).ToArrayAsync(ct);
        await transaction.CommitAsync(ct);
        return new(Array.AsReadOnly(cards.Take(pageSize).ToArray()), cards.Length > pageSize);
    }

    public Task<FlashcardDetail?> GetAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid cardId, CancellationToken ct) =>
        Scoped(workspaceId, mapId, nodeId).Where(card => card.Id == cardId)
            .Select(card => new FlashcardDetail(card.Id, card.KnowledgeNodeId, card.Front, card.Back,
                card.ContentRevision, card.CreatedAtUtc, card.UpdatedAtUtc)).SingleOrDefaultAsync(ct);

    private IQueryable<Domain.Flashcards.Flashcard> Scoped(Guid workspaceId, Guid mapId, Guid nodeId) =>
        db.Flashcards.AsNoTracking().Where(card => card.KnowledgeNodeId == nodeId &&
            db.KnowledgeNodes.Any(node => node.Id == nodeId && node.KnowledgeMapId == mapId &&
                db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId)));
}

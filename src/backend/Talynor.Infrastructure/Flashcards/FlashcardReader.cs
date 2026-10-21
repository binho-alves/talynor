using Microsoft.EntityFrameworkCore;
using Talynor.Application.Flashcards;
using Talynor.Infrastructure.Persistence;

namespace Talynor.Infrastructure.Flashcards;

public sealed class FlashcardReader(TalynorDbContext db) : IFlashcardReader, IFlashcardStudyReader
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

    public async Task<FlashcardStudyNextRead> GetNextForStudyAsync(Guid workspaceId, Guid mapId, Guid nodeId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var nodeExists = await db.KnowledgeNodes.AsNoTracking().AnyAsync(node => node.Id == nodeId &&
            node.KnowledgeMapId == mapId && db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId), ct);
        if (!nodeExists) return new(false, null);

        var item = await (
            from card in ScopedCards(workspaceId, mapId, nodeId)
            join stateValue in db.FlashcardReviewStates.AsNoTracking() on card.Id equals stateValue.FlashcardId into states
            from state in states.DefaultIfEmpty()
            orderby state == null ? 0 : 1,
                state == null ? card.CreatedAtUtc : state.LastReviewedAtUtc,
                card.CreatedAtUtc,
                card.Id
            select new FlashcardStudyNextItem(card.Id, card.Front, card.ContentRevision,
                state == null ? 0 : state.ReviewCount,
                state == null ? 0 : state.CorrectCount,
                state == null ? (DateTimeOffset?)null : state.LastReviewedAtUtc)
        ).FirstOrDefaultAsync(ct);

        await transaction.CommitAsync(ct);
        return new(true, item);
    }

    public Task<FlashcardRevealRead?> GetRevealAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid cardId,
        int contentRevision, CancellationToken ct) =>
        ScopedCards(workspaceId, mapId, nodeId).Where(card => card.Id == cardId)
            .Select(card => new FlashcardRevealRead(card.ContentRevision,
                card.ContentRevision == contentRevision ? card.Back : null)).SingleOrDefaultAsync(ct);

    public Task<FlashcardReviewView?> GetReviewAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid cardId,
        Guid reviewId, CancellationToken ct) =>
        (from review in db.FlashcardReviews.AsNoTracking()
         join card in ScopedCards(workspaceId, mapId, nodeId) on review.FlashcardId equals card.Id
         join stateValue in db.FlashcardReviewStates.AsNoTracking() on card.Id equals stateValue.FlashcardId into states
         from state in states.DefaultIfEmpty()
         where card.Id == cardId && review.ReviewId == reviewId
         select new FlashcardReviewView(
             new FlashcardReviewDetail(review.ReviewId, review.FlashcardId, review.ContentRevision, review.Result, review.ReviewedAtUtc),
             new FlashcardReviewStateView(card.ContentRevision,
                 state == null ? 0 : state.ReviewCount,
                 state == null ? 0 : state.CorrectCount,
                  state == null ? (DateTimeOffset?)null : state.LastReviewedAtUtc))
        ).SingleOrDefaultAsync(ct);

    private IQueryable<Domain.Flashcards.Flashcard> Scoped(Guid workspaceId, Guid mapId, Guid nodeId) =>
        db.Flashcards.AsNoTracking().Where(card => card.KnowledgeNodeId == nodeId &&
            db.KnowledgeNodes.Any(node => node.Id == nodeId && node.KnowledgeMapId == mapId &&
                db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId)));

    private IQueryable<Domain.Flashcards.Flashcard> ScopedCards(Guid workspaceId, Guid mapId, Guid nodeId) =>
        db.Flashcards.AsNoTracking().Where(card => card.KnowledgeNodeId == nodeId &&
            db.KnowledgeNodes.Any(node => node.Id == nodeId && node.KnowledgeMapId == mapId &&
                db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId)));
}

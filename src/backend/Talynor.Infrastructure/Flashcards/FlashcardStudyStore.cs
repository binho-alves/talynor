using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Talynor.Application.Flashcards;
using Talynor.Domain.Flashcards;
using Talynor.Infrastructure.Persistence;

namespace Talynor.Infrastructure.Flashcards;

public sealed class FlashcardStudyStore(TalynorDbContext db, TimeProvider time) : IFlashcardStudyStore
{
    public async Task<FlashcardReviewOperationResult> ReviewAsync(Guid workspaceId, Guid mapId, Guid nodeId,
        Guid cardId, Guid reviewId, int contentRevision, FlashcardAnswerResult result, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await db.Database.OpenConnectionAsync(ct);
        Flashcard? card = null;
        FlashcardReview? review = null;
        FlashcardReviewState? state = null;
        try
        {
            // Serialize with edits/resets before ownership, event, revision, or state reads.
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            await using var transaction = connection.BeginTransaction(deferred: false);
            ct.ThrowIfCancellationRequested();
            await using var enlisted = await db.Database.UseTransactionAsync(transaction, ct);

            card = await ScopedCards(workspaceId, mapId, nodeId, cardId).SingleOrDefaultAsync(ct);
            if (card is null) return new(FlashcardStatus.NotFound);

            // Check the immutable event before current revision so an old committed request
            // remains reconcilable after a material edit reset its current state.
            review = await db.FlashcardReviews.SingleOrDefaultAsync(value => value.ReviewId == reviewId, ct);
            if (review is not null)
            {
                if (review.FlashcardId != cardId || review.ContentRevision != contentRevision || review.Result != result)
                    return new(FlashcardStatus.ReviewIdConflict);
                state = await db.FlashcardReviewStates.SingleOrDefaultAsync(value => value.FlashcardId == cardId, ct);
                var replay = ToView(review, card, state);
                await transaction.CommitAsync(ct);
                return new(FlashcardStatus.Success, replay);
            }

            if (card.ContentRevision != contentRevision) return new(FlashcardStatus.ContentChanged);

            review = FlashcardReview.Create(reviewId, cardId, contentRevision, result, time.GetUtcNow());
            state = await db.FlashcardReviewStates.SingleOrDefaultAsync(value => value.FlashcardId == cardId, ct);
            if (state is null)
            {
                state = FlashcardReviewState.FromFirstReview(review);
                db.FlashcardReviewStates.Add(state);
            }
            else
            {
                state.Record(review);
            }
            db.FlashcardReviews.Add(review);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(FlashcardStatus.Success, ToView(review, card, state));
        }
        finally
        {
            if (card is not null) db.Entry(card).State = EntityState.Detached;
            if (review is not null) db.Entry(review).State = EntityState.Detached;
            if (state is not null) db.Entry(state).State = EntityState.Detached;
            await db.Database.CloseConnectionAsync();
        }
    }

    private IQueryable<Flashcard> ScopedCards(Guid workspaceId, Guid mapId, Guid nodeId, Guid cardId) =>
        db.Flashcards.Where(card => card.Id == cardId && card.KnowledgeNodeId == nodeId &&
            db.KnowledgeNodes.Any(node => node.Id == nodeId && node.KnowledgeMapId == mapId &&
                db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId)));

    private static FlashcardReviewView ToView(FlashcardReview review, Flashcard card, FlashcardReviewState? state) =>
        new(new(review.ReviewId, review.FlashcardId, review.ContentRevision, review.Result, review.ReviewedAtUtc),
            new(card.ContentRevision, state?.ReviewCount ?? 0, state?.CorrectCount ?? 0, state?.LastReviewedAtUtc));
}

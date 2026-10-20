using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Talynor.Application.Flashcards;
using Talynor.Domain.Flashcards;
using Talynor.Infrastructure.Persistence;

namespace Talynor.Infrastructure.Flashcards;

public sealed class FlashcardStore(TalynorDbContext db) : IFlashcardStore
{
    public Task<bool> NodeExistsAsync(Guid workspaceId, Guid mapId, Guid nodeId, CancellationToken ct) =>
        db.KnowledgeNodes.AsNoTracking().AnyAsync(node => node.Id == nodeId && node.KnowledgeMapId == mapId &&
            db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId), ct);

    public async Task<FlashcardStatus> CreateAsync(Guid workspaceId, Guid mapId, Flashcard card, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!await NodeExistsAsync(workspaceId, mapId, card.KnowledgeNodeId, ct)) return FlashcardStatus.NotFound;
        db.Flashcards.Add(card);
        try
        {
            await db.SaveChangesAsync(ct);
            return FlashcardStatus.Success;
        }
        catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteExtendedErrorCode: 787 })
        {
            if (!await NodeExistsAsync(workspaceId, mapId, card.KnowledgeNodeId, ct)) return FlashcardStatus.NotFound;
            throw;
        }
        finally { db.Entry(card).State = EntityState.Detached; }
    }

    public async Task<FlashcardStatus> UpdateAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid cardId,
        UpdateFlashcardInput input, DateTimeOffset now, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await db.Database.OpenConnectionAsync(ct);
        Flashcard? card = null;
        try
        {
            // Acquire the SQLite writer before reading the revision. No retry/replay.
            // Native lock waits are synchronous/bounded by DefaultTimeout, as in StudySessionStore.
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            await using var transaction = connection.BeginTransaction(deferred: false);
            ct.ThrowIfCancellationRequested();
            await using var enlisted = await db.Database.UseTransactionAsync(transaction, ct);
            card = await Scoped(workspaceId, mapId, nodeId, cardId).SingleOrDefaultAsync(ct);
            if (card is null) return FlashcardStatus.NotFound;
            if (!input.FrontSpecified && !input.BackSpecified || input.FrontSpecified && input.Front is null ||
                input.BackSpecified && input.Back is null) return FlashcardStatus.InvalidInput;
            try
            {
                if (!card.Update(input.FrontSpecified ? input.Front! : card.Front,
                    input.BackSpecified ? input.Back! : card.Back, now)) return FlashcardStatus.InvalidInput;
            }
            catch (ArgumentException) { return FlashcardStatus.InvalidInput; }
            // Overflow/operational failures roll back and reach the sanitized 500 boundary.
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return FlashcardStatus.Success;
        }
        finally
        {
            if (card is not null) db.Entry(card).State = EntityState.Detached;
            await db.Database.CloseConnectionAsync();
        }
    }

    public async Task<FlashcardStatus> DeleteAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid cardId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await Scoped(workspaceId, mapId, nodeId, cardId).ExecuteDeleteAsync(ct) == 1 ?
            FlashcardStatus.Success : FlashcardStatus.NotFound;
    }

    private IQueryable<Flashcard> Scoped(Guid workspaceId, Guid mapId, Guid nodeId, Guid cardId) =>
        db.Flashcards.Where(card => card.Id == cardId && card.KnowledgeNodeId == nodeId &&
            db.KnowledgeNodes.Any(node => node.Id == nodeId && node.KnowledgeMapId == mapId &&
                db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId)));
}

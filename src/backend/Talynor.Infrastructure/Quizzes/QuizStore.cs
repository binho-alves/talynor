using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Talynor.Application.Quizzes;
using Talynor.Domain.Quizzes;
using Talynor.Infrastructure.Persistence;

namespace Talynor.Infrastructure.Quizzes;

public sealed class QuizStore(TalynorDbContext db) : IQuizStore
{
    public Task<bool> NodeExistsAsync(Guid workspaceId, Guid mapId, Guid nodeId, CancellationToken ct) =>
        db.KnowledgeNodes.AsNoTracking().AnyAsync(node => node.Id == nodeId && node.KnowledgeMapId == mapId &&
            db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId), ct);

    public async Task<QuizStatus> CreateAsync(Guid workspaceId, Guid mapId, Quiz quiz, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!await NodeExistsAsync(workspaceId, mapId, quiz.KnowledgeNodeId, ct)) return QuizStatus.NotFound;
        db.Quizzes.Add(quiz);
        try
        {
            await db.SaveChangesAsync(ct);
            return QuizStatus.Success;
        }
        catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteExtendedErrorCode: 787 })
        {
            if (!await NodeExistsAsync(workspaceId, mapId, quiz.KnowledgeNodeId, ct)) return QuizStatus.NotFound;
            throw;
        }
        finally { Detach(quiz); }
    }

    public async Task<QuizStatus> ReplaceAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid quizId,
        int expectedRevision, QuizInput input, DateTimeOffset now, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await db.Database.OpenConnectionAsync(ct);
        Quiz? quiz = null;
        try
        {
            // Native bounded synchronous acquisition, same connection, no automatic lock replay.
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            await using var transaction = connection.BeginTransaction(deferred: false);
            ct.ThrowIfCancellationRequested();
            await using var enlisted = await db.Database.UseTransactionAsync(transaction, ct);
            quiz = await Scoped(workspaceId, mapId, nodeId, quizId).AsNoTracking()
                .Include(q => q.Questions).ThenInclude(q => q.Options).AsSingleQuery().SingleOrDefaultAsync(ct);
            if (quiz is null) return QuizStatus.NotFound;
            if (expectedRevision < 1) return QuizStatus.InvalidInput;
            if (quiz.ContentRevision != expectedRevision) return QuizStatus.ContentChanged;
            try { if (!quiz.Replace(input.Title, input.Questions, now)) return QuizStatus.InvalidInput; }
            catch (ArgumentException) { return QuizStatus.InvalidInput; }
            // Delete old children first to avoid transient unique-position conflicts. Both this
            // statement and the single SaveChanges below commit/roll back in the same transaction.
            await db.QuizQuestions.Where(q => q.QuizId == quiz.Id).ExecuteDeleteAsync(ct);
            db.Entry(quiz).State = EntityState.Modified;
            db.QuizQuestions.AddRange(quiz.Questions);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return QuizStatus.Success;
        }
        finally
        {
            if (quiz is not null) Detach(quiz);
            await db.Database.CloseConnectionAsync();
        }
    }

    public async Task<QuizStatus> DeleteAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid quizId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await Scoped(workspaceId, mapId, nodeId, quizId).ExecuteDeleteAsync(ct) == 1 ? QuizStatus.Success : QuizStatus.NotFound;
    }

    private IQueryable<Quiz> Scoped(Guid workspaceId, Guid mapId, Guid nodeId, Guid quizId) =>
        db.Quizzes.Where(q => q.Id == quizId && q.KnowledgeNodeId == nodeId &&
            db.KnowledgeNodes.Any(node => node.Id == nodeId && node.KnowledgeMapId == mapId &&
                db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId)));

    private void Detach(Quiz quiz)
    {
        var graph = new HashSet<object>(ReferenceEqualityComparer.Instance) { quiz };
        foreach (var question in quiz.Questions)
        {
            graph.Add(question);
            foreach (var option in question.Options) graph.Add(option);
        }
        // Snapshot entries once. Calling Entry(child) while a parent is still tracked can
        // run DetectChanges and reattach previously detached added children after rollback.
        var entries = db.ChangeTracker.Entries().Where(e => graph.Contains(e.Entity)).ToArray();
        foreach (var entry in entries) entry.State = EntityState.Detached;
    }
}

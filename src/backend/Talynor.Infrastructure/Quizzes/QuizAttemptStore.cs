using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Talynor.Application.Quizzes;
using Talynor.Domain.Quizzes;
using Talynor.Infrastructure.Persistence;

namespace Talynor.Infrastructure.Quizzes;

public sealed class QuizAttemptStore(TalynorDbContext db) : IQuizAttemptStore
{
    public async Task<QuizAttemptOperationResult> SubmitAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid quizId,
        Guid attemptId, int contentRevision, IReadOnlyList<QuizAnswerInput> answers, DateTimeOffset now, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!QuizAttempt.ValidSubmission(attemptId, contentRevision, answers)) return new(QuizStatus.InvalidInput);
        await db.Database.OpenConnectionAsync(ct);
        QuizAttempt? attempt = null;
        try
        {
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            await using var transaction = connection.BeginTransaction(deferred: false);
            ct.ThrowIfCancellationRequested();
            await using var enlisted = await db.Database.UseTransactionAsync(transaction, ct);
            var quizzes = db.Quizzes.AsNoTracking().Where(q => q.Id == quizId && q.KnowledgeNodeId == nodeId &&
                db.KnowledgeNodes.Any(n => n.Id == nodeId && n.KnowledgeMapId == mapId &&
                    db.KnowledgeMaps.Any(m => m.Id == mapId && m.WorkspaceId == workspaceId)));
            if (!await quizzes.AnyAsync(ct)) return new(QuizStatus.NotFound);

            // Authorize the complete chain before reading any historical answer payload.
            // A committed old revision must replay before consulting the current revision.
            attempt = await db.QuizAttempts.AsNoTracking().Where(a => a.AttemptId == attemptId && quizzes.Any(q => q.Id == a.QuizId))
                .Include(a => a.Answers).ThenInclude(a => a.Options).AsSingleQuery().SingleOrDefaultAsync(ct);
            if (attempt is not null)
            {
                if (!attempt.Matches(quizId, contentRevision, answers)) return new(QuizStatus.AttemptIdConflict);
                var original = QuizAttemptDetail.From(attempt);
                await transaction.CommitAsync(ct);
                return new(QuizStatus.Success, original);
            }
            // The global PK may collide with another quiz. Query existence only: never load
            // or return that quiz's historical payload, even for an authorized target quiz.
            if (await db.QuizAttempts.AnyAsync(a => a.AttemptId == attemptId, ct)) return new(QuizStatus.AttemptIdConflict);

            var quiz = await quizzes.Include(q => q.Questions).ThenInclude(q => q.Options).AsSingleQuery().SingleAsync(ct);
            if (quiz.ContentRevision != contentRevision) return new(QuizStatus.ContentChanged);
            try { attempt = QuizAttempt.Grade(attemptId, quiz, contentRevision, answers, now); }
            catch (ArgumentException) { return new(QuizStatus.InvalidInput); }
            db.QuizAttempts.Add(attempt);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(QuizStatus.Success, QuizAttemptDetail.From(attempt));
        }
        finally
        {
            if (attempt is not null)
            {
                var graph = new HashSet<object>(ReferenceEqualityComparer.Instance) { attempt };
                foreach (var answer in attempt.Answers)
                {
                    graph.Add(answer);
                    foreach (var option in answer.Options) graph.Add(option);
                }
                var entries = db.ChangeTracker.Entries().Where(e => graph.Contains(e.Entity)).ToArray();
                foreach (var entry in entries) entry.State = EntityState.Detached;
            }
            await db.Database.CloseConnectionAsync();
        }
    }
}

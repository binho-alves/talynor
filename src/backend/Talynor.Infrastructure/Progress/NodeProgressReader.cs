using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Talynor.Application.Progress;
using Talynor.Domain.Flashcards;
using Talynor.Infrastructure.Persistence;

namespace Talynor.Infrastructure.Progress;

public sealed class NodeProgressReader(TalynorDbContext db) : INodeProgressReader
{
    public async Task<NodeProgressSummary?> ReadAsync(Guid workspaceId, Guid mapId, Guid nodeId,
        NodeProgressPeriod period, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            // Explicit deferred BEGIN: the first scoped parent read establishes the snapshot.
            // EF's default SQLite transaction overload is not used (it may reserve a writer).
            await using var transaction = connection.BeginTransaction(deferred: true);
            ct.ThrowIfCancellationRequested();
            await using var enlisted = await db.Database.UseTransactionAsync(transaction, ct);
            var nodes = db.KnowledgeNodes.AsNoTracking().Where(n => n.Id == nodeId && n.KnowledgeMapId == mapId &&
                db.KnowledgeMaps.Any(m => m.Id == mapId && m.WorkspaceId == workspaceId));
            if (!await nodes.AnyAsync(ct)) return null;

            long sessionCount = 0;
            decimal durationTicks = 0;
            // Exact tick subtraction is not SQLite julianday arithmetic; memory stays constant.
            var sessions = db.StudySessions.AsNoTracking().Where(s => s.KnowledgeNodeId == nodeId &&
                    nodes.Any(n => n.Id == s.KnowledgeNodeId) && s.EndedAtUtc >= period.FromUtc && s.EndedAtUtc < period.ToUtc)
                .Select(s => new { s.StartedAtUtc, EndedAtUtc = s.EndedAtUtc!.Value });
            await foreach (var session in sessions.AsAsyncEnumerable().WithCancellation(ct))
            {
                ct.ThrowIfCancellationRequested();
                if (session.EndedAtUtc < session.StartedAtUtc) throw new InvalidOperationException("Invalid progress source data.");
                sessionCount = checked(sessionCount + 1);
                durationTicks = checked(durationTicks + (session.EndedAtUtc - session.StartedAtUtc).Ticks);
            }
            var study = NodeProgressCalculation.Study(sessionCount, durationTicks);

            var cards = db.Flashcards.AsNoTracking().Where(c => c.KnowledgeNodeId == nodeId && nodes.Any(n => n.Id == c.KnowledgeNodeId));
            var cardIds = cards.Select(c => c.Id);
            var reviews = await db.FlashcardReviews.AsNoTracking().Where(r => cardIds.Contains(r.FlashcardId) &&
                    r.ReviewedAtUtc >= period.FromUtc && r.ReviewedAtUtc < period.ToUtc)
                .GroupBy(r => 1).Select(g => new
                {
                    Count = g.LongCount(), Correct = g.LongCount(r => r.Result == FlashcardAnswerResult.Correct),
                    Invalid = g.LongCount(r => r.Result != FlashcardAnswerResult.Correct && r.Result != FlashcardAnswerResult.Incorrect)
                }).SingleOrDefaultAsync(ct);
            if (reviews?.Invalid > 0) throw new InvalidOperationException("Invalid progress source data.");
            var flashcards = NodeProgressCalculation.Flashcards(reviews?.Count ?? 0, reviews?.Correct ?? 0);

            var quizzes = db.Quizzes.AsNoTracking().Where(q => q.KnowledgeNodeId == nodeId && nodes.Any(n => n.Id == q.KnowledgeNodeId));
            var quizIds = quizzes.Select(q => q.Id);
            var attempts = await db.QuizAttempts.AsNoTracking().Where(a => quizIds.Contains(a.QuizId) &&
                    a.SubmittedAtUtc >= period.FromUtc && a.SubmittedAtUtc < period.ToUtc)
                .GroupBy(a => 1).Select(g => new
                {
                    Count = g.LongCount(), Questions = g.Sum(a => (long)a.QuestionCount), Correct = g.Sum(a => (long)a.CorrectCount),
                    Invalid = g.LongCount(a => a.QuestionCount < 1 || a.QuestionCount > 10 || a.CorrectCount < 0 || a.CorrectCount > a.QuestionCount)
                }).SingleOrDefaultAsync(ct);
            if (attempts?.Invalid > 0) throw new InvalidOperationException("Invalid progress source data.");
            var quizProgress = NodeProgressCalculation.Quizzes(attempts?.Count ?? 0, attempts?.Questions ?? 0, attempts?.Correct ?? 0);
            await transaction.CommitAsync(ct);
            return new(period, study, flashcards, quizProgress);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}

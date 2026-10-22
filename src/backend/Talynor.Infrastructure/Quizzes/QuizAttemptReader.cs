using Microsoft.EntityFrameworkCore;
using Talynor.Application.Quizzes;
using Talynor.Domain.Quizzes;
using Talynor.Infrastructure.Persistence;

namespace Talynor.Infrastructure.Quizzes;

public sealed class QuizAttemptReader(TalynorDbContext db) : IQuizAttemptReader
{
    public async Task<QuizAttemptPage?> ListAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid quizId,
        int offset, int pageSize, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await OwnedQuizzes(workspaceId, mapId, nodeId, quizId).AnyAsync(ct)) return null;
        var items = await Scoped(workspaceId, mapId, nodeId, quizId).OrderByDescending(a => a.SubmittedAtUtc).ThenByDescending(a => a.AttemptId)
            .Skip(offset).Take(pageSize + 1).Select(a => new QuizAttemptSummary(a.AttemptId, a.QuizId, a.QuizTitle,
                a.ContentRevision, a.SubmittedAtUtc, a.QuestionCount, a.CorrectCount)).ToArrayAsync(ct);
        await transaction.CommitAsync(ct);
        return new(items.Take(pageSize).ToArray(), items.Length > pageSize);
    }

    public Task<QuizAttemptDetail?> GetAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid quizId, Guid attemptId, CancellationToken ct) =>
        Scoped(workspaceId, mapId, nodeId, quizId).Where(a => a.AttemptId == attemptId).AsSingleQuery()
            .Select(a => new QuizAttemptDetail(a.AttemptId, a.QuizId, a.QuizTitle, a.ContentRevision, a.SubmittedAtUtc,
                a.QuestionCount, a.CorrectCount, a.Answers.OrderBy(answer => answer.Position).Select(answer =>
                    new QuizAttemptAnswerView(answer.QuestionId, answer.Position, answer.Text,
                        answer.Options.OrderBy(o => o.Position).Select(o => new QuizAttemptOptionView(o.OptionId, o.Position, o.Text)).ToArray(),
                        answer.SelectedOptionId, answer.CorrectOptionId)).ToArray())).SingleOrDefaultAsync(ct);

    private IQueryable<Quiz> OwnedQuizzes(Guid workspaceId, Guid mapId, Guid nodeId, Guid quizId) =>
        db.Quizzes.AsNoTracking().Where(q => q.Id == quizId && q.KnowledgeNodeId == nodeId &&
            db.KnowledgeNodes.Any(n => n.Id == nodeId && n.KnowledgeMapId == mapId &&
                db.KnowledgeMaps.Any(m => m.Id == mapId && m.WorkspaceId == workspaceId)));
    private IQueryable<QuizAttempt> Scoped(Guid workspaceId, Guid mapId, Guid nodeId, Guid quizId)
    {
        var quizzes = OwnedQuizzes(workspaceId, mapId, nodeId, quizId);
        return db.QuizAttempts.AsNoTracking().Where(a => a.QuizId == quizId && quizzes.Any(q => q.Id == a.QuizId));
    }
}

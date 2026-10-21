using Microsoft.EntityFrameworkCore;
using Talynor.Application.Quizzes;
using Talynor.Domain.Quizzes;
using Talynor.Infrastructure.Persistence;

namespace Talynor.Infrastructure.Quizzes;

public sealed class QuizReader(TalynorDbContext db) : IQuizReader
{
    public async Task<QuizPage?> ListAsync(Guid workspaceId, Guid mapId, Guid nodeId, int offset, int pageSize, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await db.KnowledgeNodes.AsNoTracking().AnyAsync(node => node.Id == nodeId && node.KnowledgeMapId == mapId &&
            db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId), ct)) return null;
        var items = await Scoped(workspaceId, mapId, nodeId).OrderBy(q => q.CreatedAtUtc).ThenBy(q => q.Id)
            .Skip(offset).Take(pageSize + 1).Select(q => new QuizSummary(q.Id, q.Title, q.ContentRevision,
                q.CreatedAtUtc, q.UpdatedAtUtc)).ToArrayAsync(ct);
        await transaction.CommitAsync(ct);
        return new(items.Take(pageSize).ToArray(), items.Length > pageSize);
    }

    // Each compound definition is a single SQL statement (never split collection queries).
    // Revision, question and option projections therefore share one SQLite read snapshot.
    public Task<QuizDefinition?> GetAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid quizId, CancellationToken ct) =>
        Scoped(workspaceId, mapId, nodeId).Where(q => q.Id == quizId).AsSingleQuery()
            .Select(q => new QuizDefinition(q.Id, q.KnowledgeNodeId, q.Title, q.ContentRevision, q.CreatedAtUtc, q.UpdatedAtUtc,
                q.Questions.OrderBy(question => question.Position).Select(question => new QuizQuestionDefinition(
                    question.Id, question.Text, question.Position, question.Options.OrderBy(o => o.Position)
                        .Select(o => new QuizOptionDefinition(o.Id, o.Text, o.Position)).ToArray(),
                    question.Options.Where(o => o.IsCorrect).Select(o => o.Position - 1).First())).ToArray()))
            .SingleOrDefaultAsync(ct);

    public Task<QuizTakeDefinition?> TakeAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid quizId, CancellationToken ct) =>
        Scoped(workspaceId, mapId, nodeId).Where(q => q.Id == quizId).AsSingleQuery()
            .Select(q => new QuizTakeDefinition(q.Id, q.Title, q.ContentRevision,
                q.Questions.OrderBy(question => question.Position).Select(question => new QuizTakeQuestion(
                    question.Id, question.Text, question.Position, question.Options.OrderBy(o => o.Position)
                        .Select(o => new QuizTakeOption(o.Id, o.Text, o.Position)).ToArray())).ToArray()))
            .SingleOrDefaultAsync(ct);

    private IQueryable<Quiz> Scoped(Guid workspaceId, Guid mapId, Guid nodeId) =>
        db.Quizzes.AsNoTracking().Where(q => q.KnowledgeNodeId == nodeId &&
            db.KnowledgeNodes.Any(node => node.Id == nodeId && node.KnowledgeMapId == mapId &&
                db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId)));
}

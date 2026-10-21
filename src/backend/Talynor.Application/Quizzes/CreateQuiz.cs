using Talynor.Application.Workspaces;
using Talynor.Domain.Quizzes;

namespace Talynor.Application.Quizzes;

public sealed class CreateQuiz(GetOrCreatePersonalWorkspace getWorkspace, IQuizStore store, TimeProvider time)
{
    public async Task<QuizResult> ExecuteAsync(Guid mapId, Guid nodeId, QuizInput input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(QuizStatus.AuthenticationRequired);
        if (!await store.NodeExistsAsync(workspace.Id, mapId, nodeId, ct)) return new(QuizStatus.NotFound);
        Quiz quiz;
        try { quiz = Quiz.Create(nodeId, input.Title, input.Questions, time.GetUtcNow()); }
        catch (ArgumentException) { return new(QuizStatus.InvalidInput); }
        var status = await store.CreateAsync(workspace.Id, mapId, quiz, ct);
        return new(status, status == QuizStatus.Success ? QuizDefinition.From(quiz) : null);
    }
}

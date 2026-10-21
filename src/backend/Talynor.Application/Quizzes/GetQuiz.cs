using Talynor.Application.Workspaces;

namespace Talynor.Application.Quizzes;

public sealed class GetQuiz(GetOrCreatePersonalWorkspace getWorkspace, IQuizReader reader)
{
    public async Task<QuizResult> ExecuteAsync(Guid mapId, Guid nodeId, Guid quizId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(QuizStatus.AuthenticationRequired);
        var definition = await reader.GetAsync(workspace.Id, mapId, nodeId, quizId, ct);
        return definition is null ? new(QuizStatus.NotFound) : new(QuizStatus.Success, definition);
    }
}

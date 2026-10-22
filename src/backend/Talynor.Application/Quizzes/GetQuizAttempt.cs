using Talynor.Application.Workspaces;

namespace Talynor.Application.Quizzes;

public sealed class GetQuizAttempt(GetOrCreatePersonalWorkspace getWorkspace, IQuizAttemptReader reader)
{
    public async Task<QuizAttemptOperationResult> ExecuteAsync(Guid mapId, Guid nodeId, Guid quizId, Guid attemptId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(QuizStatus.AuthenticationRequired);
        var result = await reader.GetAsync(workspace.Id, mapId, nodeId, quizId, attemptId, ct);
        return result is null ? new(QuizStatus.NotFound) : new(QuizStatus.Success, result);
    }
}

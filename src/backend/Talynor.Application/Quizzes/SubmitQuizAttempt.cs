using Talynor.Application.Workspaces;
using Talynor.Domain.Quizzes;

namespace Talynor.Application.Quizzes;

public sealed class SubmitQuizAttempt(GetOrCreatePersonalWorkspace getWorkspace, IQuizAttemptStore store, TimeProvider time)
{
    public async Task<QuizAttemptOperationResult> ExecuteAsync(Guid mapId, Guid nodeId, Guid quizId,
        Guid attemptId, int contentRevision, IReadOnlyList<QuizAnswerInput> answers, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(QuizStatus.AuthenticationRequired);
        if (!QuizAttempt.ValidSubmission(attemptId, contentRevision, answers)) return new(QuizStatus.InvalidInput);
        return await store.SubmitAsync(workspace.Id, mapId, nodeId, quizId, attemptId, contentRevision, answers, time.GetUtcNow(), ct);
    }
}

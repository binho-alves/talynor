using Talynor.Application.Workspaces;

namespace Talynor.Application.Quizzes;

public sealed class ReplaceQuiz(GetOrCreatePersonalWorkspace getWorkspace, IQuizStore store, TimeProvider time)
{
    public async Task<QuizStatus> ExecuteAsync(Guid mapId, Guid nodeId, Guid quizId, int expectedRevision,
        QuizInput input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return QuizStatus.AuthenticationRequired;
        if (expectedRevision < 1) return QuizStatus.InvalidInput;
        return await store.ReplaceAsync(workspace.Id, mapId, nodeId, quizId, expectedRevision, input, time.GetUtcNow(), ct);
    }
}

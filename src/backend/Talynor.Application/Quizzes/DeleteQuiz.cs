using Talynor.Application.Workspaces;

namespace Talynor.Application.Quizzes;

public sealed class DeleteQuiz(GetOrCreatePersonalWorkspace getWorkspace, IQuizStore store)
{
    public async Task<QuizStatus> ExecuteAsync(Guid mapId, Guid nodeId, Guid quizId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        return workspace is null ? QuizStatus.AuthenticationRequired :
            await store.DeleteAsync(workspace.Id, mapId, nodeId, quizId, ct);
    }
}

using Talynor.Application.Workspaces;

namespace Talynor.Application.Quizzes;

public sealed class ListQuizzes(GetOrCreatePersonalWorkspace getWorkspace, IQuizReader reader)
{
    public async Task<QuizListResult> ExecuteAsync(Guid mapId, Guid nodeId, int page, int pageSize, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(QuizStatus.AuthenticationRequired);
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            return new(QuizStatus.InvalidInput);
        var result = await reader.ListAsync(workspace.Id, mapId, nodeId, (page - 1) * pageSize, pageSize, ct);
        return result is null ? new(QuizStatus.NotFound) :
            new(QuizStatus.Success, new(result.Quizzes, page, pageSize, result.HasMore));
    }
}

namespace Talynor.Application.Quizzes;

public interface IQuizAttemptReader
{
    Task<QuizAttemptPage?> ListAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid quizId, int offset, int pageSize, CancellationToken ct);
    Task<QuizAttemptDetail?> GetAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid quizId, Guid attemptId, CancellationToken ct);
}

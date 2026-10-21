using Talynor.Domain.Quizzes;

namespace Talynor.Application.Quizzes;

public interface IQuizStore
{
    Task<bool> NodeExistsAsync(Guid workspaceId, Guid mapId, Guid nodeId, CancellationToken ct);
    Task<QuizStatus> CreateAsync(Guid workspaceId, Guid mapId, Quiz quiz, CancellationToken ct);
    Task<QuizStatus> ReplaceAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid quizId,
        int expectedRevision, QuizInput input, DateTimeOffset now, CancellationToken ct);
    Task<QuizStatus> DeleteAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid quizId, CancellationToken ct);
}

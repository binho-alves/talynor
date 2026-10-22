using Talynor.Domain.Quizzes;

namespace Talynor.Application.Quizzes;

public interface IQuizAttemptStore
{
    Task<QuizAttemptOperationResult> SubmitAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid quizId,
        Guid attemptId, int contentRevision, IReadOnlyList<QuizAnswerInput> answers, DateTimeOffset now, CancellationToken ct);
}

namespace Talynor.Application.Quizzes;

public interface IQuizReader
{
    Task<QuizPage?> ListAsync(Guid workspaceId, Guid mapId, Guid nodeId, int offset, int pageSize, CancellationToken ct);
    Task<QuizDefinition?> GetAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid quizId, CancellationToken ct);
    Task<QuizTakeDefinition?> TakeAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid quizId, CancellationToken ct);
}

using Talynor.Domain.Quizzes;

namespace Talynor.Application.Quizzes;

public enum QuizStatus { Success, AuthenticationRequired, NotFound, InvalidInput, ContentChanged }
public sealed record QuizInput(string Title, IReadOnlyList<QuizQuestionInput> Questions);
public sealed record QuizSummary(Guid Id, string Title, int ContentRevision, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record QuizOptionDefinition(Guid Id, string Text, int Position);
public sealed record QuizQuestionDefinition(Guid Id, string Text, int Position,
    IReadOnlyList<QuizOptionDefinition> Options, int CorrectOptionIndex);
public sealed record QuizDefinition(Guid Id, Guid KnowledgeNodeId, string Title, int ContentRevision,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, IReadOnlyList<QuizQuestionDefinition> Questions)
{
    public static QuizDefinition From(Quiz quiz) => new(quiz.Id, quiz.KnowledgeNodeId, quiz.Title, quiz.ContentRevision,
        quiz.CreatedAtUtc, quiz.UpdatedAtUtc, quiz.Questions.OrderBy(q => q.Position).Select(q =>
            new QuizQuestionDefinition(q.Id, q.Text, q.Position, q.Options.OrderBy(o => o.Position)
                .Select(o => new QuizOptionDefinition(o.Id, o.Text, o.Position)).ToArray(),
                q.Options.Single(o => o.IsCorrect).Position - 1)).ToArray());
}
// Separate read contracts: taking cannot carry answer-key fields.
public sealed record QuizTakeOption(Guid Id, string Text, int Position);
public sealed record QuizTakeQuestion(Guid Id, string Text, int Position, IReadOnlyList<QuizTakeOption> Options);
public sealed record QuizTakeDefinition(Guid QuizId, string Title, int ContentRevision, IReadOnlyList<QuizTakeQuestion> Questions);
public sealed record QuizPage(IReadOnlyList<QuizSummary> Quizzes, bool HasMore);
public sealed record QuizCollection(IReadOnlyList<QuizSummary> Quizzes, int Page, int PageSize, bool HasMore);
public sealed record QuizListResult(QuizStatus Status, QuizCollection? Collection = null);
public sealed record QuizResult(QuizStatus Status, QuizDefinition? Definition = null);
public sealed record QuizTakeResult(QuizStatus Status, QuizTakeDefinition? Definition = null);

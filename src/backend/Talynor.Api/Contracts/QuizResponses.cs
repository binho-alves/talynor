using Talynor.Application.Quizzes;

namespace Talynor.Api.Contracts;

public sealed record QuizSummaryResponse(Guid Id, string Title, int ContentRevision, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record QuizCollectionResponse(IReadOnlyList<QuizSummaryResponse> Quizzes, int Page, int PageSize, bool HasMore);
public sealed record QuizOptionResponse(Guid Id, string Text, int Position);
public sealed record QuizQuestionResponse(Guid Id, string Text, int Position, IReadOnlyList<QuizOptionResponse> Options, int CorrectOptionIndex);
public sealed record QuizDefinitionResponse(Guid Id, Guid KnowledgeNodeId, string Title, int ContentRevision,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, IReadOnlyList<QuizQuestionResponse> Questions);
public sealed record QuizTakeOptionResponse(Guid Id, string Text, int Position);
public sealed record QuizTakeQuestionResponse(Guid Id, string Text, int Position, IReadOnlyList<QuizTakeOptionResponse> Options);
public sealed record QuizTakeResponse(Guid QuizId, string Title, int ContentRevision, IReadOnlyList<QuizTakeQuestionResponse> Questions);

internal static class QuizResponseMapping
{
    public static QuizDefinitionResponse ToResponse(QuizDefinition q) => new(q.Id, q.KnowledgeNodeId, q.Title, q.ContentRevision,
        q.CreatedAtUtc, q.UpdatedAtUtc, q.Questions.Select(question => new QuizQuestionResponse(question.Id, question.Text,
            question.Position, question.Options.Select(o => new QuizOptionResponse(o.Id, o.Text, o.Position)).ToArray(),
            question.CorrectOptionIndex)).ToArray());
    public static QuizTakeResponse ToResponse(QuizTakeDefinition q) => new(q.QuizId, q.Title, q.ContentRevision,
        q.Questions.Select(question => new QuizTakeQuestionResponse(question.Id, question.Text, question.Position,
            question.Options.Select(o => new QuizTakeOptionResponse(o.Id, o.Text, o.Position)).ToArray())).ToArray());
}

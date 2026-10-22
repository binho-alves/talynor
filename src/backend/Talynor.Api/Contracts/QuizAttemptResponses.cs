using Talynor.Application.Quizzes;

namespace Talynor.Api.Contracts;

public sealed record QuizAttemptOptionResponse(Guid Id, int Position, string Text);
public sealed record QuizAttemptAnswerResponse(Guid QuestionId, int Position, string Text,
    IReadOnlyList<QuizAttemptOptionResponse> Options, Guid SelectedOptionId, Guid CorrectOptionId, bool IsCorrect);
public sealed record QuizAttemptSummaryResponse(Guid AttemptId, Guid QuizId, string QuizTitle, int ContentRevision,
    DateTimeOffset SubmittedAtUtc, int QuestionCount, int CorrectCount, decimal AccuracyPercentage);
public sealed record QuizAttemptResponse(Guid AttemptId, Guid QuizId, string QuizTitle, int ContentRevision,
    DateTimeOffset SubmittedAtUtc, int QuestionCount, int CorrectCount, decimal AccuracyPercentage, IReadOnlyList<QuizAttemptAnswerResponse> Answers);
public sealed record QuizAttemptCollectionResponse(IReadOnlyList<QuizAttemptSummaryResponse> Attempts, int Page, int PageSize, bool HasMore);

public static class QuizAttemptResponseMapping
{
    public static QuizAttemptResponse ToResponse(QuizAttemptDetail a) => new(a.AttemptId, a.QuizId, a.QuizTitle, a.ContentRevision,
        a.SubmittedAtUtc, a.QuestionCount, a.CorrectCount, a.AccuracyPercentage, a.Answers.Select(answer =>
            new QuizAttemptAnswerResponse(answer.QuestionId, answer.Position, answer.Text,
                answer.Options.Select(o => new QuizAttemptOptionResponse(o.Id, o.Position, o.Text)).ToArray(),
                answer.SelectedOptionId, answer.CorrectOptionId, answer.IsCorrect)).ToArray());
    public static QuizAttemptSummaryResponse ToResponse(QuizAttemptSummary a) => new(a.AttemptId, a.QuizId, a.QuizTitle,
        a.ContentRevision, a.SubmittedAtUtc, a.QuestionCount, a.CorrectCount, a.AccuracyPercentage);
}

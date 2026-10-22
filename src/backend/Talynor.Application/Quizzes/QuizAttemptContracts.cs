using Talynor.Domain.Quizzes;

namespace Talynor.Application.Quizzes;

public sealed record QuizAttemptOptionView(Guid Id, int Position, string Text);
public sealed record QuizAttemptAnswerView(Guid QuestionId, int Position, string Text,
    IReadOnlyList<QuizAttemptOptionView> Options, Guid SelectedOptionId, Guid CorrectOptionId)
{
    public bool IsCorrect => SelectedOptionId == CorrectOptionId;
}
public sealed record QuizAttemptSummary(Guid AttemptId, Guid QuizId, string QuizTitle, int ContentRevision,
    DateTimeOffset SubmittedAtUtc, int QuestionCount, int CorrectCount)
{
    public decimal AccuracyPercentage => QuizAttempt.Accuracy(CorrectCount, QuestionCount);
}
public sealed record QuizAttemptDetail(Guid AttemptId, Guid QuizId, string QuizTitle, int ContentRevision,
    DateTimeOffset SubmittedAtUtc, int QuestionCount, int CorrectCount, IReadOnlyList<QuizAttemptAnswerView> Answers)
{
    public decimal AccuracyPercentage => QuizAttempt.Accuracy(CorrectCount, QuestionCount);
    public static QuizAttemptDetail From(QuizAttempt attempt) => new(attempt.AttemptId, attempt.QuizId, attempt.QuizTitle,
        attempt.ContentRevision, attempt.SubmittedAtUtc, attempt.QuestionCount, attempt.CorrectCount,
        attempt.Answers.OrderBy(a => a.Position).Select(a => new QuizAttemptAnswerView(a.QuestionId, a.Position, a.Text,
            a.Options.OrderBy(o => o.Position).Select(o => new QuizAttemptOptionView(o.OptionId, o.Position, o.Text)).ToArray(),
            a.SelectedOptionId, a.CorrectOptionId)).ToArray());
}
public sealed record QuizAttemptPage(IReadOnlyList<QuizAttemptSummary> Attempts, bool HasMore);
public sealed record QuizAttemptCollection(IReadOnlyList<QuizAttemptSummary> Attempts, int Page, int PageSize, bool HasMore);
public sealed record QuizAttemptOperationResult(QuizStatus Status, QuizAttemptDetail? Attempt = null);
public sealed record QuizAttemptListResult(QuizStatus Status, QuizAttemptCollection? Collection = null);

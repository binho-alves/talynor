using Talynor.Application.Progress;

namespace Talynor.Api.Contracts;

public sealed record NodeProgressResponse(NodeProgressPeriodResponse Period, DateTimeOffset CalculatedAtUtc,
    StudyProgressResponse Study, FlashcardProgressResponse Flashcards, QuizProgressResponse Quizzes);
public sealed record NodeProgressPeriodResponse(int Days, DateTimeOffset FromUtc, DateTimeOffset ToUtc);
public sealed record StudyProgressResponse(long CompletedSessionCount, long TotalDurationSeconds);
public sealed record FlashcardProgressResponse(long ReviewCount, long CorrectCount, decimal? CorrectPercentage);
public sealed record QuizProgressResponse(long AttemptCount, long AnsweredQuestionCount, long CorrectAnswerCount,
    decimal? AccuracyPercentage);

internal static class NodeProgressResponseMapping
{
    public static NodeProgressResponse ToResponse(NodeProgressSummary summary) => new(
        new(summary.Period.Days, summary.Period.FromUtc, summary.Period.ToUtc), summary.CalculatedAtUtc,
        new(summary.Study.CompletedSessionCount, summary.Study.TotalDurationSeconds),
        new(summary.Flashcards.ReviewCount, summary.Flashcards.CorrectCount, summary.Flashcards.CorrectPercentage),
        new(summary.Quizzes.AttemptCount, summary.Quizzes.AnsweredQuestionCount, summary.Quizzes.CorrectAnswerCount,
            summary.Quizzes.AccuracyPercentage));
}

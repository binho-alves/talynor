namespace Talynor.Application.Progress;

public sealed record NodeProgressPeriod
{
    private NodeProgressPeriod(int days, DateTimeOffset toUtc)
    { Days = days; ToUtc = toUtc; FromUtc = toUtc.AddDays(-days); }

    public int Days { get; }
    public DateTimeOffset FromUtc { get; }
    public DateTimeOffset ToUtc { get; }
    public static bool Supports(int days) => days is 7 or 30 or 90;
    public static NodeProgressPeriod Create(int days, DateTimeOffset reference)
    {
        if (!Supports(days)) throw new ArgumentOutOfRangeException(nameof(days));
        // A clock/range failure is operational, never a silently shortened period.
        return new(days, reference.ToUniversalTime());
    }
}

public sealed record StudyProgress(long CompletedSessionCount, long TotalDurationSeconds);
public sealed record FlashcardProgress(long ReviewCount, long CorrectCount, decimal? CorrectPercentage);
public sealed record QuizProgress(long AttemptCount, long AnsweredQuestionCount, long CorrectAnswerCount, decimal? AccuracyPercentage);
public sealed record NodeProgressSummary(NodeProgressPeriod Period, StudyProgress Study,
    FlashcardProgress Flashcards, QuizProgress Quizzes)
{
    public DateTimeOffset CalculatedAtUtc => Period.ToUtc;
}

public enum NodeProgressStatus { Success, AuthenticationRequired, NotFound, Invalid }
public sealed record NodeProgressResult(NodeProgressStatus Status, NodeProgressSummary? Summary = null);

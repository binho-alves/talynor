namespace Talynor.Application.Progress;

// Application rules shared by the SQL reader and deterministic arithmetic tests.
public static class NodeProgressCalculation
{
    public const long MaxSafeInteger = 9_007_199_254_740_991;

    public static StudyProgress Study(long count, decimal durationTicks)
    {
        ValidateInteger(count);
        if (durationTicks < 0 || decimal.Truncate(durationTicks) != durationTicks || (count == 0 && durationTicks != 0))
            throw new InvalidOperationException("Invalid progress source data.");
        var seconds = decimal.Floor(durationTicks / TimeSpan.TicksPerSecond);
        if (seconds > MaxSafeInteger) throw new InvalidOperationException("Progress numeric range exceeded.");
        return new(count, (long)seconds);
    }

    public static FlashcardProgress Flashcards(long reviews, long correct)
    {
        ValidateInteger(reviews); ValidateInteger(correct);
        return new(reviews, correct, Percentage(correct, reviews));
    }

    public static QuizProgress Quizzes(long attempts, long questions, long correct)
    {
        ValidateInteger(attempts); ValidateInteger(questions); ValidateInteger(correct);
        if (questions < attempts || questions > 10m * attempts)
            throw new InvalidOperationException("Invalid progress source data.");
        return new(attempts, questions, correct, Percentage(correct, questions));
    }

    private static decimal? Percentage(long numerator, long denominator)
    {
        if (numerator > denominator) throw new InvalidOperationException("Invalid progress source data.");
        return denominator == 0 ? null : Math.Round(100m * numerator / denominator, 2, MidpointRounding.AwayFromZero);
    }

    private static void ValidateInteger(long value)
    {
        if (value is < 0 or > MaxSafeInteger) throw new InvalidOperationException("Progress numeric range exceeded.");
    }
}

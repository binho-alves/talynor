namespace Talynor.Domain.Flashcards;

public sealed class FlashcardReviewState
{
    private FlashcardReviewState() { }

    public Guid FlashcardId { get; private set; }
    public int ReviewCount { get; private set; }
    public int CorrectCount { get; private set; }
    public DateTimeOffset LastReviewedAtUtc { get; private set; }

    public static FlashcardReviewState FromFirstReview(FlashcardReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        return new FlashcardReviewState
        {
            FlashcardId = review.FlashcardId,
            ReviewCount = 1,
            CorrectCount = review.Result == FlashcardAnswerResult.Correct ? 1 : 0,
            LastReviewedAtUtc = review.ReviewedAtUtc.ToUniversalTime()
        };
    }

    public void Record(FlashcardReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (review.FlashcardId != FlashcardId)
            throw new ArgumentException("Review belongs to another flashcard.", nameof(review));

        var nextReviewCount = checked(ReviewCount + 1);
        var nextCorrectCount = review.Result == FlashcardAnswerResult.Correct
            ? checked(CorrectCount + 1)
            : CorrectCount;

        ReviewCount = nextReviewCount;
        CorrectCount = nextCorrectCount;
        LastReviewedAtUtc = review.ReviewedAtUtc.ToUniversalTime();
    }
}

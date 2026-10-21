namespace Talynor.Domain.Flashcards;

public sealed class FlashcardReview
{
    private FlashcardReview() { }

    public Guid ReviewId { get; private set; }
    public Guid FlashcardId { get; private set; }
    public int ContentRevision { get; private set; }
    public FlashcardAnswerResult Result { get; private set; }
    public DateTimeOffset ReviewedAtUtc { get; private set; }

    public static FlashcardReview Create(Guid reviewId, Guid flashcardId, int contentRevision,
        FlashcardAnswerResult result, DateTimeOffset reviewedAtUtc)
    {
        if (reviewId == Guid.Empty) throw new ArgumentException("Review ID is required.", nameof(reviewId));
        if (flashcardId == Guid.Empty) throw new ArgumentException("Flashcard ID is required.", nameof(flashcardId));
        if (contentRevision < 1) throw new ArgumentOutOfRangeException(nameof(contentRevision));
        if (result is not (FlashcardAnswerResult.Correct or FlashcardAnswerResult.Incorrect))
            throw new ArgumentOutOfRangeException(nameof(result));

        return new FlashcardReview
        {
            ReviewId = reviewId,
            FlashcardId = flashcardId,
            ContentRevision = contentRevision,
            Result = result,
            ReviewedAtUtc = reviewedAtUtc.ToUniversalTime()
        };
    }
}

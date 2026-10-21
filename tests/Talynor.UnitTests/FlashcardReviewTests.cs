using Talynor.Domain.Flashcards;

namespace Talynor.UnitTests;

public class FlashcardReviewTests
{
    private static readonly DateTimeOffset Local = new(2026, 10, 20, 9, 1, 2, TimeSpan.FromHours(-3));

    [Fact]
    public void Review_event_validates_identity_revision_result_and_normalizes_utc()
    {
        var cardId = Guid.NewGuid(); var reviewId = Guid.NewGuid();
        var review = FlashcardReview.Create(reviewId, cardId, 3, FlashcardAnswerResult.Correct, Local);
        Assert.Equal(reviewId, review.ReviewId); Assert.Equal(cardId, review.FlashcardId);
        Assert.Equal(3, review.ContentRevision); Assert.Equal(FlashcardAnswerResult.Correct, review.Result);
        Assert.Equal(Local.ToUniversalTime(), review.ReviewedAtUtc);
        Assert.Equal(TimeSpan.Zero, review.ReviewedAtUtc.Offset);
        Assert.Null(typeof(FlashcardReview).GetProperty(nameof(FlashcardReview.Result))!.GetSetMethod(nonPublic: false));
    }

    [Fact]
    public void Review_event_rejects_empty_ids_nonpositive_revision_and_unknown_result()
    {
        Assert.Throws<ArgumentException>(() => FlashcardReview.Create(Guid.Empty, Guid.NewGuid(), 1, FlashcardAnswerResult.Correct, Local));
        Assert.Throws<ArgumentException>(() => FlashcardReview.Create(Guid.NewGuid(), Guid.Empty, 1, FlashcardAnswerResult.Correct, Local));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlashcardReview.Create(Guid.NewGuid(), Guid.NewGuid(), 0, FlashcardAnswerResult.Correct, Local));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlashcardReview.Create(Guid.NewGuid(), Guid.NewGuid(), 1, (FlashcardAnswerResult)42, Local));
    }

    [Fact]
    public void Optional_state_starts_at_first_event_and_updates_counts_and_time()
    {
        var cardId = Guid.NewGuid();
        var first = FlashcardReview.Create(Guid.NewGuid(), cardId, 1, FlashcardAnswerResult.Incorrect, Local);
        var state = FlashcardReviewState.FromFirstReview(first);
        Assert.Equal((1, 0, Local.ToUniversalTime()), (state.ReviewCount, state.CorrectCount, state.LastReviewedAtUtc));

        state.Record(FlashcardReview.Create(Guid.NewGuid(), cardId, 1, FlashcardAnswerResult.Correct, Local.AddMinutes(1)));
        Assert.Equal((2, 1, Local.AddMinutes(1).ToUniversalTime()), (state.ReviewCount, state.CorrectCount, state.LastReviewedAtUtc));
        state.Record(FlashcardReview.Create(Guid.NewGuid(), cardId, 1, FlashcardAnswerResult.Incorrect, Local.AddMinutes(2)));
        Assert.Equal((3, 1, Local.AddMinutes(2).ToUniversalTime()), (state.ReviewCount, state.CorrectCount, state.LastReviewedAtUtc));
        Assert.Throws<ArgumentException>(() => state.Record(FlashcardReview.Create(Guid.NewGuid(), Guid.NewGuid(), 1,
            FlashcardAnswerResult.Correct, Local.AddMinutes(3))));
    }

    [Fact]
    public void State_count_overflow_is_checked_without_partial_change()
    {
        var cardId = Guid.NewGuid();
        var first = FlashcardReview.Create(Guid.NewGuid(), cardId, 1, FlashcardAnswerResult.Correct, Local);
        var state = FlashcardReviewState.FromFirstReview(first);
        typeof(FlashcardReviewState).GetProperty(nameof(FlashcardReviewState.ReviewCount))!.SetValue(state, int.MaxValue);
        var before = (state.ReviewCount, state.CorrectCount, state.LastReviewedAtUtc);
        Assert.Throws<OverflowException>(() => state.Record(FlashcardReview.Create(Guid.NewGuid(), cardId, 1,
            FlashcardAnswerResult.Correct, Local.AddDays(1))));
        Assert.Equal(before, (state.ReviewCount, state.CorrectCount, state.LastReviewedAtUtc));
    }
}

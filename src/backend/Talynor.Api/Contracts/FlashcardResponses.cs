using Talynor.Application.Flashcards;

namespace Talynor.Api.Contracts;

public sealed record FlashcardResponse(Guid Id, Guid KnowledgeNodeId, string Front, string Back,
    int ContentRevision, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record FlashcardCollectionResponse(IReadOnlyList<FlashcardResponse> Flashcards, int Page, int PageSize, bool HasMore);
public sealed record FlashcardStudyReviewStateResponse(int ContentRevision, int ReviewCount, int CorrectCount,
    DateTimeOffset? LastReviewedAtUtc);
public sealed record FlashcardStudyItemResponse(Guid Id, string Front, int ContentRevision,
    FlashcardStudyReviewStateResponse ReviewState);
public sealed record FlashcardStudyNextResponse(FlashcardStudyItemResponse? Item);
public sealed record FlashcardRevealResponse(Guid FlashcardId, string Back, int ContentRevision);
public sealed record FlashcardReviewResponse(Guid ReviewId, Guid FlashcardId, int ContentRevision,
    string Result, DateTimeOffset ReviewedAtUtc);
public sealed record FlashcardReviewEnvelopeResponse(FlashcardReviewResponse Review,
    FlashcardStudyReviewStateResponse CurrentReviewState);
internal static class FlashcardResponseMapping
{
    public static FlashcardResponse ToResponse(FlashcardDetail card) => new(card.Id, card.KnowledgeNodeId, card.Front,
        card.Back, card.ContentRevision, card.CreatedAtUtc, card.UpdatedAtUtc);

    public static FlashcardStudyNextResponse ToStudyNextResponse(FlashcardStudyNextItem? item) => new(item is null ? null :
        new FlashcardStudyItemResponse(item.Id, item.Front, item.ContentRevision,
            new(item.ContentRevision, item.ReviewCount, item.CorrectCount, item.LastReviewedAtUtc)));

    public static FlashcardReviewEnvelopeResponse ToReviewResponse(FlashcardReviewView view) => new(
        new(view.Review.ReviewId, view.Review.FlashcardId, view.Review.ContentRevision,
            view.Review.Result switch
            {
                Talynor.Domain.Flashcards.FlashcardAnswerResult.Correct => "correct",
                Talynor.Domain.Flashcards.FlashcardAnswerResult.Incorrect => "incorrect",
                _ => throw new InvalidOperationException("Unknown flashcard review result.")
            }, view.Review.ReviewedAtUtc),
        new(view.CurrentReviewState.ContentRevision, view.CurrentReviewState.ReviewCount,
            view.CurrentReviewState.CorrectCount, view.CurrentReviewState.LastReviewedAtUtc));
}

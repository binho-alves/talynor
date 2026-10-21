using Talynor.Domain.Flashcards;

namespace Talynor.Application.Flashcards;

public sealed record FlashcardDetail(Guid Id, Guid KnowledgeNodeId, string Front, string Back,
    int ContentRevision, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc)
{
    public static FlashcardDetail From(Flashcard card) => new(card.Id, card.KnowledgeNodeId, card.Front, card.Back,
        card.ContentRevision, card.CreatedAtUtc, card.UpdatedAtUtc);
}
public sealed record FlashcardPage(IReadOnlyList<FlashcardDetail> Flashcards, bool HasMore);
public sealed record FlashcardCollection(IReadOnlyList<FlashcardDetail> Flashcards, int Page, int PageSize, bool HasMore);
public enum FlashcardStatus { Success, AuthenticationRequired, NotFound, InvalidInput, ContentChanged, ReviewIdConflict }
public sealed record FlashcardListResult(FlashcardStatus Status, FlashcardCollection? Collection = null);
public sealed record FlashcardResult(FlashcardStatus Status, FlashcardDetail? Card = null);
public sealed record CreateFlashcardInput(string Front, string Back);
public sealed record UpdateFlashcardInput(string? Front = null, string? Back = null,
    bool FrontSpecified = false, bool BackSpecified = false);

public sealed record FlashcardStudyNextItem(Guid Id, string Front, int ContentRevision,
    int ReviewCount, int CorrectCount, DateTimeOffset? LastReviewedAtUtc);
public sealed record FlashcardStudyNextRead(bool NodeExists, FlashcardStudyNextItem? Item);
public sealed record FlashcardReveal(Guid FlashcardId, string Back, int ContentRevision);
public sealed record FlashcardRevealRead(int CurrentContentRevision, string? Back);
public sealed record FlashcardReviewDetail(Guid ReviewId, Guid FlashcardId, int ContentRevision,
    Talynor.Domain.Flashcards.FlashcardAnswerResult Result, DateTimeOffset ReviewedAtUtc);
public sealed record FlashcardReviewStateView(int ContentRevision, int ReviewCount, int CorrectCount,
    DateTimeOffset? LastReviewedAtUtc);
public sealed record FlashcardReviewView(FlashcardReviewDetail Review, FlashcardReviewStateView CurrentReviewState);
public sealed record FlashcardStudyNextResult(FlashcardStatus Status, FlashcardStudyNextItem? Item = null);
public sealed record FlashcardRevealResult(FlashcardStatus Status, FlashcardReveal? Reveal = null);
public sealed record FlashcardReviewOperationResult(FlashcardStatus Status, FlashcardReviewView? View = null);

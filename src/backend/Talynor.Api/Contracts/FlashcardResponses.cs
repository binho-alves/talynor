using Talynor.Application.Flashcards;

namespace Talynor.Api.Contracts;

public sealed record FlashcardResponse(Guid Id, Guid KnowledgeNodeId, string Front, string Back,
    int ContentRevision, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record FlashcardCollectionResponse(IReadOnlyList<FlashcardResponse> Flashcards, int Page, int PageSize, bool HasMore);
internal static class FlashcardResponseMapping
{
    public static FlashcardResponse ToResponse(FlashcardDetail card) => new(card.Id, card.KnowledgeNodeId, card.Front,
        card.Back, card.ContentRevision, card.CreatedAtUtc, card.UpdatedAtUtc);
}

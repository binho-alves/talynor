using Talynor.Application.Workspaces;
using Talynor.Domain.Flashcards;

namespace Talynor.Application.Flashcards;

public sealed class ReviewFlashcard(GetOrCreatePersonalWorkspace getWorkspace, IFlashcardStudyStore store)
{
    public async Task<FlashcardReviewOperationResult> ExecuteAsync(Guid mapId, Guid nodeId, Guid cardId,
        Guid reviewId, int contentRevision, string? result, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (cardId == Guid.Empty || reviewId == Guid.Empty || contentRevision < 1 ||
            !TryParseResult(result, out var parsed))
            return new(FlashcardStatus.InvalidInput);
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(FlashcardStatus.AuthenticationRequired);
        return await store.ReviewAsync(workspace.Id, mapId, nodeId, cardId, reviewId, contentRevision, parsed, ct);
    }

    private static bool TryParseResult(string? value, out FlashcardAnswerResult result)
    {
        result = default;
        if (string.Equals(value, "correct", StringComparison.Ordinal))
        {
            result = FlashcardAnswerResult.Correct;
            return true;
        }
        if (string.Equals(value, "incorrect", StringComparison.Ordinal))
        {
            result = FlashcardAnswerResult.Incorrect;
            return true;
        }
        return false;
    }
}

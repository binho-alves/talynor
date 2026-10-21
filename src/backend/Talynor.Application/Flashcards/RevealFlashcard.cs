using Talynor.Application.Workspaces;

namespace Talynor.Application.Flashcards;

public sealed class RevealFlashcard(GetOrCreatePersonalWorkspace getWorkspace, IFlashcardStudyReader reader)
{
    public async Task<FlashcardRevealResult> ExecuteAsync(Guid mapId, Guid nodeId, Guid cardId,
        int contentRevision, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (cardId == Guid.Empty || contentRevision < 1) return new(FlashcardStatus.InvalidInput);
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(FlashcardStatus.AuthenticationRequired);
        var candidate = await reader.GetRevealAsync(workspace.Id, mapId, nodeId, cardId, contentRevision, ct);
        if (candidate is null) return new(FlashcardStatus.NotFound);
        if (candidate.CurrentContentRevision != contentRevision || candidate.Back is null)
            return new(FlashcardStatus.ContentChanged);
        return new(FlashcardStatus.Success, new(cardId, candidate.Back, contentRevision));
    }
}

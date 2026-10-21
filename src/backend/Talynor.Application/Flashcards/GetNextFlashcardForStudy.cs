using Talynor.Application.Workspaces;

namespace Talynor.Application.Flashcards;

public sealed class GetNextFlashcardForStudy(GetOrCreatePersonalWorkspace getWorkspace, IFlashcardStudyReader reader)
{
    public async Task<FlashcardStudyNextResult> ExecuteAsync(Guid mapId, Guid nodeId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(FlashcardStatus.AuthenticationRequired);
        var result = await reader.GetNextForStudyAsync(workspace.Id, mapId, nodeId, ct);
        return result.NodeExists ? new(FlashcardStatus.Success, result.Item) : new(FlashcardStatus.NotFound);
    }
}

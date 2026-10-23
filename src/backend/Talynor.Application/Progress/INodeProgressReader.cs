namespace Talynor.Application.Progress;

public interface INodeProgressReader
{
    Task<NodeProgressSummary?> ReadAsync(Guid workspaceId, Guid mapId, Guid nodeId,
        NodeProgressPeriod period, CancellationToken ct);
}

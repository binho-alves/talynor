using Talynor.Application.Workspaces;

namespace Talynor.Application.Progress;

public sealed class GetNodeProgress(GetOrCreatePersonalWorkspace getWorkspace, INodeProgressReader reader,
    TimeProvider timeProvider)
{
    public async Task<NodeProgressResult> ExecuteAsync(Guid mapId, Guid nodeId, int days, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (mapId == Guid.Empty || nodeId == Guid.Empty || !NodeProgressPeriod.Supports(days))
            return new(NodeProgressStatus.Invalid);
        var workspace = await getWorkspace.ExecuteAsync(ct);
        if (workspace is null) return new(NodeProgressStatus.AuthenticationRequired);
        var period = NodeProgressPeriod.Create(days, timeProvider.GetUtcNow());
        var summary = await reader.ReadAsync(workspace.Id, mapId, nodeId, period, ct);
        return summary is null ? new(NodeProgressStatus.NotFound) : new(NodeProgressStatus.Success, summary);
        // As with existing reads, cancellation/provider/clock/corrupt-data failures propagate;
        // a later HTTP boundary sanitizes them instead of inventing an empty summary.
    }
}

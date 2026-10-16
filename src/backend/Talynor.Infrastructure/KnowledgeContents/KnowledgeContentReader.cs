using Microsoft.EntityFrameworkCore;
using Talynor.Application.KnowledgeContents;
using Talynor.Infrastructure.Persistence;

namespace Talynor.Infrastructure.KnowledgeContents;

public sealed class KnowledgeContentReader(TalynorDbContext db) : IKnowledgeContentReader
{
    public async Task<IReadOnlyList<KnowledgeContentSummary>?> ListForNodeAsync(Guid workspaceId, Guid mapId, Guid nodeId, CancellationToken ct)
    {
        // Parent existence and summaries share a snapshot; no N+1 or ownership check after loading.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var exists = await db.KnowledgeNodes.AsNoTracking().AnyAsync(node => node.Id == nodeId && node.KnowledgeMapId == mapId &&
            db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId), ct);
        if (!exists) return null;
        var contents = await db.KnowledgeContents.AsNoTracking().Where(content => content.KnowledgeNodeId == nodeId &&
            db.KnowledgeNodes.Any(node => node.Id == content.KnowledgeNodeId && node.KnowledgeMapId == mapId &&
                db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId)))
            .OrderBy(content => content.CreatedAtUtc).ThenBy(content => content.Id)
            .Select(content => new KnowledgeContentSummary(content.Id, content.Type, content.Title,
                content.CreatedAtUtc, content.UpdatedAtUtc, content.Url)).ToArrayAsync(ct);
        await transaction.CommitAsync(ct);
        return Array.AsReadOnly(contents);
    }

    public Task<KnowledgeContentDetail?> GetForNodeAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid contentId, CancellationToken ct) =>
        db.KnowledgeContents.AsNoTracking().Where(content => content.Id == contentId && content.KnowledgeNodeId == nodeId &&
            db.KnowledgeNodes.Any(node => node.Id == nodeId && node.KnowledgeMapId == mapId &&
                db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId)))
            .Select(content => new KnowledgeContentDetail(content.Id, content.Type, content.Title, content.Body,
                content.Url, content.CreatedAtUtc, content.UpdatedAtUtc)).SingleOrDefaultAsync(ct);
}

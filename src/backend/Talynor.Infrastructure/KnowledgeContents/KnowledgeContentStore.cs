using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Talynor.Application.KnowledgeContents;
using Talynor.Domain.KnowledgeContents;
using Talynor.Infrastructure.Persistence;

namespace Talynor.Infrastructure.KnowledgeContents;

public sealed class KnowledgeContentStore(TalynorDbContext db) : IKnowledgeContentStore
{
    public Task<bool> NodeExistsAsync(Guid workspaceId, Guid mapId, Guid nodeId, CancellationToken ct) =>
        db.KnowledgeNodes.AsNoTracking().AnyAsync(node => node.Id == nodeId && node.KnowledgeMapId == mapId &&
            db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId), ct);

    public Task<KnowledgeContent?> GetForNodeAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid contentId, CancellationToken ct) =>
        db.KnowledgeContents.SingleOrDefaultAsync(content => content.Id == contentId && content.KnowledgeNodeId == nodeId &&
            db.KnowledgeNodes.Any(node => node.Id == nodeId && node.KnowledgeMapId == mapId &&
                db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId)), ct);

    public void Add(KnowledgeContent content) => db.KnowledgeContents.Add(content);
    public void Remove(KnowledgeContent content) => db.KnowledgeContents.Remove(content);

    public async Task<KnowledgeContentWriteStatus> SaveAsync(KnowledgeContentWriteAttempt attempt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await db.SaveChangesAsync(ct);
            return KnowledgeContentWriteStatus.Success;
        }
        catch (DbUpdateConcurrencyException) when (attempt.Operation is KnowledgeContentWriteOperation.Update or KnowledgeContentWriteOperation.Delete)
        {
            return KnowledgeContentWriteStatus.NotFound;
        }
        catch (DbUpdateException error) when (attempt.Operation == KnowledgeContentWriteOperation.Create &&
            error.InnerException is SqliteException { SqliteErrorCode: 19, SqliteExtendedErrorCode: 787 })
        {
            // Only a confirmed vanished/scoped-out parent becomes 404; unknown failures remain operational.
            if (!await NodeExistsAsync(attempt.WorkspaceId, attempt.MapId, attempt.NodeId, ct))
                return KnowledgeContentWriteStatus.NotFound;
            throw;
        }
    }
}

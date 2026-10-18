using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Talynor.Application.KnowledgeMaps;
using Talynor.Domain.KnowledgeMaps;
using Talynor.Infrastructure.Persistence;

namespace Talynor.Infrastructure.KnowledgeMaps;

public sealed class KnowledgeMapStore(TalynorDbContext db) : IKnowledgeMapStore
{
    public Task<KnowledgeMap?> GetMapAsync(Guid mapId, Guid workspaceId, CancellationToken ct) =>
        db.KnowledgeMaps.SingleOrDefaultAsync(map => map.Id == mapId && map.WorkspaceId == workspaceId, ct);
    public Task<KnowledgeNode?> GetNodeAsync(Guid nodeId, Guid mapId, Guid workspaceId, CancellationToken ct) =>
        db.KnowledgeNodes.SingleOrDefaultAsync(node => node.Id == nodeId && node.KnowledgeMapId == mapId &&
            db.KnowledgeMaps.Any(map => map.Id == node.KnowledgeMapId && map.WorkspaceId == workspaceId), ct);
    public Task<KnowledgeEdge?> GetEdgeAsync(Guid edgeId, Guid mapId, Guid workspaceId, CancellationToken ct) =>
        db.KnowledgeEdges.SingleOrDefaultAsync(edge => edge.Id == edgeId && edge.KnowledgeMapId == mapId &&
            db.KnowledgeMaps.Any(map => map.Id == edge.KnowledgeMapId && map.WorkspaceId == workspaceId), ct);
    public Task<bool> MapInUseAsync(Guid mapId, Guid workspaceId, CancellationToken ct) =>
        db.KnowledgeNodes.AnyAsync(node => (node.ChildMapId == mapId ||
            node.KnowledgeMapId == mapId && db.StudySessions.Any(session => session.KnowledgeNodeId == node.Id)) &&
            db.KnowledgeMaps.Any(map => map.Id == node.KnowledgeMapId && map.WorkspaceId == workspaceId), ct);
    public Task<bool> NodeInUseAsync(Guid nodeId, Guid mapId, Guid workspaceId, CancellationToken ct) =>
        db.KnowledgeNodes.AnyAsync(node => node.Id == nodeId && node.KnowledgeMapId == mapId &&
            db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId) &&
            (db.KnowledgeEdges.Any(edge => edge.KnowledgeMapId == mapId && (edge.SourceNodeId == nodeId || edge.TargetNodeId == nodeId)) ||
             db.StudySessions.Any(session => session.KnowledgeNodeId == nodeId)), ct);
    public void AddMap(KnowledgeMap map) => db.KnowledgeMaps.Add(map);
    public void AddNode(KnowledgeNode node) => db.KnowledgeNodes.Add(node);
    public void AddEdge(KnowledgeEdge edge) => db.KnowledgeEdges.Add(edge);
    public void RemoveMap(KnowledgeMap map) => db.KnowledgeMaps.Remove(map);
    public void RemoveNode(KnowledgeNode node) => db.KnowledgeNodes.Remove(node);
    public void RemoveEdge(KnowledgeEdge edge) => db.KnowledgeEdges.Remove(edge);

    public async Task<KnowledgeWriteStatus> SaveAsync(KnowledgeWriteAttempt attempt, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct); // Native atomic SaveChanges; no extra transaction wrapper.
            return KnowledgeWriteStatus.Success;
        }
        catch (DbUpdateConcurrencyException) when (attempt.Operation is KnowledgeWriteOperation.UpdateMap or
            KnowledgeWriteOperation.DeleteMap or KnowledgeWriteOperation.UpdateNode or
            KnowledgeWriteOperation.DeleteNode or KnowledgeWriteOperation.DeleteEdge)
        {
            // No concurrency token exists: a tracked UPDATE/DELETE affected zero rows.
            return KnowledgeWriteStatus.NotFound;
        }
        catch (DbUpdateException error) when (IsRelationConstraint(error))
        {
            // Recheck the exact expected condition after rollback; never relabel busy/PK/other failures.
            var ownMapExists = await db.KnowledgeMaps.AsNoTracking()
                .AnyAsync(map => map.Id == attempt.MapId && map.WorkspaceId == attempt.WorkspaceId, ct);
            if (!ownMapExists && attempt.Operation != KnowledgeWriteOperation.CreateMap)
                return KnowledgeWriteStatus.NotFound;
            if (attempt.Operation == KnowledgeWriteOperation.DeleteMap &&
                await MapInUseAsync(attempt.MapId, attempt.WorkspaceId, ct)) return KnowledgeWriteStatus.MapInUse;
            if (attempt.Operation == KnowledgeWriteOperation.DeleteNode && attempt.ResourceId.HasValue &&
                await NodeInUseAsync(attempt.ResourceId.Value, attempt.MapId, attempt.WorkspaceId, ct)) return KnowledgeWriteStatus.NodeInUse;
            if (attempt.Operation is KnowledgeWriteOperation.CreateNode or KnowledgeWriteOperation.UpdateNode)
            {
                var node = db.ChangeTracker.Entries<KnowledgeNode>().Single(entry => entry.Entity.Id == attempt.ResourceId).Entity;
                if (node.ChildMapId.HasValue && !await db.KnowledgeMaps.AsNoTracking().AnyAsync(
                    map => map.Id == node.ChildMapId && map.WorkspaceId == attempt.WorkspaceId, ct))
                    return KnowledgeWriteStatus.InvalidChildMap;
            }
            if (attempt.Operation == KnowledgeWriteOperation.CreateEdge)
            {
                var edge = db.ChangeTracker.Entries<KnowledgeEdge>().Single(entry => entry.Entity.Id == attempt.ResourceId).Entity;
                var source = await db.KnowledgeNodes.AsNoTracking().AnyAsync(node => node.Id == edge.SourceNodeId && node.KnowledgeMapId == attempt.MapId, ct);
                var target = await db.KnowledgeNodes.AsNoTracking().AnyAsync(node => node.Id == edge.TargetNodeId && node.KnowledgeMapId == attempt.MapId, ct);
                if (!source || !target) return KnowledgeWriteStatus.InvalidEdgeNodes;
            }
            throw;
        }
    }

    private static bool IsRelationConstraint(DbUpdateException error) => error.InnerException is SqliteException sqlite &&
        sqlite.SqliteErrorCode == 19 && (sqlite.SqliteExtendedErrorCode == 787 ||
            sqlite.SqliteExtendedErrorCode == 1811 && sqlite.Message is
                "SQLite Error 19: 'Child map must belong to the same workspace.'." or
                "SQLite Error 19: 'FOREIGN KEY constraint failed'.");
}

using Microsoft.EntityFrameworkCore;
using Talynor.Application.StudySessions;
using Talynor.Infrastructure.Persistence;

namespace Talynor.Infrastructure.StudySessions;

public sealed class StudySessionReader(TalynorDbContext db) : IStudySessionReader
{
    public Task<ActiveStudySession?> GetActiveAsync(Guid workspaceId, CancellationToken ct) =>
        (from session in db.StudySessions.AsNoTracking()
         join node in db.KnowledgeNodes on session.KnowledgeNodeId equals node.Id
         join map in db.KnowledgeMaps on node.KnowledgeMapId equals map.Id
         where session.EndedAtUtc == null && map.WorkspaceId == workspaceId
         select new ActiveStudySession(new(session.Id, node.Id, session.StartedAtUtc, session.EndedAtUtc),
             map.Id, node.Id, node.Title)).SingleOrDefaultAsync(ct);

    public async Task<StudySessionPage?> ListCompletedForNodeAsync(Guid workspaceId, Guid mapId, Guid nodeId,
        int offset, int pageSize, CancellationToken ct)
    {
        // Parent existence and the page share a read snapshot, following the content reader.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var exists = await db.KnowledgeNodes.AsNoTracking().AnyAsync(node => node.Id == nodeId && node.KnowledgeMapId == mapId &&
            db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId), ct);
        if (!exists) return null;
        var sessions = await db.StudySessions.AsNoTracking().Where(session => session.KnowledgeNodeId == nodeId &&
                session.EndedAtUtc != null && db.KnowledgeNodes.Any(node => node.Id == session.KnowledgeNodeId && node.KnowledgeMapId == mapId &&
                    db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId)))
            .OrderByDescending(session => session.StartedAtUtc).ThenByDescending(session => session.Id)
            .Skip(offset).Take(pageSize + 1)
            .Select(session => new StudySessionReadModel(session.Id, session.KnowledgeNodeId, session.StartedAtUtc, session.EndedAtUtc))
            .ToArrayAsync(ct);
        await transaction.CommitAsync(ct);
        return new(Array.AsReadOnly(sessions.Take(pageSize).ToArray()), sessions.Length > pageSize);
    }

    public Task<StudySessionReadModel?> GetForNodeAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid sessionId, CancellationToken ct) =>
        db.StudySessions.AsNoTracking().Where(session => session.Id == sessionId && session.KnowledgeNodeId == nodeId &&
            db.KnowledgeNodes.Any(node => node.Id == session.KnowledgeNodeId && node.KnowledgeMapId == mapId &&
                db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId)))
            .Select(session => new StudySessionReadModel(session.Id, session.KnowledgeNodeId, session.StartedAtUtc, session.EndedAtUtc))
            .SingleOrDefaultAsync(ct);
}

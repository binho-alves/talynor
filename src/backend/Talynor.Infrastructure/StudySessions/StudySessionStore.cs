using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Talynor.Application.StudySessions;
using Talynor.Domain.StudySessions;
using Talynor.Infrastructure.Persistence;

namespace Talynor.Infrastructure.StudySessions;

public sealed class StudySessionStore(TalynorDbContext db) : IStudySessionStore
{
    public async Task<StartStudySessionResult> StartAsync(Guid workspaceId, Guid mapId, StudySession session, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            // Microsoft.Data.Sqlite 10.0.12 emits BEGIN IMMEDIATE for deferred:false.
            // The provider's synchronous lock wait is bounded by DefaultTimeout; cancellation
            // is checked on both sides of it. No application retry or replay is performed.
            await using var transaction = connection.BeginTransaction(deferred: false);
            ct.ThrowIfCancellationRequested();
            await using var enlisted = await db.Database.UseTransactionAsync(transaction, ct);
            if (!await db.KnowledgeNodes.AsNoTracking().AnyAsync(node => node.Id == session.KnowledgeNodeId &&
                node.KnowledgeMapId == mapId && db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId), ct))
                return new(StudySessionWriteStatus.NotFound);
            if (await db.StudySessions.AnyAsync(existing => existing.EndedAtUtc == null &&
                db.KnowledgeNodes.Any(node => node.Id == existing.KnowledgeNodeId &&
                    db.KnowledgeMaps.Any(map => map.Id == node.KnowledgeMapId && map.WorkspaceId == workspaceId)), ct))
                return new(StudySessionWriteStatus.ActiveSessionExists);
            db.StudySessions.Add(session);
            await db.SaveChangesAsync(ct);
            var persisted = await db.StudySessions.AsNoTracking().Where(value => value.Id == session.Id)
                .Select(value => new StudySessionReadModel(value.Id, value.KnowledgeNodeId, value.StartedAtUtc, value.EndedAtUtc))
                .SingleAsync(ct);
            await transaction.CommitAsync(ct);
            return new(StudySessionWriteStatus.Success, persisted);
        }
        finally
        {
            // Also detach a rolled-back insert so a later SaveChanges cannot replay it.
            db.Entry(session).State = EntityState.Detached;
            await db.Database.CloseConnectionAsync();
        }
    }

    public async Task<StudySessionWriteStatus> CompleteAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid sessionId,
        DateTimeOffset now, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var end = now.ToUniversalTime();
        // Atomic equivalent of Domain.TryComplete: first valid completion wins.
        var affected = await Scoped(workspaceId, mapId, nodeId, sessionId)
            .Where(session => session.EndedAtUtc == null && session.StartedAtUtc <= end)
            .ExecuteUpdateAsync(update => update.SetProperty(session => session.EndedAtUtc, (DateTimeOffset?)end), ct);
        if (affected == 1) return StudySessionWriteStatus.Success;
        var existing = await Scoped(workspaceId, mapId, nodeId, sessionId).SingleOrDefaultAsync(ct);
        if (existing is null) return StudySessionWriteStatus.NotFound;
        return existing.EndedAtUtc.HasValue ? StudySessionWriteStatus.Success : StudySessionWriteStatus.ClockConflict;
    }

    public async Task<StudySessionWriteStatus> DeleteAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await Scoped(workspaceId, mapId, nodeId, sessionId).ExecuteDeleteAsync(ct) == 1 ?
            StudySessionWriteStatus.Success : StudySessionWriteStatus.NotFound;
    }

    private IQueryable<StudySession> Scoped(Guid workspaceId, Guid mapId, Guid nodeId, Guid sessionId) =>
        db.StudySessions.AsNoTracking().Where(session => session.Id == sessionId && session.KnowledgeNodeId == nodeId &&
            db.KnowledgeNodes.Any(node => node.Id == nodeId && node.KnowledgeMapId == mapId &&
                db.KnowledgeMaps.Any(map => map.Id == mapId && map.WorkspaceId == workspaceId)));
}

using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Application.KnowledgeMaps;
using Talynor.Application.StudySessions;
using Talynor.Domain.KnowledgeMaps;
using Talynor.Domain.StudySessions;
using Talynor.Infrastructure.KnowledgeMaps;
using Talynor.Infrastructure.Persistence;
using Talynor.Infrastructure.StudySessions;

namespace Talynor.IntegrationTests;

public class StudySessionConcurrencyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddTicks(1234567);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)]
    public async Task Simultaneous_start_same_workspace_same_or_different_node_and_different_workspace(bool sameNode, bool differentWorkspace)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); var (f, cs) = await Seed(factory);
        var hold = new HoldFirstRead(); var attempted = Signal();
        using var c1 = new ProbeConnection(cs); using var c2 = new ProbeConnection(cs, () => attempted.TrySetResult());
        await using var d1 = Context(c1, hold); await using var d2 = Context(c2);
        var second = differentWorkspace ? f.ChildNode : sameNode ? f.First : f.Second;
        // Child normally belongs to the same workspace. Move an unused map to the other workspace for this case.
        if (differentWorkspace)
        {
            using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            second = KnowledgeNode.Create(f.OtherMap.Id, "Other", null, 0, 0, null, Now);
            db.KnowledgeNodes.Add(second); await db.SaveChangesAsync();
        }
        var start1 = Task.Run(() => new StudySessionStore(d1).StartAsync(f.Workspace.Id, f.Map.Id, StudySession.Start(f.First.Id, Now), CancellationToken.None));
        await hold.Entered.Task.WaitAsync(Timeout);
        var secondNode = second;
        var start2 = Task.Run(() => new StudySessionStore(d2).StartAsync(differentWorkspace ? f.OtherWorkspace.Id : f.Workspace.Id,
            differentWorkspace ? f.OtherMap.Id : f.Map.Id, StudySession.Start(secondNode.Id, Now), CancellationToken.None));
        try { await attempted.Task.WaitAsync(Timeout); } finally { hold.Release.TrySetResult(); }
        var results = await Task.WhenAll(start1, start2).WaitAsync(Timeout);
        Assert.Equal(StudySessionWriteStatus.Success, results[0].Status);
        Assert.Equal(differentWorkspace ? StudySessionWriteStatus.Success : StudySessionWriteStatus.ActiveSessionExists, results[1].Status);
        Assert.False(c1.Deferred); Assert.False(c2.Deferred); Assert.NotSame(c1, c2);
        using var verify = factory.Services.CreateScope();
        Assert.Equal(differentWorkspace ? 2 : 1, await verify.ServiceProvider.GetRequiredService<TalynorDbContext>().StudySessions.CountAsync(s => s.EndedAtUtc == null));
        Assert.Null(d1.Database.CurrentTransaction); Assert.Null(d2.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Simultaneous_complete_complete_preserves_timestamp_of_the_single_winning_update()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); var (f, cs) = await Seed(factory);
        var id = await Active(factory, f); var pair = new PairGate();
        var p1 = new PairedWrite(pair); var p2 = new PairedWrite(pair);
        using var c1 = new ProbeConnection(cs); using var c2 = new ProbeConnection(cs);
        await using var d1 = Context(c1, p1); await using var d2 = Context(c2, p2);
        var end1 = Now.AddTicks(1); var end2 = Now.AddHours(1);
        var a = Task.Run(() => new StudySessionStore(d1).CompleteAsync(f.Workspace.Id, f.Map.Id, f.First.Id, id, end1, CancellationToken.None));
        var b = Task.Run(() => new StudySessionStore(d2).CompleteAsync(f.Workspace.Id, f.Map.Id, f.First.Id, id, end2, CancellationToken.None));
        var results = await Task.WhenAll(a, b).WaitAsync(Timeout);
        Assert.All(results, result => Assert.Equal(StudySessionWriteStatus.Success, result));
        Assert.Equal(1, p1.Affected + p2.Affected);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        Assert.Equal(p1.Affected == 1 ? end1 : end2, (await db.StudySessions.SingleAsync()).EndedAtUtc);
        Assert.Equal(StudySessionWriteStatus.Success, await new StudySessionStore(db).CompleteAsync(f.Workspace.Id, f.Map.Id, f.First.Id, id, Now.AddDays(2), CancellationToken.None));
        db.ChangeTracker.Clear(); Assert.Equal(p1.Affected == 1 ? end1 : end2, (await db.StudySessions.SingleAsync()).EndedAtUtc);
    }

    [Fact]
    public async Task Simultaneous_complete_delete_has_no_resurrection_or_partial_record()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); var (f, cs) = await Seed(factory);
        var id = await Active(factory, f); var pair = new PairGate();
        using var c1 = new ProbeConnection(cs); using var c2 = new ProbeConnection(cs);
        await using var d1 = Context(c1, new PairedWrite(pair)); await using var d2 = Context(c2, new PairedWrite(pair));
        var complete = Task.Run(() => new StudySessionStore(d1).CompleteAsync(f.Workspace.Id, f.Map.Id, f.First.Id, id, Now.AddSeconds(1), CancellationToken.None));
        var delete = Task.Run(() => new StudySessionStore(d2).DeleteAsync(f.Workspace.Id, f.Map.Id, f.First.Id, id, CancellationToken.None));
        await Task.WhenAll(complete, delete).WaitAsync(Timeout);
        Assert.Contains(await complete, new[] { StudySessionWriteStatus.Success, StudySessionWriteStatus.NotFound });
        Assert.Equal(StudySessionWriteStatus.Success, await delete);
        using var scope = factory.Services.CreateScope(); Assert.Empty(await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().StudySessions.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Simultaneous_start_node_or_map_delete_preserves_fk_and_classifies_conflict(bool map)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); var (f, cs) = await Seed(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var seedDb = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            await seedDb.KnowledgeEdges.ExecuteDeleteAsync();
            seedDb.KnowledgeContents.Add(Talynor.Domain.KnowledgeContents.KnowledgeContent.Create(f.First.Id,
                Talynor.Domain.KnowledgeContents.KnowledgeContentType.Note, null, "Retained history context", null, Now));
            await seedDb.SaveChangesAsync();
        }
        var hold = new HoldFirstRead(); var deleting = new DeleteAttempt();
        using var c1 = new ProbeConnection(cs); using var c2 = new ProbeConnection(cs);
        await using var d1 = Context(c1, hold); await using var d2 = Context(c2, deleting);
        var start = Task.Run(() => new StudySessionStore(d1).StartAsync(f.Workspace.Id, f.Map.Id, StudySession.Start(f.First.Id, Now), CancellationToken.None));
        await hold.Entered.Task.WaitAsync(Timeout);
        var delete = Task.Run(async () =>
        {
            var store = new KnowledgeMapStore(d2);
            if (map) store.RemoveMap((await store.GetMapAsync(f.Map.Id, f.Workspace.Id, CancellationToken.None))!);
            else store.RemoveNode((await store.GetNodeAsync(f.First.Id, f.Map.Id, f.Workspace.Id, CancellationToken.None))!);
            // A stale precheck can pass before start commits; the real FK and classifier must still protect history.
            Assert.False(map ? await store.MapInUseAsync(f.Map.Id, f.Workspace.Id, CancellationToken.None) :
                await store.NodeInUseAsync(f.First.Id, f.Map.Id, f.Workspace.Id, CancellationToken.None));
            return await store.SaveAsync(new(map ? KnowledgeWriteOperation.DeleteMap : KnowledgeWriteOperation.DeleteNode,
                f.Workspace.Id, f.Map.Id, map ? f.Map.Id : f.First.Id), CancellationToken.None);
        });
        try { await deleting.Entered.Task.WaitAsync(Timeout); } finally { hold.Release.TrySetResult(); }
        Assert.Equal(StudySessionWriteStatus.Success, (await start.WaitAsync(Timeout)).Status);
        Assert.Equal(map ? KnowledgeWriteStatus.MapInUse : KnowledgeWriteStatus.NodeInUse, await delete.WaitAsync(Timeout));
        using var verify = factory.Services.CreateScope(); var db = verify.ServiceProvider.GetRequiredService<TalynorDbContext>();
        Assert.True(await db.KnowledgeMaps.AnyAsync(m => m.Id == f.Map.Id));
        Assert.Equal(2, await db.KnowledgeNodes.CountAsync(n => n.KnowledgeMapId == f.Map.Id));
        Assert.Single(await db.StudySessions.ToArrayAsync());
        Assert.Single(await db.KnowledgeContents.ToArrayAsync());
        await db.Database.OpenConnectionAsync();
        using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = "PRAGMA foreign_key_check";
        using var violations = await command.ExecuteReaderAsync(); Assert.False(await violations.ReadAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Parent_delete_winning_before_start_is_generic_not_found_without_orphan(bool map)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); var (f, cs) = await Seed(factory);
        using var c = new ProbeConnection(cs); await using var db = Context(c);
        await db.KnowledgeEdges.ExecuteDeleteAsync();
        if (map) await db.KnowledgeMaps.Where(m => m.Id == f.Map.Id).ExecuteDeleteAsync();
        else await db.KnowledgeNodes.Where(n => n.Id == f.First.Id).ExecuteDeleteAsync();
        Assert.Equal(StudySessionWriteStatus.NotFound, (await new StudySessionStore(db).StartAsync(f.Workspace.Id, f.Map.Id, StudySession.Start(f.First.Id, Now), CancellationToken.None)).Status);
        Assert.Empty(await db.StudySessions.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Failed_or_cancelled_start_rolls_back_insert_detaches_and_allows_context_reuse(bool cancel)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); var (f, cs) = await Seed(factory);
        using var cancellation = new CancellationTokenSource(); var fault = new AfterInsertFault(cancel ? cancellation : null);
        using var c = new ProbeConnection(cs); await using var db = Context(c, fault); var store = new StudySessionStore(db);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.StartAsync(f.Workspace.Id, f.Map.Id, StudySession.Start(f.First.Id, Now), cancellation.Token));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => store.StartAsync(f.Workspace.Id, f.Map.Id, StudySession.Start(f.First.Id, Now), CancellationToken.None));
        Assert.Null(db.Database.CurrentTransaction); Assert.Empty(db.ChangeTracker.Entries<StudySession>());
        Assert.Equal(System.Data.ConnectionState.Closed, c.State); Assert.Empty(await db.StudySessions.ToArrayAsync());
        fault.Enabled = false;
        Assert.Equal(StudySessionWriteStatus.Success, (await store.StartAsync(f.Workspace.Id, f.Map.Id, StudySession.Start(f.First.Id, Now), CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Busy_start_is_operational_not_active_conflict_and_connection_is_cleaned_up()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); var (f, cs) = await Seed(factory);
        using var blocker = new SqliteConnection(cs); await blocker.OpenAsync(); using var held = blocker.BeginTransaction(deferred: false);
        using var c = new ProbeConnection(new SqliteConnectionStringBuilder(cs) { DefaultTimeout = 1 }.ConnectionString);
        await using var db = Context(c);
        var error = await Assert.ThrowsAsync<SqliteException>(() => Task.Run(() => new StudySessionStore(db).StartAsync(
            f.Workspace.Id, f.Map.Id, StudySession.Start(f.First.Id, Now), CancellationToken.None)));
        Assert.Contains(error.SqliteErrorCode, new[] { 5, 6 }); Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(System.Data.ConnectionState.Closed, c.State);
        held.Rollback();
        Assert.Equal(StudySessionWriteStatus.Success, (await new StudySessionStore(db).StartAsync(f.Workspace.Id, f.Map.Id, StudySession.Start(f.First.Id, Now), CancellationToken.None)).Status);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TalynorDbContext Context(SqliteConnection connection, params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<TalynorDbContext>().UseSqlite(connection).AddInterceptors(interceptors).Options);
    private static async Task<(KnowledgeGraphFixture Fixture, string Connection)> Seed(RegistrationTestFactory factory)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        return (await KnowledgeGraphFixture.CreateAsync(db), db.Database.GetConnectionString()!);
    }
    private static async Task<Guid> Active(RegistrationTestFactory factory, KnowledgeGraphFixture f)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var result = await new StudySessionStore(db).StartAsync(f.Workspace.Id, f.Map.Id, StudySession.Start(f.First.Id, Now), CancellationToken.None);
        return result.Session!.Id;
    }
    private sealed class ProbeConnection(string cs, Action? attempted = null) : SqliteConnection(cs)
    {
        public bool Deferred = true;
        public override SqliteTransaction BeginTransaction(bool deferred) { Deferred = deferred; attempted?.Invoke(); return base.BeginTransaction(deferred); }
    }
    private sealed class HoldFirstRead : DbCommandInterceptor
    {
        public TaskCompletionSource Entered = Signal(), Release = Signal(); private int count;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref count) == 1) { Assert.NotNull(command.Transaction); Entered.TrySetResult(); await Release.Task.WaitAsync(Timeout, ct); }
            return result;
        }
    }
    private sealed class PairGate
    {
        private int count; private readonly TaskCompletionSource ready = Signal();
        public async Task Enter(CancellationToken ct) { if (Interlocked.Increment(ref count) == 2) ready.TrySetResult(); await ready.Task.WaitAsync(Timeout, ct); }
    }
    private sealed class PairedWrite(PairGate pair) : DbCommandInterceptor
    {
        public int Affected;
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken ct = default) { await pair.Enter(ct); return result; }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result, CancellationToken ct = default)
        { Affected = result; return ValueTask.FromResult(result); }
    }
    private sealed class DeleteAttempt : DbCommandInterceptor
    {
        public TaskCompletionSource Entered = Signal();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { if (command.CommandText.StartsWith("DELETE", StringComparison.Ordinal)) Entered.TrySetResult(); return ValueTask.FromResult(result); }
    }
    private sealed class AfterInsertFault(CancellationTokenSource? cancellation) : SaveChangesInterceptor
    {
        public bool Enabled = true;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (Enabled) { if (cancellation is null) throw new InvalidOperationException("Synthetic after-insert failure."); cancellation.Cancel(); ct.ThrowIfCancellationRequested(); }
            return ValueTask.FromResult(result);
        }
    }
}

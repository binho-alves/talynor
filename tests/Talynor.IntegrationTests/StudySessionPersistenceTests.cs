using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Application.KnowledgeMaps;
using Talynor.Application.StudySessions;
using Talynor.Domain.StudySessions;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class StudySessionPersistenceTests
{
    [Fact]
    public async Task Utc_roundtrip_four_columns_and_untracked_sql_pagination_use_descending_start_then_id_without_count()
    {
        var commands = new Commands();
        using var factory = new RegistrationTestFactory(configureServices: services =>
            services.AddDbContext<TalynorDbContext>(options => options.AddInterceptors(commands)));
        await factory.InitializeAsync(); StudySessionFixture f;
        using (var seed = factory.Services.CreateScope()) f = await StudySessionFixture.CreateAsync(seed.ServiceProvider.GetRequiredService<TalynorDbContext>());
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var stored = await db.StudySessions.SingleAsync(session => session.Id == f.Low.Id);
        Assert.Equal(StudySessionFixture.Instant, stored.StartedAtUtc);
        Assert.Equal(f.Low.EndedAtUtc, stored.EndedAtUtc);
        Assert.Equal(TimeSpan.Zero, stored.StartedAtUtc.Offset); Assert.Equal(TimeSpan.Zero, stored.EndedAtUtc!.Value.Offset);
        Assert.Equal(TimeSpan.FromTicks(1234567), stored.Duration); Assert.Equal(StudySessionStatus.Completed, stored.Status);
        var model = db.Model.FindEntityType(typeof(StudySession))!;
        Assert.Equal(new[] { "EndedAtUtc", "Id", "KnowledgeNodeId", "StartedAtUtc" }, model.GetProperties().Select(p => p.Name).Order());
        Assert.Equal(typeof(DateTime), model.FindProperty("StartedAtUtc")!.GetValueConverter()!.ProviderClrType);
        Assert.Equal(typeof(DateTime), model.FindProperty("EndedAtUtc")!.GetValueConverter()!.ProviderClrType);
        Assert.Equal(DeleteBehavior.Restrict, Assert.Single(model.GetForeignKeys()).DeleteBehavior);
        db.ChangeTracker.Clear(); commands.Sql.Clear();
        var reader = scope.ServiceProvider.GetRequiredService<IStudySessionReader>();
        var first = await reader.ListCompletedForNodeAsync(f.Graph.Workspace.Id, f.Graph.Map.Id, f.Graph.First.Id, 0, 2, CancellationToken.None);
        Assert.Equal(new[] { f.High.Id, f.Low.Id }, first!.Sessions.Select(s => s.Id)); Assert.True(first.HasMore);
        Assert.All(first.Sessions, s => Assert.NotNull(s.DurationSeconds));
        Assert.Equal(2, commands.Sql.Count);
        var query = commands.Sql[1]; var sql = query.Text.Replace("\"", "");
        Assert.DoesNotContain("COUNT(", sql.ToUpperInvariant());
        Assert.Contains("WorkspaceId", sql); Assert.Contains("KnowledgeMapId", sql);
        Assert.Contains("EndedAtUtc IS NOT NULL", sql);
        const RegexOptions flags = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        var source = Regex.Match(sql, @"\bFROM\s+StudySessions\s+(?:AS\s+)?(?<alias>\w+)\b", flags);
        Assert.True(source.Success); var alias = Regex.Escape(source.Groups["alias"].Value);
        Assert.Matches(new Regex($@"\bORDER\s+BY\s+{alias}\.StartedAtUtc\s+DESC\s*,\s*{alias}\.Id\s+DESC\s+LIMIT", flags), sql);
        var limits = Regex.Match(sql, @"LIMIT\s+(?<take>@\w+)\s+OFFSET\s+(?<skip>@\w+)", flags);
        Assert.True(limits.Success);
        Assert.Equal(3L, Convert.ToInt64(query.Parameters[limits.Groups["take"].Value]));
        Assert.Equal(0L, Convert.ToInt64(query.Parameters[limits.Groups["skip"].Value]));
        var second = await reader.ListCompletedForNodeAsync(f.Graph.Workspace.Id, f.Graph.Map.Id, f.Graph.First.Id, 2, 2, CancellationToken.None);
        Assert.Equal(f.Early.Id, Assert.Single(second!.Sessions).Id); Assert.False(second.HasMore);
        var exact = await reader.ListCompletedForNodeAsync(f.Graph.Workspace.Id, f.Graph.Map.Id, f.Graph.First.Id, 0, 3, CancellationToken.None);
        Assert.Equal(3, exact!.Sessions.Count); Assert.False(exact.HasMore);
        var past = await reader.ListCompletedForNodeAsync(f.Graph.Workspace.Id, f.Graph.Map.Id, f.Graph.First.Id, 3, 2, CancellationToken.None);
        Assert.Empty(past!.Sessions); Assert.False(past.HasMore);
        commands.Sql.Clear();
        var active = await reader.GetForNodeAsync(f.Graph.Workspace.Id, f.Graph.Map.Id, f.Graph.First.Id, f.Active.Id, CancellationToken.None);
        Assert.Null(active!.EndedAtUtc); Assert.Null(active.DurationSeconds);
        Assert.Single(commands.Sql); Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(await reader.ListCompletedForNodeAsync(f.Graph.OtherWorkspace.Id, f.Graph.Map.Id, f.Graph.First.Id, 0, 20, CancellationToken.None));
        Assert.Null(await reader.GetForNodeAsync(f.Graph.Workspace.Id, f.Graph.Map.Id, f.Graph.Second.Id, f.Low.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Required_node_fk_prevents_orphan_sessions()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        db.StudySessions.Add(new(Guid.NewGuid(), Guid.NewGuid(), StudySessionFixture.Instant, null));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(787, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
    }

    [Fact]
    public async Task Database_check_rejects_negative_interval_even_for_direct_sql()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var f = await StudySessionFixture.CreateAsync(db);
        var end = StudySessionFixture.Instant.AddTicks(-1).UtcDateTime;
        var error = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE StudySessions SET EndedAtUtc = {end} WHERE Id = {f.High.Id}"));
        Assert.Equal(275, error.SqliteExtendedErrorCode);
        db.ChangeTracker.Clear(); Assert.Equal(TimeSpan.Zero, (await db.StudySessions.SingleAsync(s => s.Id == f.High.Id)).Duration);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Database_blocks_node_or_map_delete_without_partial_cascade(bool map, bool active)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var f = await KnowledgeContentFixture.CreateAsync(db);
        await db.KnowledgeEdges.ExecuteDeleteAsync();
        db.StudySessions.Add(new(Guid.NewGuid(), f.Graph.First.Id, StudySessionFixture.Instant, active ? null : StudySessionFixture.Instant));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var error = await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            if (map) await db.KnowledgeMaps.Where(m => m.Id == f.Graph.Map.Id).ExecuteDeleteAsync();
            else await db.KnowledgeNodes.Where(n => n.Id == f.Graph.First.Id).ExecuteDeleteAsync();
        });
        Assert.Equal(19, error.SqliteErrorCode);
        Assert.True(await db.KnowledgeMaps.AnyAsync(m => m.Id == f.Graph.Map.Id));
        Assert.Equal(2, await db.KnowledgeNodes.CountAsync(n => n.KnowledgeMapId == f.Graph.Map.Id));
        Assert.Equal(3, await db.KnowledgeContents.CountAsync(c => c.KnowledgeNodeId == f.Graph.First.Id));
        Assert.Single(await db.StudySessions.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Store_translates_fk_failure_after_bypassing_precheck_into_existing_conflict(bool map)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); KnowledgeContentFixture f;
        using (var seed = factory.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<TalynorDbContext>(); f = await KnowledgeContentFixture.CreateAsync(db);
            await db.KnowledgeEdges.ExecuteDeleteAsync();
            db.StudySessions.Add(new(Guid.NewGuid(), f.Graph.First.Id, StudySessionFixture.Instant, null));
            await db.SaveChangesAsync();
        }
        using var scope = factory.Services.CreateScope(); var store = scope.ServiceProvider.GetRequiredService<IKnowledgeMapStore>();
        var resourceId = map ? f.Graph.Map.Id : f.Graph.First.Id;
        if (map) store.RemoveMap((await store.GetMapAsync(f.Graph.Map.Id, f.Graph.Workspace.Id, CancellationToken.None))!);
        else store.RemoveNode((await store.GetNodeAsync(f.Graph.First.Id, f.Graph.Map.Id, f.Graph.Workspace.Id, CancellationToken.None))!);
        var status = await store.SaveAsync(new(map ? KnowledgeWriteOperation.DeleteMap : KnowledgeWriteOperation.DeleteNode,
            f.Graph.Workspace.Id, f.Graph.Map.Id, resourceId), CancellationToken.None);
        Assert.Equal(map ? KnowledgeWriteStatus.MapInUse : KnowledgeWriteStatus.NodeInUse, status);
    }

    [Fact]
    public async Task Migration_up_down_reapply_preserves_graph_content_and_triggers_without_model_drift()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var f = await StudySessionFixture.CreateAsync(db); db.ChangeTracker.Clear();
        Assert.Equal(7, (await db.Database.GetAppliedMigrationsAsync()).Count()); Assert.False(db.Database.HasPendingModelChanges());
        await db.GetService<IMigrator>().MigrateAsync("20261016054447_AddKnowledgeContent");
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("SELECT Id FROM StudySessions"));
        Assert.Equal(5, await db.KnowledgeContents.CountAsync()); Assert.Equal(4, await db.KnowledgeNodes.CountAsync());
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE KnowledgeNodes SET ChildMapId = {f.Graph.OtherMap.Id} WHERE Id = {f.Graph.First.Id}"));
        await db.Database.MigrateAsync();
        Assert.Empty(await db.StudySessions.ToArrayAsync()); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        db.StudySessions.Add(new(Guid.NewGuid(), f.Graph.First.Id, StudySessionFixture.Instant, null));
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<SqliteException>(() => db.KnowledgeMaps.Where(m => m.Id == f.Graph.Map.Id).ExecuteDeleteAsync());
        Assert.Equal(5, await db.KnowledgeContents.CountAsync());
    }

    private sealed class Commands : DbCommandInterceptor
    {
        public List<(string Text, Dictionary<string, object?> Parameters)> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Sql.Add((command.CommandText, command.Parameters.Cast<DbParameter>().ToDictionary(p => p.ParameterName, p => p.Value)));
            return ValueTask.FromResult(result);
        }
    }
}

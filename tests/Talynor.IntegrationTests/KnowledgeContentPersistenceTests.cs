using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Application.KnowledgeContents;
using Talynor.Application.KnowledgeMaps;
using Talynor.Domain.KnowledgeContents;
using Talynor.Domain.KnowledgeMaps;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class KnowledgeContentPersistenceTests
{
    [Fact]
    public async Task Roundtrip_is_utc_and_sql_order_uses_creation_then_controlled_id_without_loading_bodies_or_tracking()
    {
        var commands = new Commands();
        using var factory = new RegistrationTestFactory(configureServices: services =>
            services.AddDbContext<TalynorDbContext>(options => options.AddInterceptors(commands)));
        await factory.InitializeAsync(); KnowledgeContentFixture fixture;
        using (var seed = factory.Services.CreateScope())
            fixture = await KnowledgeContentFixture.CreateAsync(seed.ServiceProvider.GetRequiredService<TalynorDbContext>());
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var note = await db.KnowledgeContents.SingleAsync(content => content.Id == fixture.Note.Id);
        Assert.Equal(fixture.Note.Body, note.Body); Assert.Equal("Note", note.Title); Assert.Null(note.Url);
        Assert.Equal(KnowledgeContentFixture.Instant, note.CreatedAtUtc); Assert.Equal(TimeSpan.Zero, note.CreatedAtUtc.Offset);
        Assert.Equal(note.CreatedAtUtc, note.UpdatedAtUtc);
        var link = await db.KnowledgeContents.SingleAsync(content => content.Id == fixture.Link.Id);
        Assert.Equal(KnowledgeContentType.Link, link.Type); Assert.Equal(fixture.Link.Url, link.Url);
        Assert.Equal(note.CreatedAtUtc, link.CreatedAtUtc); Assert.Equal(TimeSpan.Zero, link.UpdatedAtUtc.Offset);
        var timestamp = db.Model.FindEntityType(typeof(KnowledgeContent))!.FindProperty(nameof(KnowledgeContent.CreatedAtUtc))!;
        Assert.Equal(typeof(DateTime), timestamp.GetValueConverter()!.ProviderClrType);
        Assert.Null(db.Model.FindEntityType(typeof(KnowledgeNode))!.FindProperty(nameof(KnowledgeNode.CreatedAtUtc))!.GetValueConverter());
        db.ChangeTracker.Clear(); commands.Sql.Clear();
        var reader = scope.ServiceProvider.GetRequiredService<IKnowledgeContentReader>();
        var list = await reader.ListForNodeAsync(fixture.Graph.Workspace.Id, fixture.Graph.Map.Id, fixture.Graph.First.Id, CancellationToken.None);
        Assert.Equal(new[] { fixture.Early.Id, fixture.Link.Id, fixture.Note.Id }, list!.Select(item => item.Id));
        Assert.Equal(2, commands.Sql.Count);
        Assert.All(commands.Sql, sql => Assert.DoesNotContain("\"Body\"", sql));
        var sql = commands.Sql[1].Replace("\"", "").Replace("[", "").Replace("]", "").Replace("`", "");
        const RegexOptions sqlOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        var contentSource = Regex.Match(sql, @"\bFROM\s+KnowledgeContents\s+(?:AS\s+)?(?<alias>\w+)\b", sqlOptions);
        Assert.True(contentSource.Success, "The real query must identify its KnowledgeContents source alias.");
        var contentAlias = Regex.Escape(contentSource.Groups["alias"].Value);
        Assert.Matches(new Regex(
            $@"\bORDER\s+BY\s+{contentAlias}\s*\.\s*CreatedAtUtc(?:\s+ASC)?\s*,\s*{contentAlias}\s*\.\s*Id(?:\s+ASC)?\s*;?\s*$",
            sqlOptions), sql);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(fixture.Note.Body, (await reader.GetForNodeAsync(fixture.Graph.Workspace.Id, fixture.Graph.Map.Id, fixture.Graph.First.Id, fixture.Note.Id, CancellationToken.None))!.Body);
        Assert.Null(await reader.ListForNodeAsync(fixture.Graph.OtherWorkspace.Id, fixture.Graph.Map.Id, fixture.Graph.First.Id, CancellationToken.None));
        Assert.Null(await reader.GetForNodeAsync(fixture.Graph.Workspace.Id, fixture.Graph.Map.Id, fixture.Graph.Second.Id, fixture.Note.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData("unknown", "body", null)]
    [InlineData("note", null, null)]
    [InlineData("note", "body", "https://example.invalid")]
    [InlineData("link", null, null)]
    [InlineData("link", "body", "https://example.invalid")]
    public async Task Database_rejects_invalid_discriminator_or_payload_even_for_direct_sql(string type, string? body, string? url)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var fixture = await KnowledgeContentFixture.CreateAsync(db);
        var error = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE KnowledgeContents SET Type = {type}, Body = {body}, Url = {url} WHERE Id = {fixture.Note.Id}"));
        Assert.Equal(275, error.SqliteExtendedErrorCode);
        db.ChangeTracker.Clear();
        Assert.Equal(fixture.Note.Body, (await db.KnowledgeContents.SingleAsync(content => content.Id == fixture.Note.Id)).Body);
    }

    [Fact]
    public async Task Required_node_fk_rejects_orphans()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        db.KnowledgeContents.Add(KnowledgeContent.Create(Guid.NewGuid(), KnowledgeContentType.Note, null, "orphan", null, KnowledgeContentFixture.Instant));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(787, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
    }

    [Fact]
    public async Task Node_in_use_preserves_contents_until_edge_removed_then_node_cascade_deletes_only_its_contents()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); KnowledgeContentFixture f;
        using (var seed = factory.Services.CreateScope()) f = await KnowledgeContentFixture.CreateAsync(seed.ServiceProvider.GetRequiredService<TalynorDbContext>());
        using (var attempt = factory.Services.CreateScope())
        {
            var store = attempt.ServiceProvider.GetRequiredService<IKnowledgeMapStore>();
            var node = await store.GetNodeAsync(f.Graph.First.Id, f.Graph.Map.Id, f.Graph.Workspace.Id, CancellationToken.None);
            store.RemoveNode(node!);
            Assert.Equal(KnowledgeWriteStatus.NodeInUse, await store.SaveAsync(new(KnowledgeWriteOperation.DeleteNode, f.Graph.Workspace.Id, f.Graph.Map.Id, node!.Id), CancellationToken.None));
        }
        using (var remove = factory.Services.CreateScope())
        {
            var db = remove.ServiceProvider.GetRequiredService<TalynorDbContext>();
            Assert.Equal(3, await db.KnowledgeContents.CountAsync(content => content.KnowledgeNodeId == f.Graph.First.Id));
            await db.KnowledgeEdges.Where(edge => edge.Id == f.Graph.Edge.Id).ExecuteDeleteAsync();
            await db.KnowledgeNodes.Where(node => node.Id == f.Graph.First.Id).ExecuteDeleteAsync();
            Assert.False(await db.KnowledgeContents.AnyAsync(content => content.KnowledgeNodeId == f.Graph.First.Id));
            Assert.True(await db.KnowledgeContents.AnyAsync(content => content.Id == f.ChildContent.Id));
            Assert.True(await db.KnowledgeContents.AnyAsync(content => content.Id == f.ForeignContent.Id));
        }
    }

    [Fact]
    public async Task Map_in_use_preserves_contents_and_permitted_map_delete_cascades_transitively()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); KnowledgeContentFixture f;
        using (var seed = factory.Services.CreateScope()) f = await KnowledgeContentFixture.CreateAsync(seed.ServiceProvider.GetRequiredService<TalynorDbContext>());
        using (var attempt = factory.Services.CreateScope())
        {
            var store = attempt.ServiceProvider.GetRequiredService<IKnowledgeMapStore>();
            var child = await store.GetMapAsync(f.Graph.Child.Id, f.Graph.Workspace.Id, CancellationToken.None);
            store.RemoveMap(child!);
            Assert.Equal(KnowledgeWriteStatus.MapInUse, await store.SaveAsync(new(KnowledgeWriteOperation.DeleteMap, f.Graph.Workspace.Id, child!.Id), CancellationToken.None));
        }
        using var remove = factory.Services.CreateScope(); var db = remove.ServiceProvider.GetRequiredService<TalynorDbContext>();
        Assert.True(await db.KnowledgeContents.AnyAsync(content => content.Id == f.ChildContent.Id));
        await db.KnowledgeMaps.Where(map => map.Id == f.Graph.Map.Id).ExecuteDeleteAsync();
        Assert.False(await db.KnowledgeNodes.AnyAsync(node => node.KnowledgeMapId == f.Graph.Map.Id));
        Assert.False(await db.KnowledgeContents.AnyAsync(content => content.Id == f.Note.Id || content.Id == f.Link.Id || content.Id == f.Early.Id));
        Assert.True(await db.KnowledgeContents.AnyAsync(content => content.Id == f.ChildContent.Id));
        Assert.True(await db.KnowledgeContents.AnyAsync(content => content.Id == f.ForeignContent.Id));
    }

    [Fact]
    public async Task Migration_down_and_up_preserve_existing_graph_and_triggers_without_model_drift()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var f = await KnowledgeContentFixture.CreateAsync(db); db.ChangeTracker.Clear();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(5, (await db.Database.GetAppliedMigrationsAsync()).Count());
        await db.GetService<IMigrator>().MigrateAsync("20261014004536_AddPersistentKnowledgeGraph");
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("SELECT Id FROM KnowledgeContents"));
        Assert.Equal(4, await db.KnowledgeNodes.CountAsync());
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE KnowledgeNodes SET ChildMapId = {f.Graph.OtherMap.Id} WHERE Id = {f.Graph.First.Id}"));
        await db.Database.MigrateAsync();
        Assert.Empty(await db.KnowledgeContents.ToArrayAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private sealed class Commands : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Sql.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}

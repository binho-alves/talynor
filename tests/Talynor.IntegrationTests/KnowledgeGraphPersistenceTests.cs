using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Application.KnowledgeMaps;
using Talynor.Domain.KnowledgeMaps;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class KnowledgeGraphPersistenceTests
{
    [Fact]
    public async Task Graph_roundtrips_across_contexts_with_positions_timestamps_and_same_workspace_child()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        KnowledgeGraphFixture fixture;
        using (var scope = factory.Services.CreateScope())
            fixture = await KnowledgeGraphFixture.CreateAsync(scope.ServiceProvider.GetRequiredService<TalynorDbContext>());
        using var readScope = factory.Services.CreateScope();
        var db = readScope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var map = await db.KnowledgeMaps.SingleAsync(map => map.Id == fixture.Map.Id);
        Assert.Equal(fixture.Workspace.Id, map.WorkspaceId);
        Assert.Equal(fixture.Map.CreatedAtUtc, map.CreatedAtUtc);
        Assert.Equal(map.CreatedAtUtc, map.UpdatedAtUtc);
        var node = await db.KnowledgeNodes.SingleAsync(node => node.Id == fixture.First.Id);
        Assert.Equal(fixture.First.CreatedAtUtc, node.CreatedAtUtc);
        Assert.Equal(node.CreatedAtUtc, node.UpdatedAtUtc);
        Assert.Equal(125.5, node.PositionX);
        Assert.Equal(-32.25, node.PositionY);
        Assert.Equal(fixture.Child.Id, node.ChildMapId);
        var edge = await db.KnowledgeEdges.SingleAsync();
        Assert.Equal(fixture.Edge.Id, edge.Id);
        Assert.Equal(fixture.First.Id, edge.SourceNodeId);
        Assert.Equal(fixture.Second.Id, edge.TargetNodeId);
        Assert.Equal(fixture.Edge.CreatedAtUtc, edge.CreatedAtUtc);
        var reader = readScope.ServiceProvider.GetRequiredService<IPersistedKnowledgeMapReader>();
        var projection = await reader.GetAsync(map.Id, map.WorkspaceId, CancellationToken.None);
        Assert.Equal(2, projection!.Nodes.Count);
        Assert.Single(projection.Edges);
        Assert.Null(await reader.GetAsync(map.Id, fixture.OtherWorkspace.Id, CancellationToken.None));
        Assert.Null(await reader.GetAsync(Guid.NewGuid(), map.WorkspaceId, CancellationToken.None));
        Assert.NotNull(await reader.GetAsync(fixture.Child.Id, map.WorkspaceId, CancellationToken.None));
    }

    [Fact]
    public async Task Cross_workspace_child_is_rejected_on_insert_and_direct_sql_updates_in_both_directions()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var fixture = await KnowledgeGraphFixture.CreateAsync(db);
        var invalid = KnowledgeNode.Create(fixture.Map.Id, "Invalid child", null, 0, 0, fixture.OtherMap.Id, TimeProvider.System.GetUtcNow());
        db.KnowledgeNodes.Add(invalid);
        var insert = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(1811, Assert.IsType<SqliteException>(insert.InnerException).SqliteExtendedErrorCode);
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE KnowledgeNodes SET ChildMapId = {fixture.OtherMap.Id} WHERE Id = {fixture.First.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE KnowledgeNodes SET KnowledgeMapId = {fixture.OtherMap.Id} WHERE Id = {fixture.First.Id}"));
        foreach (var map in new[] { fixture.Map, fixture.Child })
            await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE KnowledgeMaps SET WorkspaceId = {fixture.OtherWorkspace.Id} WHERE Id = {map.Id}"));
        Assert.Equal(fixture.Child.Id, (await db.KnowledgeNodes.SingleAsync(node => node.Id == fixture.First.Id)).ChildMapId);
        Assert.All(await db.KnowledgeMaps.Where(map => map.Id == fixture.Map.Id || map.Id == fixture.Child.Id).ToListAsync(),
            map => Assert.Equal(fixture.Workspace.Id, map.WorkspaceId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Composite_fk_rejects_cross_map_source_or_target(bool invalidSource)
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var fixture = await KnowledgeGraphFixture.CreateAsync(db);
        db.KnowledgeEdges.Add(KnowledgeEdge.Create(fixture.Map.Id,
            invalidSource ? fixture.ChildNode.Id : fixture.First.Id,
            invalidSource ? fixture.Second.Id : fixture.ChildNode.Id, TimeProvider.System.GetUtcNow()));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(787, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
        db.ChangeTracker.Clear();
        Assert.Single(await db.KnowledgeEdges.ToListAsync());
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE KnowledgeEdges SET TargetNodeId = {fixture.ChildNode.Id} WHERE Id = {fixture.Edge.Id}"));
    }

    [Theory]
    [InlineData("workspace")]
    [InlineData("map")]
    [InlineData("child")]
    [InlineData("source")]
    [InlineData("target")]
    public async Task Missing_foreign_references_are_rejected(string reference)
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var fixture = await KnowledgeGraphFixture.CreateAsync(db);
        var missing = Guid.NewGuid();
        switch (reference)
        {
            case "workspace": db.KnowledgeMaps.Add(KnowledgeMap.Create(missing, "Missing", null, TimeProvider.System.GetUtcNow())); break;
            case "map": db.KnowledgeNodes.Add(KnowledgeNode.Create(missing, "Missing", null, 0, 0, null, TimeProvider.System.GetUtcNow())); break;
            case "child": db.KnowledgeNodes.Add(KnowledgeNode.Create(fixture.Map.Id, "Missing", null, 0, 0, missing, TimeProvider.System.GetUtcNow())); break;
            case "source": db.KnowledgeEdges.Add(KnowledgeEdge.Create(fixture.Map.Id, missing, fixture.Second.Id, TimeProvider.System.GetUtcNow())); break;
            case "target": db.KnowledgeEdges.Add(KnowledgeEdge.Create(fixture.Map.Id, fixture.First.Id, missing, TimeProvider.System.GetUtcNow())); break;
        }
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(787, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
    }

    [Fact]
    public async Task Delete_restricts_workspace_child_and_referenced_node_but_map_cascades_only_its_graph()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var fixture = await KnowledgeGraphFixture.CreateAsync(db);
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM PersonalWorkspaces WHERE Id = {fixture.Workspace.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM KnowledgeMaps WHERE Id = {fixture.Child.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM KnowledgeNodes WHERE Id = {fixture.First.Id}"));
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM KnowledgeMaps WHERE Id = {fixture.Map.Id}");
        db.ChangeTracker.Clear();
        Assert.Empty(await db.KnowledgeNodes.Where(node => node.KnowledgeMapId == fixture.Map.Id).ToListAsync());
        Assert.Empty(await db.KnowledgeEdges.ToListAsync());
        Assert.NotNull(await db.KnowledgeMaps.FindAsync(fixture.Child.Id));
        Assert.NotNull(await db.KnowledgeNodes.FindAsync(fixture.ChildNode.Id));
        Assert.Equal(2, await db.PersonalWorkspaces.CountAsync());
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task Database_rejects_non_finite_positions_even_outside_domain_factory(double value)
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var fixture = await KnowledgeGraphFixture.CreateAsync(db);
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE KnowledgeNodes SET PositionX = {value} WHERE Id = {fixture.First.Id}"));
    }

    [Fact]
    public async Task Graph_migration_down_and_up_preserve_existing_workspace_and_identity_schema()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var fixture = await KnowledgeGraphFixture.CreateAsync(db);
        var previous = db.Database.GetMigrations().Single(name => name.EndsWith("_AddPersonalWorkspace"));
        await db.GetService<IMigrator>().MigrateAsync(previous);
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.PersonalWorkspaces.CountAsync());
        Assert.Equal(0, await db.Users.CountAsync());
        Assert.Single(await db.Database.GetPendingMigrationsAsync());
        await db.Database.MigrateAsync();
        Assert.Empty(await db.KnowledgeMaps.ToListAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        // The recreated triggers still reject invalid links, not only the first Up.
        var map = KnowledgeMap.Create(fixture.Workspace.Id, "A", null, TimeProvider.System.GetUtcNow());
        var other = KnowledgeMap.Create(fixture.OtherWorkspace.Id, "B", null, TimeProvider.System.GetUtcNow());
        db.KnowledgeMaps.AddRange(map, other);
        await db.SaveChangesAsync();
        db.KnowledgeNodes.Add(KnowledgeNode.Create(map.Id, "Bad", null, 0, 0, other.Id, TimeProvider.System.GetUtcNow()));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}

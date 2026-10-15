using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Application.KnowledgeMaps;
using Talynor.Domain.KnowledgeMaps;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class KnowledgeAuthoringPersistenceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Real_fk_delete_conflicts_are_classified_even_without_application_precheck(bool deleteMap)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        KnowledgeGraphFixture fixture;
        using (var seed = factory.Services.CreateScope()) fixture = await KnowledgeGraphFixture.CreateAsync(seed.ServiceProvider.GetRequiredService<TalynorDbContext>());
        using var scope = factory.Services.CreateScope(); var store = scope.ServiceProvider.GetRequiredService<IKnowledgeMapStore>();
        if (deleteMap)
        {
            var map = await store.GetMapAsync(fixture.Child.Id, fixture.Workspace.Id, CancellationToken.None); store.RemoveMap(map!);
            Assert.Equal(KnowledgeWriteStatus.MapInUse, await store.SaveAsync(new(KnowledgeWriteOperation.DeleteMap, fixture.Workspace.Id, map!.Id), CancellationToken.None));
        }
        else
        {
            var node = await store.GetNodeAsync(fixture.First.Id, fixture.Map.Id, fixture.Workspace.Id, CancellationToken.None); store.RemoveNode(node!);
            Assert.Equal(KnowledgeWriteStatus.NodeInUse, await store.SaveAsync(new(KnowledgeWriteOperation.DeleteNode, fixture.Workspace.Id, fixture.Map.Id, node!.Id), CancellationToken.None));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Relation_removed_after_validation_is_classified_by_actual_fk_failure(bool childRelation)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        KnowledgeGraphFixture fixture; Guid target;
        using (var seed = factory.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<TalynorDbContext>(); fixture = await KnowledgeGraphFixture.CreateAsync(db);
            if (childRelation)
            { var child = KnowledgeMap.Create(fixture.Workspace.Id, "Unused child", null, TimeProvider.System.GetUtcNow()); db.KnowledgeMaps.Add(child); target = child.Id; }
            else
            { var node = KnowledgeNode.Create(fixture.Map.Id, "Unused node", null, 0, 0, null, TimeProvider.System.GetUtcNow()); db.KnowledgeNodes.Add(node); target = node.Id; }
            await db.SaveChangesAsync();
        }
        using var write = factory.Services.CreateScope(); var store = write.ServiceProvider.GetRequiredService<IKnowledgeMapStore>();
        Guid resource;
        if (childRelation)
        {
            Assert.NotNull(await store.GetMapAsync(target, fixture.Workspace.Id, CancellationToken.None));
            var node = KnowledgeNode.Create(fixture.Map.Id, "New", null, 0, 0, target, TimeProvider.System.GetUtcNow()); store.AddNode(node); resource = node.Id;
        }
        else
        {
            Assert.NotNull(await store.GetNodeAsync(target, fixture.Map.Id, fixture.Workspace.Id, CancellationToken.None));
            var edge = KnowledgeEdge.Create(fixture.Map.Id, fixture.First.Id, target, TimeProvider.System.GetUtcNow()); store.AddEdge(edge); resource = edge.Id;
        }
        using (var remove = factory.Services.CreateScope())
        {
            var db = remove.ServiceProvider.GetRequiredService<TalynorDbContext>();
            if (childRelation) await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM KnowledgeMaps WHERE Id = {target}");
            else await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM KnowledgeNodes WHERE Id = {target}");
        }
        var status = await store.SaveAsync(new(childRelation ? KnowledgeWriteOperation.CreateNode : KnowledgeWriteOperation.CreateEdge,
            fixture.Workspace.Id, fixture.Map.Id, resource), CancellationToken.None);
        Assert.Equal(childRelation ? KnowledgeWriteStatus.InvalidChildMap : KnowledgeWriteStatus.InvalidEdgeNodes, status);
        using var final = factory.Services.CreateScope(); var finalDb = final.ServiceProvider.GetRequiredService<TalynorDbContext>();
        Assert.False(await finalDb.KnowledgeNodes.AnyAsync(node => node.Id == resource)); Assert.False(await finalDb.KnowledgeEdges.AnyAsync(edge => edge.Id == resource));
    }

    [Fact]
    public async Task Concurrent_loaded_updates_are_last_committed_write_per_modified_column_without_tokens()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); KnowledgeGraphFixture fixture;
        using (var seed = factory.Services.CreateScope()) fixture = await KnowledgeGraphFixture.CreateAsync(seed.ServiceProvider.GetRequiredService<TalynorDbContext>());
        using var first = factory.Services.CreateScope(); using var second = factory.Services.CreateScope();
        var a = first.ServiceProvider.GetRequiredService<TalynorDbContext>(); var b = second.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var nodeA = await a.KnowledgeNodes.SingleAsync(node => node.Id == fixture.Second.Id);
        var nodeB = await b.KnowledgeNodes.SingleAsync(node => node.Id == fixture.Second.Id);
        nodeA.UpdateDetails("First", null, TimeProvider.System.GetUtcNow());
        nodeB.UpdateDetails("Last", null, TimeProvider.System.GetUtcNow());
        await a.SaveChangesAsync(); await b.SaveChangesAsync();
        using var verify = factory.Services.CreateScope(); var db = verify.ServiceProvider.GetRequiredService<TalynorDbContext>();
        Assert.Equal("Last", (await db.KnowledgeNodes.SingleAsync(node => node.Id == fixture.Second.Id)).Title);
        Assert.DoesNotContain(db.Model.FindEntityType(typeof(KnowledgeNode))!.GetProperties(), property => property.IsConcurrencyToken);
        Assert.DoesNotContain(db.Model.FindEntityType(typeof(KnowledgeMap))!.GetProperties(), property => property.IsConcurrencyToken);
    }
}

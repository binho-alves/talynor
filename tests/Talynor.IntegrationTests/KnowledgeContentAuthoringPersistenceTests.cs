using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Application.KnowledgeContents;
using Talynor.Domain.KnowledgeContents;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class KnowledgeContentAuthoringPersistenceTests
{
    [Fact]
    public async Task Store_tracks_scoped_content_and_persists_create_update_delete_with_one_save_each()
    {
        var saves = new Saves(); using var factory = new RegistrationTestFactory(saves); await factory.InitializeAsync();
        KnowledgeContentFixture f;
        using (var scope = factory.Services.CreateScope()) f = await KnowledgeContentFixture.CreateAsync(scope.ServiceProvider.GetRequiredService<TalynorDbContext>());
        saves.Count = 0;
        var candidate = KnowledgeContent.Create(f.Graph.Second.Id, KnowledgeContentType.Note, null, "created", null, KnowledgeContentFixture.Instant);
        using (var scope = factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IKnowledgeContentStore>();
            Assert.True(await store.NodeExistsAsync(f.Graph.Workspace.Id, f.Graph.Map.Id, f.Graph.Second.Id, CancellationToken.None));
            store.Add(candidate);
            Assert.Equal(KnowledgeContentWriteStatus.Success, await store.SaveAsync(Attempt(f, candidate, KnowledgeContentWriteOperation.Create), CancellationToken.None));
            Assert.Equal(1, saves.Count);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var store = scope.ServiceProvider.GetRequiredService<IKnowledgeContentStore>();
            var content = await store.GetForNodeAsync(f.Graph.Workspace.Id, f.Graph.Map.Id, f.Graph.Second.Id, candidate.Id, CancellationToken.None);
            Assert.NotNull(content); Assert.Equal(EntityState.Unchanged, db.Entry(content).State);
            Assert.True(content.UpdateDetails("Updated", "new body", null, KnowledgeContentFixture.Instant.AddHours(1)));
            Assert.Equal(KnowledgeContentWriteStatus.Success, await store.SaveAsync(Attempt(f, candidate, KnowledgeContentWriteOperation.Update), CancellationToken.None));
            Assert.Equal(2, saves.Count);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var content = await db.KnowledgeContents.SingleAsync(c => c.Id == candidate.Id);
            Assert.Equal("new body", content.Body); Assert.Equal("Updated", content.Title);
            Assert.Equal(candidate.CreatedAtUtc, content.CreatedAtUtc); Assert.Equal(KnowledgeContentFixture.Instant.AddHours(1), content.UpdatedAtUtc);
            var store = scope.ServiceProvider.GetRequiredService<IKnowledgeContentStore>(); store.Remove(content);
            Assert.Equal(KnowledgeContentWriteStatus.Success, await store.SaveAsync(Attempt(f, candidate, KnowledgeContentWriteOperation.Delete), CancellationToken.None));
            Assert.Equal(3, saves.Count);
        }
        using var verify = factory.Services.CreateScope();
        Assert.False(await verify.ServiceProvider.GetRequiredService<TalynorDbContext>().KnowledgeContents.AnyAsync(c => c.Id == candidate.Id));
    }

    [Fact]
    public async Task Write_lookup_filters_all_parents_and_workspace_and_honors_cancellation()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var f = await KnowledgeContentFixture.CreateAsync(db); db.ChangeTracker.Clear();
        var store = scope.ServiceProvider.GetRequiredService<IKnowledgeContentStore>();
        foreach (var (workspace, map, node) in new[] { (f.Graph.OtherWorkspace.Id, f.Graph.Map.Id, f.Graph.First.Id),
            (f.Graph.Workspace.Id, f.Graph.OtherMap.Id, f.Graph.First.Id), (f.Graph.Workspace.Id, f.Graph.Map.Id, f.ForeignNode.Id),
            (f.Graph.Workspace.Id, f.Graph.Map.Id, f.Graph.Second.Id) })
            Assert.Null(await store.GetForNodeAsync(workspace, map, node, f.Note.Id, CancellationToken.None));
        Assert.False(await store.NodeExistsAsync(f.Graph.OtherWorkspace.Id, f.Graph.Map.Id, f.Graph.First.Id, CancellationToken.None));
        Assert.Empty(db.ChangeTracker.Entries()); var ct = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.NodeExistsAsync(f.Graph.Workspace.Id, f.Graph.Map.Id, f.Graph.First.Id, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetForNodeAsync(f.Graph.Workspace.Id, f.Graph.Map.Id, f.Graph.First.Id, f.Note.Id, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(Attempt(f, f.Note, KnowledgeContentWriteOperation.Update), ct));
    }

    [Fact]
    public async Task Parent_removed_after_create_check_returns_not_found_but_unrelated_fk_failure_propagates()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var f = await KnowledgeContentFixture.CreateAsync(db); db.ChangeTracker.Clear(); var store = scope.ServiceProvider.GetRequiredService<IKnowledgeContentStore>();
        var candidate = KnowledgeContent.Create(f.Graph.ChildNode.Id, KnowledgeContentType.Note, null, "candidate", null, KnowledgeContentFixture.Instant);
        Assert.True(await store.NodeExistsAsync(f.Graph.Workspace.Id, f.Graph.Child.Id, f.Graph.ChildNode.Id, CancellationToken.None));
        store.Add(candidate);
        using (var external = factory.Services.CreateScope())
            await external.ServiceProvider.GetRequiredService<TalynorDbContext>().KnowledgeNodes.Where(n => n.Id == f.Graph.ChildNode.Id).ExecuteDeleteAsync();
        Assert.Equal(KnowledgeContentWriteStatus.NotFound, await store.SaveAsync(new(KnowledgeContentWriteOperation.Create,
            f.Graph.Workspace.Id, f.Graph.Child.Id, candidate.KnowledgeNodeId, candidate.Id), CancellationToken.None));
        db.ChangeTracker.Clear();
        var invalid = KnowledgeContent.Create(Guid.NewGuid(), KnowledgeContentType.Note, null, "orphan", null, KnowledgeContentFixture.Instant);
        store.Add(invalid);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveAsync(new(KnowledgeContentWriteOperation.Create,
            f.Graph.Workspace.Id, f.Graph.Map.Id, f.Graph.First.Id, invalid.Id), CancellationToken.None));
        Assert.Equal(787, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Zero_row_update_or_delete_is_not_found_without_optimistic_tokens(bool delete)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var f = await KnowledgeContentFixture.CreateAsync(db); db.ChangeTracker.Clear();
        var store = scope.ServiceProvider.GetRequiredService<IKnowledgeContentStore>();
        var content = (await store.GetForNodeAsync(f.Graph.Workspace.Id, f.Graph.Map.Id, f.Graph.First.Id, f.Note.Id, CancellationToken.None))!;
        using (var external = factory.Services.CreateScope())
            await external.ServiceProvider.GetRequiredService<TalynorDbContext>().KnowledgeContents.Where(c => c.Id == f.Note.Id).ExecuteDeleteAsync();
        if (delete) store.Remove(content); else content.UpdateDetails("Changed", content.Body, null, KnowledgeContentFixture.Instant.AddHours(1));
        Assert.Equal(KnowledgeContentWriteStatus.NotFound, await store.SaveAsync(Attempt(f, content,
            delete ? KnowledgeContentWriteOperation.Delete : KnowledgeContentWriteOperation.Update), CancellationToken.None));
    }

    [Fact]
    public async Task Primary_key_failure_is_not_mislabeled_as_not_found()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var f = await KnowledgeContentFixture.CreateAsync(db); db.ChangeTracker.Clear();
        var candidate = KnowledgeContent.Create(f.Graph.First.Id, KnowledgeContentType.Note, null, "duplicate", null, KnowledgeContentFixture.Instant);
        db.Entry(candidate).Property(c => c.Id).CurrentValue = f.Note.Id;
        var store = scope.ServiceProvider.GetRequiredService<IKnowledgeContentStore>(); store.Add(candidate);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveAsync(Attempt(f, candidate, KnowledgeContentWriteOperation.Create), CancellationToken.None));
        Assert.Equal(1555, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
    }

    [Fact]
    public async Task Concurrent_modified_column_writes_follow_existing_baseline_without_a_global_row_ordering_claim()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); KnowledgeContentFixture f;
        using (var seed = factory.Services.CreateScope()) f = await KnowledgeContentFixture.CreateAsync(seed.ServiceProvider.GetRequiredService<TalynorDbContext>());
        using var first = factory.Services.CreateScope(); using var second = factory.Services.CreateScope();
        var a = first.ServiceProvider.GetRequiredService<IKnowledgeContentStore>(); var b = second.ServiceProvider.GetRequiredService<IKnowledgeContentStore>();
        var ca = (await a.GetForNodeAsync(f.Graph.Workspace.Id, f.Graph.Map.Id, f.Graph.First.Id, f.Note.Id, CancellationToken.None))!;
        var cb = (await b.GetForNodeAsync(f.Graph.Workspace.Id, f.Graph.Map.Id, f.Graph.First.Id, f.Note.Id, CancellationToken.None))!;
        ca.UpdateDetails("First title", ca.Body, null, KnowledgeContentFixture.Instant.AddHours(1));
        cb.UpdateDetails(cb.Title, "Second body", null, KnowledgeContentFixture.Instant.AddHours(2));
        await a.SaveAsync(Attempt(f, ca, KnowledgeContentWriteOperation.Update), CancellationToken.None);
        await b.SaveAsync(Attempt(f, cb, KnowledgeContentWriteOperation.Update), CancellationToken.None);
        using var read = factory.Services.CreateScope(); var saved = await read.ServiceProvider.GetRequiredService<TalynorDbContext>().KnowledgeContents.SingleAsync(c => c.Id == f.Note.Id);
        Assert.Equal("First title", saved.Title); Assert.Equal("Second body", saved.Body); Assert.Equal(cb.UpdatedAtUtc, saved.UpdatedAtUtc);
    }

    private static KnowledgeContentWriteAttempt Attempt(KnowledgeContentFixture f, KnowledgeContent content, KnowledgeContentWriteOperation operation) =>
        new(operation, f.Graph.Workspace.Id, f.Graph.Map.Id, content.KnowledgeNodeId, content.Id);
    private sealed class Saves : SaveChangesInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Count++; return ValueTask.FromResult(result); }
    }
}

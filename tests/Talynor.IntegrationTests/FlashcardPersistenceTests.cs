using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Application.Flashcards;
using Talynor.Application.KnowledgeMaps;
using Talynor.Domain.Flashcards;
using Talynor.Domain.StudySessions;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class FlashcardPersistenceTests
{
    [Fact]
    public async Task Utc_roundtrip_model_and_sql_pagination_preserve_ascending_creation_and_adversarial_id_tie_break()
    {
        var commands = new Commands();
        using var factory = new RegistrationTestFactory(configureServices: services => services.AddDbContext<TalynorDbContext>(o => o.AddInterceptors(commands)));
        await factory.InitializeAsync(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var graph = await KnowledgeGraphFixture.CreateAsync(db);
        var instant = DateTimeOffset.UnixEpoch.AddTicks(1234567).ToOffset(TimeSpan.FromHours(-3));
        var high = Card(graph.Second.Id, instant, "90000000-0000-0000-0000-000000000000");
        var low = Card(graph.Second.Id, instant, "10000000-0000-0000-0000-000000000000");
        var early = Card(graph.Second.Id, instant.AddSeconds(-1));
        db.Flashcards.AddRange(high, low, early); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var stored = await db.Flashcards.SingleAsync(c => c.Id == low.Id);
        Assert.Equal(instant.ToUniversalTime(), stored.CreatedAtUtc); Assert.Equal(stored.CreatedAtUtc, stored.UpdatedAtUtc);
        Assert.Equal(TimeSpan.Zero, stored.CreatedAtUtc.Offset); Assert.Equal(TimeSpan.Zero, stored.UpdatedAtUtc.Offset);
        Assert.Equal(1, stored.ContentRevision);
        var model = db.Model.FindEntityType(typeof(Flashcard))!;
        Assert.Equal(new[] { "Back", "ContentRevision", "CreatedAtUtc", "Front", "Id", "KnowledgeNodeId", "UpdatedAtUtc" }, model.GetProperties().Select(p => p.Name).Order());
        Assert.Equal(2000, model.FindProperty("Front")!.GetMaxLength()); Assert.Equal(4000, model.FindProperty("Back")!.GetMaxLength());
        Assert.Equal(typeof(DateTime), model.FindProperty("CreatedAtUtc")!.GetValueConverter()!.ProviderClrType);
        Assert.Equal(typeof(DateTime), model.FindProperty("UpdatedAtUtc")!.GetValueConverter()!.ProviderClrType);
        Assert.Equal(DeleteBehavior.Cascade, Assert.Single(model.GetForeignKeys()).DeleteBehavior);
        Assert.Equal(new[] { "KnowledgeNodeId", "CreatedAtUtc", "Id" }, Assert.Single(model.GetIndexes()).Properties.Select(p => p.Name));
        db.ChangeTracker.Clear(); commands.Sql.Clear();
        var reader = scope.ServiceProvider.GetRequiredService<IFlashcardReader>();
        var first = await reader.ListAsync(graph.Workspace.Id, graph.Map.Id, graph.Second.Id, 0, 2, default);
        Assert.Equal(new[] { early.Id, low.Id }, first!.Flashcards.Select(c => c.Id)); Assert.True(first.HasMore);
        Assert.Equal(2, commands.Sql.Count);
        var query = commands.Sql[1]; var sql = query.Text.Replace("\"", "");
        Assert.DoesNotContain("COUNT(", sql.ToUpperInvariant()); Assert.Contains("WorkspaceId", sql); Assert.Contains("KnowledgeMapId", sql);
        Assert.Matches(@"ORDER BY \w+\.CreatedAtUtc, \w+\.Id\s+LIMIT", sql);
        Assert.Contains(query.Parameters.Values, value => Convert.ToString(value) == "3");
        var next = await reader.ListAsync(graph.Workspace.Id, graph.Map.Id, graph.Second.Id, 2, 2, default);
        Assert.Equal(high.Id, Assert.Single(next!.Flashcards).Id); Assert.False(next.HasMore);
        var exact = await reader.ListAsync(graph.Workspace.Id, graph.Map.Id, graph.Second.Id, 0, 3, default);
        Assert.Equal(3, exact!.Flashcards.Count); Assert.False(exact.HasMore);
        Assert.Empty((await reader.ListAsync(graph.Workspace.Id, graph.Map.Id, graph.Second.Id, 3, 2, default))!.Flashcards);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(await reader.ListAsync(graph.OtherWorkspace.Id, graph.Map.Id, graph.Second.Id, 0, 20, default));
        Assert.Null(await reader.GetAsync(graph.Workspace.Id, graph.Child.Id, graph.Second.Id, low.Id, default));
        Assert.Null(await reader.GetAsync(graph.Workspace.Id, graph.Map.Id, graph.First.Id, low.Id, default));
    }

    [Fact]
    public async Task Required_fk_positive_revision_and_required_text_are_database_constraints()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var graph = await KnowledgeGraphFixture.CreateAsync(db);
        db.Flashcards.Add(Card(Guid.NewGuid(), DateTimeOffset.UnixEpoch));
        var orphan = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(787, Assert.IsType<SqliteException>(orphan.InnerException).SqliteExtendedErrorCode); db.ChangeTracker.Clear();
        var card = Card(graph.Second.Id, DateTimeOffset.UnixEpoch); db.Flashcards.Add(card); await db.SaveChangesAsync();
        Assert.Equal(275, (await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Flashcards SET ContentRevision = 0 WHERE Id = {card.Id}"))).SqliteExtendedErrorCode);
        Assert.Equal(1299, (await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Flashcards SET Front = NULL WHERE Id = {card.Id}"))).SqliteExtendedErrorCode);
    }

    [Fact]
    public async Task Writer_noops_invalid_candidates_and_overflow_do_not_save_or_change_persisted_values()
    {
        var saves = new Saves();
        using var factory = new RegistrationTestFactory(saves); await factory.InitializeAsync();
        Guid cardId; KnowledgeGraphFixture graph;
        using (var seed = factory.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<TalynorDbContext>(); graph = await KnowledgeGraphFixture.CreateAsync(db);
            var card = Card(graph.Second.Id, DateTimeOffset.UnixEpoch); cardId = card.Id; db.Flashcards.Add(card); await db.SaveChangesAsync();
        }
        using var scope = factory.Services.CreateScope(); var writer = scope.ServiceProvider.GetRequiredService<IFlashcardStore>();
        var before = saves.Count;
        foreach (var input in new UpdateFlashcardInput[] { new(), new("front", FrontSpecified: true), new(BackSpecified: true),
            new("candidate", " ", true, true), new(Back: new string('b', 4001), BackSpecified: true) })
            Assert.Equal(FlashcardStatus.InvalidInput, await writer.UpdateAsync(graph.Workspace.Id, graph.Map.Id, graph.Second.Id, cardId, input, DateTimeOffset.UnixEpoch.AddDays(1), default));
        Assert.Equal(before, saves.Count);
        Assert.Equal(FlashcardStatus.Success, await writer.UpdateAsync(graph.Workspace.Id, graph.Map.Id, graph.Second.Id, cardId,
            new(" changed ", "new\n", true, true), DateTimeOffset.UnixEpoch.AddDays(1), default));
        Assert.Equal(before + 1, saves.Count);
        var dbContext = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var changed = await dbContext.Flashcards.AsNoTracking().SingleAsync(c => c.Id == cardId);
        Assert.Equal(2, changed.ContentRevision); Assert.Equal(" changed ", changed.Front); Assert.Equal("new\n", changed.Back);
        Assert.Equal(DateTimeOffset.UnixEpoch, changed.CreatedAtUtc); Assert.Equal(DateTimeOffset.UnixEpoch.AddDays(1), changed.UpdatedAtUtc);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE Flashcards SET ContentRevision = {int.MaxValue} WHERE Id = {cardId}");
        await Assert.ThrowsAsync<OverflowException>(() => writer.UpdateAsync(graph.Workspace.Id, graph.Map.Id, graph.Second.Id, cardId,
            new("candidate", FrontSpecified: true), DateTimeOffset.UnixEpoch.AddDays(2), default));
        var unchanged = await dbContext.Flashcards.AsNoTracking().SingleAsync(c => c.Id == cardId);
        Assert.Equal(int.MaxValue, unchanged.ContentRevision); Assert.Equal(changed.Front, unchanged.Front);
        Assert.Equal(changed.UpdatedAtUtc, unchanged.UpdatedAtUtc); Assert.Equal(before + 1, saves.Count);
        Assert.Empty(dbContext.ChangeTracker.Entries<Flashcard>());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Concurrent_official_edits_read_revision_inside_immediate_and_never_lose_increment(bool disjoint)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        KnowledgeGraphFixture graph; Flashcard card;
        using (var seed = factory.Services.CreateScope())
        { var db = seed.ServiceProvider.GetRequiredService<TalynorDbContext>(); graph = await KnowledgeGraphFixture.CreateAsync(db);
            card = Card(graph.Second.Id, DateTimeOffset.UnixEpoch); db.Flashcards.Add(card); await db.SaveChangesAsync(); }
        var ready = new CountdownEvent(2); var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<FlashcardStatus> Write(UpdateFlashcardInput input) => Task.Run(async () =>
        {
            using var scope = factory.Services.CreateScope(); ready.Signal(); await go.Task;
            return await scope.ServiceProvider.GetRequiredService<IFlashcardStore>().UpdateAsync(graph.Workspace.Id, graph.Map.Id,
                graph.Second.Id, card.Id, input, DateTimeOffset.UnixEpoch.AddDays(1), default);
        });
        var first = Write(new("first", FrontSpecified: true));
        var second = Write(disjoint ? new(Back: "second", BackSpecified: true) : new("second", FrontSpecified: true));
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10))); go.SetResult();
        Assert.All(await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30)), status => Assert.Equal(FlashcardStatus.Success, status));
        using var read = factory.Services.CreateScope(); var final = await read.ServiceProvider.GetRequiredService<TalynorDbContext>().Flashcards.SingleAsync();
        Assert.Equal(3, final.ContentRevision);
        if (disjoint) { Assert.Equal("first", final.Front); Assert.Equal("second", final.Back); }
        else Assert.Contains(final.Front, new[] { "first", "second" });
        ready.Dispose();
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task Graph_deletion_cascades_cards_unless_existing_study_history_blocks_it(bool map, bool blocked)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var graph = await KnowledgeGraphFixture.CreateAsync(db);
        await db.KnowledgeEdges.ExecuteDeleteAsync(); var card = Card(graph.Second.Id, DateTimeOffset.UnixEpoch);
        db.Flashcards.Add(card);
        if (blocked) db.StudySessions.Add(StudySession.Start(graph.Second.Id, DateTimeOffset.UnixEpoch));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var store = scope.ServiceProvider.GetRequiredService<IKnowledgeMapStore>();
        if (map) store.RemoveMap((await store.GetMapAsync(graph.Map.Id, graph.Workspace.Id, default))!);
        else store.RemoveNode((await store.GetNodeAsync(graph.Second.Id, graph.Map.Id, graph.Workspace.Id, default))!);
        var status = await store.SaveAsync(new(map ? KnowledgeWriteOperation.DeleteMap : KnowledgeWriteOperation.DeleteNode,
            graph.Workspace.Id, graph.Map.Id, map ? graph.Map.Id : graph.Second.Id), default);
        Assert.Equal(blocked ? (map ? KnowledgeWriteStatus.MapInUse : KnowledgeWriteStatus.NodeInUse) : KnowledgeWriteStatus.Success, status);
        Assert.Equal(blocked, await db.Flashcards.AnyAsync(c => c.Id == card.Id));
        Assert.True(await db.KnowledgeMaps.AnyAsync(m => m.Id == graph.OtherMap.Id));
    }

    [Fact]
    public async Task AddFlashcards_up_down_reapply_preserves_earlier_data_triggers_and_model()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var f = await KnowledgeGraphFixture.CreateAsync(db);
        var card = Card(f.Second.Id, DateTimeOffset.UnixEpoch); db.Flashcards.Add(card); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.EndsWith("_AddQuizAttempts", (await db.Database.GetAppliedMigrationsAsync()).Last());
        Assert.False(db.Database.HasPendingModelChanges());
        await db.GetService<IMigrator>().MigrateAsync("20261018074303_AddStudySessions");
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("SELECT Id FROM Flashcards"));
        Assert.Equal(3, await db.KnowledgeNodes.CountAsync());
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE KnowledgeNodes SET ChildMapId = {f.OtherMap.Id} WHERE Id = {f.First.Id}"));
        await db.Database.MigrateAsync(); Assert.Empty(await db.Flashcards.ToArrayAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync()); Assert.False(db.Database.HasPendingModelChanges());
        db.Flashcards.Add(Card(f.Second.Id, DateTimeOffset.UnixEpoch)); await db.SaveChangesAsync();
        Assert.Single(await db.Flashcards.ToArrayAsync());
    }

    private static Flashcard Card(Guid nodeId, DateTimeOffset now, string? id = null)
    {
        var card = Flashcard.Create(nodeId, "front", "back", now);
        if (id is not null) typeof(Flashcard).GetProperty(nameof(Flashcard.Id))!.SetValue(card, Guid.Parse(id));
        return card;
    }
    private sealed class Saves : SaveChangesInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken ct = default) { Count++; return ValueTask.FromResult(result); }
    }
    private sealed class Commands : DbCommandInterceptor
    {
        public List<(string Text, Dictionary<string, object?> Parameters)> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Sql.Add((command.CommandText, command.Parameters.Cast<DbParameter>().ToDictionary(p => p.ParameterName, p => p.Value))); return ValueTask.FromResult(result); }
    }
}

using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Application.Flashcards;
using Talynor.Domain.Flashcards;
using Talynor.Infrastructure.Flashcards;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class FlashcardReviewPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task Review_tables_enforce_counts_result_revision_foreign_keys_and_cascade()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var graph = await KnowledgeGraphFixture.CreateAsync(db); var card = Card(graph.Second.Id, "a0000000-0000-0000-0000-000000000001");
        db.Flashcards.Add(card); await db.SaveChangesAsync();
        var eventId = Guid.NewGuid(); var review = Review(eventId, card.Id, 1, FlashcardAnswerResult.Correct, Now);
        db.FlashcardReviews.Add(review); db.FlashcardReviewStates.Add(FlashcardReviewState.FromFirstReview(review));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await db.Database.OpenConnectionAsync();
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "SELECT Result FROM FlashcardReviews";
            Assert.Equal("correct", (string?)await command.ExecuteScalarAsync());
        }
        Assert.Equal(Now, (await db.FlashcardReviews.SingleAsync()).ReviewedAtUtc);

        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE FlashcardReviewStates SET ReviewCount = 0 WHERE FlashcardId = {card.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE FlashcardReviewStates SET CorrectCount = 2 WHERE FlashcardId = {card.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE FlashcardReviews SET ContentRevision = 0 WHERE ReviewId = {eventId}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE FlashcardReviews SET Result = 'maybe' WHERE ReviewId = {eventId}"));
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            db.FlashcardReviewStates.Add(FlashcardReviewState.FromFirstReview(
                Review(Guid.NewGuid(), Guid.NewGuid(), 1, FlashcardAnswerResult.Correct, Now)));
            await db.SaveChangesAsync();
        });
        db.ChangeTracker.Clear();

        var model = db.Model;
        Assert.Equal(DeleteBehavior.Cascade, Assert.Single(model.FindEntityType(typeof(FlashcardReviewState))!.GetForeignKeys()).DeleteBehavior);
        Assert.Equal(DeleteBehavior.Cascade, Assert.Single(model.FindEntityType(typeof(FlashcardReview))!.GetForeignKeys()).DeleteBehavior);
        Assert.Equal(new[] { "LastReviewedAtUtc", "FlashcardId" }, Assert.Single(model.FindEntityType(typeof(FlashcardReviewState))!.GetIndexes()).Properties.Select(p => p.Name));
        Assert.EndsWith("_AddQuizAttempts", (await db.Database.GetAppliedMigrationsAsync()).Last());
        var cardStore = scope.ServiceProvider.GetRequiredService<IFlashcardStore>();
        Assert.Equal(FlashcardStatus.Success, await cardStore.DeleteAsync(graph.Workspace.Id, graph.Map.Id, graph.Second.Id, card.Id, default));
        Assert.Empty(await db.FlashcardReviews.ToArrayAsync()); Assert.Empty(await db.FlashcardReviewStates.ToArrayAsync());
    }

    [Fact]
    public async Task Study_rotation_prioritizes_never_reviewed_then_orders_reviewed_by_last_review_and_ties()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var graph = await KnowledgeGraphFixture.CreateAsync(db);
        var unreviewedOld = Card(graph.Second.Id, "00000000-0000-0000-0000-000000000001", Now.AddDays(-4));
        var unreviewedNew = Card(graph.Second.Id, "00000000-0000-0000-0000-000000000004", Now.AddDays(-1));
        var reviewedOld = Card(graph.Second.Id, "00000000-0000-0000-0000-000000000003", Now.AddDays(-3));
        var reviewedNew = Card(graph.Second.Id, "00000000-0000-0000-0000-000000000002", Now.AddDays(-2));
        db.Flashcards.AddRange(unreviewedOld, unreviewedNew, reviewedOld, reviewedNew);
        var earlier = Review(Guid.NewGuid(), reviewedNew.Id, 1, FlashcardAnswerResult.Correct, Now.AddDays(-2));
        var later = Review(Guid.NewGuid(), reviewedOld.Id, 1, FlashcardAnswerResult.Incorrect, Now.AddDays(-1));
        db.FlashcardReviews.AddRange(earlier, later);
        db.FlashcardReviewStates.AddRange(FlashcardReviewState.FromFirstReview(earlier), FlashcardReviewState.FromFirstReview(later));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var reader = new FlashcardReader(db);

        var next = await reader.GetNextForStudyAsync(graph.Workspace.Id, graph.Map.Id, graph.Second.Id, default);
        Assert.True(next.NodeExists); Assert.Equal(unreviewedOld.Id, next.Item!.Id);
        Assert.Equal(0, next.Item.ReviewCount); Assert.Null(next.Item.LastReviewedAtUtc);
        Assert.Equal("front", next.Item.Front);

        await db.FlashcardReviewStates.ExecuteDeleteAsync();
        await db.FlashcardReviews.ExecuteDeleteAsync();
        var first = Review(Guid.NewGuid(), reviewedNew.Id, 1, FlashcardAnswerResult.Correct, Now.AddDays(-2));
        var second = Review(Guid.NewGuid(), reviewedOld.Id, 1, FlashcardAnswerResult.Correct, Now.AddDays(-2));
        var third = Review(Guid.NewGuid(), unreviewedOld.Id, 1, FlashcardAnswerResult.Correct, Now.AddDays(1));
        var fourth = Review(Guid.NewGuid(), unreviewedNew.Id, 1, FlashcardAnswerResult.Correct, Now.AddDays(1));
        db.FlashcardReviews.AddRange(first, second, third, fourth);
        db.FlashcardReviewStates.AddRange(FlashcardReviewState.FromFirstReview(first), FlashcardReviewState.FromFirstReview(second),
            FlashcardReviewState.FromFirstReview(third), FlashcardReviewState.FromFirstReview(fourth));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var orderedFirst = await reader.GetNextForStudyAsync(graph.Workspace.Id, graph.Map.Id, graph.Second.Id, default);
        Assert.Equal(reviewedOld.Id, orderedFirst.Item!.Id); // equal LastReviewedAtUtc, then CreatedAtUtc
        Assert.Equal(1, orderedFirst.Item.ReviewCount); Assert.Equal(1, orderedFirst.Item.CorrectCount);

        Assert.False((await reader.GetNextForStudyAsync(graph.Workspace.Id, graph.OtherMap.Id, graph.Second.Id, default)).NodeExists);
    }

    [Fact]
    public async Task Material_edit_resets_only_current_state_and_old_event_replay_does_not_restore_it()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var graph = await KnowledgeGraphFixture.CreateAsync(db); var card = Card(graph.Second.Id);
        db.Flashcards.Add(card); await db.SaveChangesAsync();
        var id = Guid.NewGuid(); var first = await new FlashcardStudyStore(db, new FixedTime(Now))
            .ReviewAsync(graph.Workspace.Id, graph.Map.Id, graph.Second.Id, card.Id, id, 1, FlashcardAnswerResult.Correct, default);
        Assert.Equal(FlashcardStatus.Success, first.Status); Assert.Equal((1, 1),
            (first.View!.CurrentReviewState.ReviewCount, first.View.CurrentReviewState.CorrectCount));

        var writer = scope.ServiceProvider.GetRequiredService<IFlashcardStore>();
        Assert.Equal(FlashcardStatus.Success, await writer.UpdateAsync(graph.Workspace.Id, graph.Map.Id, graph.Second.Id,
            card.Id, new("new front", FrontSpecified: true), Now.AddMinutes(1), default));
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.Flashcards.Where(value => value.Id == card.Id).Select(value => value.ContentRevision).SingleAsync());
        Assert.Empty(await db.FlashcardReviewStates.ToArrayAsync()); Assert.Single(await db.FlashcardReviews.ToArrayAsync());

        var reader = new FlashcardReader(db);
        var history = await reader.GetReviewAsync(graph.Workspace.Id, graph.Map.Id, graph.Second.Id, card.Id, id, default);
        Assert.NotNull(history); Assert.Equal(1, history!.Review.ContentRevision); Assert.Equal(FlashcardAnswerResult.Correct, history.Review.Result);
        Assert.Equal((2, 0, 0, (DateTimeOffset?)null), (history.CurrentReviewState.ContentRevision,
            history.CurrentReviewState.ReviewCount, history.CurrentReviewState.CorrectCount, history.CurrentReviewState.LastReviewedAtUtc));
        var replay = await new FlashcardStudyStore(db, new FixedTime(Now.AddHours(1))).ReviewAsync(graph.Workspace.Id,
            graph.Map.Id, graph.Second.Id, card.Id, id, 1, FlashcardAnswerResult.Correct, default);
        Assert.Equal(FlashcardStatus.Success, replay.Status);
        Assert.Equal(Now, replay.View!.Review.ReviewedAtUtc);
        Assert.Equal((2, 0, 0), (replay.View.CurrentReviewState.ContentRevision,
            replay.View.CurrentReviewState.ReviewCount, replay.View.CurrentReviewState.CorrectCount));
        var staleNewId = await new FlashcardStudyStore(db, new FixedTime(Now.AddHours(2))).ReviewAsync(graph.Workspace.Id,
            graph.Map.Id, graph.Second.Id, card.Id, Guid.NewGuid(), 1, FlashcardAnswerResult.Correct, default);
        Assert.Equal(FlashcardStatus.ContentChanged, staleNewId.Status);
        Assert.Empty(await db.FlashcardReviewStates.ToArrayAsync()); Assert.Single(await db.FlashcardReviews.ToArrayAsync());
    }

    [Fact]
    public async Task Review_replay_is_immutable_and_reused_id_with_different_payload_conflicts()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var graph = await KnowledgeGraphFixture.CreateAsync(db); var card = Card(graph.Second.Id); db.Flashcards.Add(card); await db.SaveChangesAsync();
        var id = Guid.NewGuid(); var store = new FlashcardStudyStore(db, new FixedTime(Now));
        var first = await store.ReviewAsync(graph.Workspace.Id, graph.Map.Id, graph.Second.Id, card.Id, id, 1, FlashcardAnswerResult.Correct, default);
        var replay = await store.ReviewAsync(graph.Workspace.Id, graph.Map.Id, graph.Second.Id, card.Id, id, 1, FlashcardAnswerResult.Correct, default);
        Assert.Equal(FlashcardStatus.Success, replay.Status);
        Assert.Equal(first.View!.Review, replay.View!.Review); Assert.Equal(1, replay.View.CurrentReviewState.ReviewCount);
        Assert.Equal(FlashcardStatus.ReviewIdConflict, (await store.ReviewAsync(graph.Workspace.Id, graph.Map.Id,
            graph.Second.Id, card.Id, id, 1, FlashcardAnswerResult.Incorrect, default)).Status);
        Assert.Equal(1, await db.FlashcardReviews.CountAsync()); Assert.Equal(1, (await db.FlashcardReviewStates.SingleAsync()).ReviewCount);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Independent_writers_serialize_same_and_distinct_review_ids_without_lost_updates(bool sameId)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        KnowledgeGraphFixture graph; Flashcard card; string connection;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); graph = await KnowledgeGraphFixture.CreateAsync(db);
            card = Card(graph.Second.Id); db.Flashcards.Add(card); await db.SaveChangesAsync(); connection = db.Database.GetConnectionString()!;
        }
        var firstId = Guid.NewGuid(); var secondId = sameId ? firstId : Guid.NewGuid();
        var (first, second) = await Contend(connection,
            db => new FlashcardStudyStore(db, new FixedTime(Now)).ReviewAsync(graph.Workspace.Id, graph.Map.Id,
                graph.Second.Id, card.Id, firstId, 1, FlashcardAnswerResult.Correct, default),
            db => new FlashcardStudyStore(db, new FixedTime(Now.AddMinutes(1))).ReviewAsync(graph.Workspace.Id, graph.Map.Id,
                graph.Second.Id, card.Id, secondId, 1, sameId ? FlashcardAnswerResult.Correct : FlashcardAnswerResult.Incorrect, default));
        Assert.Equal(FlashcardStatus.Success, first.Status); Assert.Equal(FlashcardStatus.Success, second.Status);
        Assert.Equal(firstId, first.View!.Review.ReviewId); Assert.Equal(secondId, second.View!.Review.ReviewId);
        if (sameId) Assert.Equal(first.View.Review, second.View.Review);
        Assert.Equal(sameId ? Now : Now.AddMinutes(1), second.View.Review.ReviewedAtUtc);
        Assert.Equal((sameId ? 1 : 2, 1), (second.View.CurrentReviewState.ReviewCount, second.View.CurrentReviewState.CorrectCount));
        using var verify = factory.Services.CreateScope(); var final = verify.ServiceProvider.GetRequiredService<TalynorDbContext>();
        Assert.Equal(sameId ? 1 : 2, await final.FlashcardReviews.CountAsync());
        var state = await final.FlashcardReviewStates.SingleAsync();
        Assert.Equal((sameId ? 1 : 2, 1, sameId ? Now : Now.AddMinutes(1)),
            (state.ReviewCount, state.CorrectCount, state.LastReviewedAtUtc));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task Review_edit_race_serializes_to_review_then_reset_or_stale_rejection(bool startEditFirst, bool existingState)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        KnowledgeGraphFixture graph; Flashcard card; string connection;
        var oldReviewId = Guid.NewGuid(); var reviewId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); graph = await KnowledgeGraphFixture.CreateAsync(db);
            card = Card(graph.Second.Id); db.Flashcards.Add(card);
            if (existingState)
            {
                var oldReview = Review(oldReviewId, card.Id, 1, FlashcardAnswerResult.Incorrect, Now.AddMinutes(-1));
                db.FlashcardReviews.Add(oldReview); db.FlashcardReviewStates.Add(FlashcardReviewState.FromFirstReview(oldReview));
            }
            await db.SaveChangesAsync(); connection = db.Database.GetConnectionString()!;
        }
        Task<FlashcardStatus> Edit(TalynorDbContext db) => new FlashcardStore(db).UpdateAsync(graph.Workspace.Id,
            graph.Map.Id, graph.Second.Id, card.Id, new("edited", FrontSpecified: true), Now.AddMinutes(1), default);
        Task<FlashcardReviewOperationResult> Submit(TalynorDbContext db) => new FlashcardStudyStore(db,
            new FixedTime(Now)).ReviewAsync(graph.Workspace.Id, graph.Map.Id, graph.Second.Id, card.Id, reviewId, 1,
                FlashcardAnswerResult.Correct, default);
        FlashcardStatus edit; FlashcardReviewOperationResult review;
        if (startEditFirst) (edit, review) = await Contend(connection, Edit, Submit);
        else (review, edit) = await Contend(connection, Submit, Edit);
        Assert.Equal(FlashcardStatus.Success, edit);
        Assert.Equal(startEditFirst ? FlashcardStatus.ContentChanged : FlashcardStatus.Success, review.Status);
        if (startEditFirst) Assert.Null(review.View);
        else
        {
            Assert.Equal(reviewId, review.View!.Review.ReviewId);
            Assert.Equal(1, review.View.Review.ContentRevision);
            Assert.Equal(Now, review.View.Review.ReviewedAtUtc);
            Assert.Equal((existingState ? 2 : 1, 1),
                (review.View.CurrentReviewState.ReviewCount, review.View.CurrentReviewState.CorrectCount));
        }
        using var verify = factory.Services.CreateScope(); var final = verify.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var edited = await final.Flashcards.SingleAsync(value => value.Id == card.Id);
        Assert.Equal((2, "edited", "back", Now.AddMinutes(1)), (edited.ContentRevision, edited.Front, edited.Back, edited.UpdatedAtUtc));
        Assert.Equal(startEditFirst ? 0 : 1, await final.FlashcardReviews.CountAsync(value => value.ReviewId == reviewId));
        Assert.Equal((existingState ? 1 : 0) + (startEditFirst ? 0 : 1), await final.FlashcardReviews.CountAsync());
        Assert.Equal(0, await final.FlashcardReviewStates.CountAsync());
        if (existingState)
        {
            var historical = await final.FlashcardReviews.SingleAsync(value => value.ReviewId == oldReviewId);
            Assert.Equal((1, FlashcardAnswerResult.Incorrect, Now.AddMinutes(-1)),
                (historical.ContentRevision, historical.Result, historical.ReviewedAtUtc));
        }
        var next = await new FlashcardReader(final).GetNextForStudyAsync(graph.Workspace.Id, graph.Map.Id, graph.Second.Id, default);
        Assert.Equal((2, 0, 0, (DateTimeOffset?)null), (next.Item!.ContentRevision, next.Item.ReviewCount,
            next.Item.CorrectCount, next.Item.LastReviewedAtUtc));
    }

    [Fact]
    public async Task Failure_after_event_and_state_save_before_commit_rolls_back_both_and_allows_context_reuse()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        KnowledgeGraphFixture graph; Flashcard card; string connection;
        using (var scope = factory.Services.CreateScope())
        {
            var seed = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); graph = await KnowledgeGraphFixture.CreateAsync(seed);
            card = Card(graph.Second.Id); seed.Flashcards.Add(card); await seed.SaveChangesAsync(); connection = seed.Database.GetConnectionString()!;
        }
        var fault = new AfterReviewSaveFault(); using var probe = new ProbeConnection(connection);
        await using var db = Context(probe, fault); var store = new FlashcardStudyStore(db, new FixedTime(Now));
        var reviewId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReviewAsync(graph.Workspace.Id, graph.Map.Id,
            graph.Second.Id, card.Id, reviewId, 1, FlashcardAnswerResult.Correct, default));
        Assert.True(fault.SawPersistedRows); Assert.Null(db.Database.CurrentTransaction);
        Assert.Empty(db.ChangeTracker.Entries()); Assert.Equal(System.Data.ConnectionState.Closed, probe.State);
        using (var verify = factory.Services.CreateScope())
        {
            var persisted = verify.ServiceProvider.GetRequiredService<TalynorDbContext>();
            Assert.Empty(await persisted.FlashcardReviews.ToArrayAsync()); Assert.Empty(await persisted.FlashcardReviewStates.ToArrayAsync());
            Assert.Equal(1, (await persisted.Flashcards.SingleAsync()).ContentRevision);
        }
        fault.Enabled = false;
        var retried = await store.ReviewAsync(graph.Workspace.Id, graph.Map.Id, graph.Second.Id, card.Id,
            reviewId, 1, FlashcardAnswerResult.Correct, default);
        Assert.Equal(FlashcardStatus.Success, retried.Status); Assert.Equal(1, retried.View!.CurrentReviewState.ReviewCount);
    }

    [Fact]
    public async Task AddFlashcardReviews_down_and_reapply_preserves_flashcards()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var graph = await KnowledgeGraphFixture.CreateAsync(db); var card = Card(graph.Second.Id); db.Flashcards.Add(card); await db.SaveChangesAsync();
        var eventId = Guid.NewGuid(); var review = Review(eventId, card.Id, 1, FlashcardAnswerResult.Incorrect, Now);
        db.FlashcardReviews.Add(review); db.FlashcardReviewStates.Add(FlashcardReviewState.FromFirstReview(review)); await db.SaveChangesAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        await db.GetService<IMigrator>().MigrateAsync("20261020100137_AddFlashcards");
        Assert.Equal(card.Id, await db.Flashcards.Select(value => value.Id).SingleAsync());
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("SELECT ReviewId FROM FlashcardReviews"));
        await db.Database.MigrateAsync();
        Assert.Equal(card.Id, await db.Flashcards.Select(value => value.Id).SingleAsync());
        Assert.Empty(await db.FlashcardReviews.ToArrayAsync()); Assert.Empty(await db.FlashcardReviewStates.ToArrayAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static Flashcard Card(Guid nodeId, string? id = null, DateTimeOffset? created = null)
    {
        var card = Flashcard.Create(nodeId, "front", "back", created ?? Now);
        if (id is not null) typeof(Flashcard).GetProperty(nameof(Flashcard.Id))!.SetValue(card, Guid.Parse(id));
        return card;
    }
    private static FlashcardReview Review(Guid id, Guid cardId, int revision, FlashcardAnswerResult result, DateTimeOffset time) =>
        FlashcardReview.Create(id, cardId, revision, result, time);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TalynorDbContext Context(SqliteConnection connection, params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<TalynorDbContext>().UseSqlite(connection).AddInterceptors(interceptors).Options);

    private static async Task<(TFirst First, TSecond Second)> Contend<TFirst, TSecond>(string connection,
        Func<TalynorDbContext, Task<TFirst>> firstWrite, Func<TalynorDbContext, Task<TSecond>> secondWrite)
    {
        var hold = new HoldFirstRead();
        using var firstConnection = new ProbeConnection(connection); using var secondConnection = new ProbeConnection(connection);
        await using var firstDb = Context(firstConnection, hold); await using var secondDb = Context(secondConnection);
        Assert.NotSame(firstDb, secondDb); Assert.NotSame(firstConnection, secondConnection);
        var first = Task.Run(() => firstWrite(firstDb)); Task<TSecond>? second = null;
        try
        {
            // The first real BEGIN IMMEDIATE has returned and its transaction stays open at this read.
            await hold.Entered.Task.WaitAsync(Timeout);
            Assert.True(firstConnection.Acquired.Task.IsCompletedSuccessfully);
            second = Task.Run(() => secondWrite(secondDb));
            await secondConnection.Attempted.Task.WaitAsync(Timeout);
            Assert.False(secondConnection.Acquired.Task.IsCompleted);
            Assert.False(second.IsCompleted);
        }
        finally
        {
            hold.Release.TrySetResult();
            if (second is null) await first.WaitAsync(Timeout);
            else await Task.WhenAll(first, second).WaitAsync(Timeout);
        }
        Assert.False(firstConnection.Deferred); Assert.False(secondConnection.Deferred);
        Assert.True(secondConnection.Acquired.Task.IsCompletedSuccessfully);
        Assert.Null(firstDb.Database.CurrentTransaction); Assert.Null(secondDb.Database.CurrentTransaction);
        Assert.Equal(System.Data.ConnectionState.Closed, firstConnection.State);
        Assert.Equal(System.Data.ConnectionState.Closed, secondConnection.State);
        return (await first, await second!);
    }

    private sealed class ProbeConnection(string connection) : SqliteConnection(
        new SqliteConnectionStringBuilder(connection) { DefaultTimeout = 10 }.ConnectionString)
    {
        public bool Deferred = true;
        public TaskCompletionSource Attempted = Signal(), Acquired = Signal();
        public override SqliteTransaction BeginTransaction(bool deferred)
        {
            Deferred = deferred; Attempted.TrySetResult();
            var transaction = base.BeginTransaction(deferred);
            Acquired.TrySetResult(); return transaction;
        }
    }

    private sealed class HoldFirstRead : DbCommandInterceptor
    {
        public TaskCompletionSource Entered = Signal(), Release = Signal(); private int count;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref count) == 1)
            {
                Assert.NotNull(command.Transaction); Entered.TrySetResult(); await Release.Task.WaitAsync(Timeout, ct);
            }
            return result;
        }
    }

    private sealed class AfterReviewSaveFault : SaveChangesInterceptor
    {
        public bool Enabled = true, SawPersistedRows;
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (Enabled)
            {
                var db = (TalynorDbContext)data.Context!; Assert.NotNull(db.Database.CurrentTransaction);
                Assert.Equal(1, await db.FlashcardReviews.CountAsync(ct));
                Assert.Equal(1, (await db.FlashcardReviewStates.SingleAsync(ct)).ReviewCount);
                SawPersistedRows = true;
                throw new InvalidOperationException("Synthetic failure after review rows were saved, before commit.");
            }
            return result;
        }
    }
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}

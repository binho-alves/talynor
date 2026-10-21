using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Application.KnowledgeMaps;
using Talynor.Application.Quizzes;
using Talynor.Domain.Quizzes;
using Talynor.Domain.StudySessions;
using Talynor.Infrastructure.Persistence;
using Talynor.Infrastructure.Quizzes;

namespace Talynor.IntegrationTests;

public class QuizPersistenceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddTicks(1234567);
    internal static QuizInput Definition(string title = "title") => new(title,
        [new("first question\n", ["a", "b"], 1), new("second question", ["x", "y", "z"], 0)]);
    private static Quiz New(Guid node, string title = "title", DateTimeOffset? now = null)
    { var input = Definition(title); return Quiz.Create(node, input.Title, input.Questions, now ?? Now); }

    [Fact]
    public async Task Utc_model_sql_order_pagination_and_key_free_single_query_projection()
    {
        var commands = new Commands();
        using var factory = new RegistrationTestFactory(configureServices: s => s.AddDbContext<TalynorDbContext>(o => o.AddInterceptors(commands)));
        await factory.InitializeAsync(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var g = await KnowledgeGraphFixture.CreateAsync(db);
        var high = New(g.Second.Id, "high", Now.ToOffset(TimeSpan.FromHours(-3)));
        var low = New(g.Second.Id, "low"); var early = New(g.Second.Id, "early", Now.AddSeconds(-1));
        typeof(Quiz).GetProperty(nameof(Quiz.Id))!.SetValue(high, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        typeof(Quiz).GetProperty(nameof(Quiz.Id))!.SetValue(low, Guid.Parse("00000000-0000-0000-0000-000000000001"));
        // Update FK identities for the deliberate test-only PK change.
        foreach (var quiz in new[] { high, low }) foreach (var q in quiz.Questions)
            typeof(QuizQuestion).GetProperty(nameof(QuizQuestion.QuizId))!.SetValue(q, quiz.Id);
        db.Quizzes.AddRange(high, low, early); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var recovered = await db.Quizzes.SingleAsync(q => q.Id == high.Id);
        Assert.Equal(Now, recovered.CreatedAtUtc); Assert.Equal(TimeSpan.Zero, recovered.CreatedAtUtc.Offset);
        Assert.Equal(recovered.CreatedAtUtc, recovered.UpdatedAtUtc); db.ChangeTracker.Clear();
        var model = db.Model.FindEntityType(typeof(Quiz))!;
        Assert.Equal(new[] { "ContentRevision", "CreatedAtUtc", "Id", "KnowledgeNodeId", "Title", "UpdatedAtUtc" }, model.GetProperties().Select(p => p.Name).Order());
        Assert.Equal(typeof(DateTime), model.FindProperty("CreatedAtUtc")!.GetValueConverter()!.ProviderClrType);
        Assert.Equal(200, model.FindProperty("Title")!.GetMaxLength());
        Assert.Equal(new[] { "KnowledgeNodeId", "CreatedAtUtc", "Id" }, Assert.Single(model.GetIndexes()).Properties.Select(p => p.Name));
        Assert.Equal(DeleteBehavior.Cascade, Assert.Single(model.GetForeignKeys()).DeleteBehavior);
        Assert.Equal(1000, db.Model.FindEntityType(typeof(QuizQuestion))!.FindProperty("Text")!.GetMaxLength());
        Assert.Equal(500, db.Model.FindEntityType(typeof(QuizOption))!.FindProperty("Text")!.GetMaxLength());
        Assert.Contains(db.Model.FindEntityType(typeof(QuizOption))!.GetIndexes(), i => i.IsUnique && i.GetFilter() == "IsCorrect = 1");
        var reader = scope.ServiceProvider.GetRequiredService<IQuizReader>(); commands.Sql.Clear();
        var page = await reader.ListAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, 0, 2, default);
        Assert.Equal(new[] { early.Id, low.Id }, page!.Quizzes.Select(q => q.Id)); Assert.True(page.HasMore);
        var sql = commands.Sql.Last().Replace("\"", "");
        Assert.Matches(@"ORDER BY \w+\.CreatedAtUtc, \w+\.Id\s+LIMIT", sql); Assert.DoesNotContain("COUNT(", sql.ToUpperInvariant());
        Assert.Contains("WorkspaceId", sql); Assert.Contains("KnowledgeMapId", sql);
        Assert.Equal(high.Id, Assert.Single((await reader.ListAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, 2, 2, default))!.Quizzes).Id);
        Assert.False((await reader.ListAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, 0, 3, default))!.HasMore);
        commands.Sql.Clear(); var take = await reader.TakeAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, high.Id, default);
        Assert.NotNull(take); Assert.Single(commands.Sql); Assert.DoesNotContain("IsCorrect", commands.Sql[0]);
        Assert.Equal(new[] { 1, 2 }, take.Questions.Select(q => q.Position));
        Assert.Equal(new[] { 1, 2, 3 }, take.Questions[1].Options.Select(o => o.Position));
        commands.Sql.Clear(); var detail = await reader.GetAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, high.Id, default);
        Assert.Single(commands.Sql); Assert.Equal(1, detail!.Questions[0].CorrectOptionIndex); Assert.Equal(0, detail.Questions[1].CorrectOptionIndex);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Null(await reader.GetAsync(g.OtherWorkspace.Id, g.Map.Id, g.Second.Id, high.Id, default));
        Assert.Null(await reader.TakeAsync(g.Workspace.Id, g.Child.Id, g.Second.Id, high.Id, default));
        Assert.Empty((await reader.ListAsync(g.Workspace.Id, g.Map.Id, g.First.Id, 0, 20, default))!.Quizzes);
    }

    [Fact]
    public async Task Database_enforces_fks_positive_revision_positions_and_at_most_one_correct_option()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var g = await KnowledgeGraphFixture.CreateAsync(db);
        db.Quizzes.Add(New(Guid.NewGuid())); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        var quiz = New(g.Second.Id); db.Quizzes.Add(quiz); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var q1 = quiz.Questions[0]; var q2 = quiz.Questions[1]; var option = q1.Options[0];
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Quizzes SET ContentRevision=0 WHERE Id={quiz.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuizQuestions SET Position=0 WHERE Id={q1.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuizQuestions SET Position=1 WHERE Id={q2.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuizQuestions SET QuizId={Guid.NewGuid()} WHERE Id={q1.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuizOptions SET IsCorrect=1 WHERE Id={option.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuizOptions SET IsCorrect=2 WHERE Id={option.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuizOptions SET Position=2 WHERE Id={option.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuizOptions SET Position=5 WHERE Id={option.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuizOptions SET QuestionId={Guid.NewGuid()} WHERE Id={option.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuizOptions SET Text=NULL WHERE Id={option.Id}"));
    }

    [Fact]
    public async Task Noop_invalid_stale_overflow_and_cancellation_do_not_save_or_replace_children()
    {
        var saves = new Saves(); using var factory = new RegistrationTestFactory(saves); await factory.InitializeAsync();
        KnowledgeGraphFixture g; Quiz quiz;
        using (var seed = factory.Services.CreateScope())
        { var db = seed.ServiceProvider.GetRequiredService<TalynorDbContext>(); g = await KnowledgeGraphFixture.CreateAsync(db);
            quiz = New(g.Second.Id); db.Quizzes.Add(quiz); await db.SaveChangesAsync(); }
        using var scope = factory.Services.CreateScope(); var writer = scope.ServiceProvider.GetRequiredService<IQuizStore>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var before = saves.Count;
        async Task<QuizStatus> Replace(int revision, QuizInput input, CancellationToken ct = default) =>
            await writer.ReplaceAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, quiz.Id, revision, input, Now.AddDays(1), ct);
        Assert.Equal(QuizStatus.InvalidInput, await Replace(1, Definition(" title ")));
        Assert.Equal(QuizStatus.InvalidInput, await Replace(1, new("candidate", [new("q", ["a", "a"], 0)])));
        Assert.Equal(QuizStatus.ContentChanged, await Replace(2, Definition("new")));
        Assert.Equal(QuizStatus.InvalidInput, await Replace(0, Definition("new")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Replace(1, Definition("new"), new CancellationToken(true)));
        Assert.Equal(before, saves.Count);
        var reader = scope.ServiceProvider.GetRequiredService<IQuizReader>();
        var unchanged = await reader.GetAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, quiz.Id, default);
        Assert.Equal(quiz.Questions.Select(q => q.Id), unchanged!.Questions.Select(q => q.Id));
        Assert.Equal(QuizStatus.Success, await Replace(1, Definition("new"))); Assert.Equal(before + 1, saves.Count);
        var changed = await reader.GetAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, quiz.Id, default);
        Assert.Equal(2, changed!.ContentRevision); Assert.Equal(Now, changed.CreatedAtUtc); Assert.Equal(Now.AddDays(1), changed.UpdatedAtUtc);
        Assert.DoesNotContain(changed.Questions[0].Id, unchanged.Questions.Select(q => q.Id));
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"UPDATE Quizzes SET ContentRevision={int.MaxValue} WHERE Id={quiz.Id}");
        await Assert.ThrowsAsync<OverflowException>(() => Replace(int.MaxValue, Definition("overflow")));
        var after = await reader.GetAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, quiz.Id, default);
        Assert.Equal(int.MaxValue, after!.ContentRevision); Assert.Equal("new", after.Title);
        Assert.Equal(changed.Questions.Select(q => q.Id), after.Questions.Select(q => q.Id)); Assert.Equal(changed.UpdatedAtUtc, after.UpdatedAtUtc);
        Assert.Empty(dbContext.ChangeTracker.Entries()); Assert.Equal(before + 1, saves.Count);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Failure_after_old_children_delete_rolls_back_everything_and_context_can_be_reused(bool afterSave)
    {
        var saves = new Saves(); using var factory = new RegistrationTestFactory(saves); await factory.InitializeAsync();
        KnowledgeGraphFixture g; Quiz quiz;
        using (var seed = factory.Services.CreateScope())
        { var db = seed.ServiceProvider.GetRequiredService<TalynorDbContext>(); g = await KnowledgeGraphFixture.CreateAsync(db);
            quiz = New(g.Second.Id); db.Quizzes.Add(quiz); await db.SaveChangesAsync(); }
        using var scope = factory.Services.CreateScope(); var dbContext = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<IQuizStore>(); saves.Fail = !afterSave; saves.FailAfter = afterSave;
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.ReplaceAsync(g.Workspace.Id, g.Map.Id, g.Second.Id,
            quiz.Id, 1, Definition("losing"), Now.AddDays(1), default)); saves.Fail = false; saves.FailAfter = false;
        Assert.Empty(dbContext.ChangeTracker.Entries());
        var before = await scope.ServiceProvider.GetRequiredService<IQuizReader>().GetAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, quiz.Id, default);
        Assert.Equal("title", before!.Title); Assert.Equal(1, before.ContentRevision); Assert.Equal(Now, before.UpdatedAtUtc);
        Assert.Equal(quiz.Questions.Select(q => q.Id), before.Questions.Select(q => q.Id));
        Assert.Equal(5, await dbContext.QuizOptions.CountAsync());
        Assert.Equal(QuizStatus.Success, await writer.ReplaceAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, quiz.Id, 1, Definition("winner"), Now.AddDays(1), default));
    }

    [Fact]
    public async Task Independent_updates_with_same_revision_have_one_winner_no_mixed_children()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        KnowledgeGraphFixture g; Quiz quiz; string cs;
        using (var seed = factory.Services.CreateScope())
        { var db = seed.ServiceProvider.GetRequiredService<TalynorDbContext>(); g = await KnowledgeGraphFixture.CreateAsync(db);
            quiz = New(g.Second.Id); db.Quizzes.Add(quiz); await db.SaveChangesAsync(); cs = db.Database.GetConnectionString()!; }
        var gate = new ReadGate(); var secondConnection = new OpenGate();
        await using var firstDb = new TalynorDbContext(new DbContextOptionsBuilder<TalynorDbContext>().UseSqlite(cs).AddInterceptors(gate).Options);
        await using var secondDb = new TalynorDbContext(new DbContextOptionsBuilder<TalynorDbContext>().UseSqlite(cs).AddInterceptors(secondConnection).Options);
        var firstInput = new QuizInput("first", [new("first Q", ["first A", "first B"], 1)]);
        var secondInput = new QuizInput("second", [new("second Q", ["second A", "second B"], 0)]);
        var first = Task.Run(() => new QuizStore(firstDb).ReplaceAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, quiz.Id, 1, firstInput, Now.AddDays(1), default));
        Task<QuizStatus>? second = null;
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // A separate reader sees the entire committed old definition while the edit
            // holds RESERVED but has not written; after commit it sees the entire new one.
            using (var snapshot = factory.Services.CreateScope())
            {
                var old = await snapshot.ServiceProvider.GetRequiredService<IQuizReader>().TakeAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, quiz.Id, default);
                Assert.Equal(1, old!.ContentRevision); Assert.Equal("title", old.Title);
                Assert.Equal(new[] { "first question\n", "second question" }, old.Questions.Select(q => q.Text));
            }
            second = Task.Run(() => new QuizStore(secondDb).ReplaceAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, quiz.Id, 1, secondInput, Now.AddDays(2), default));
            await secondConnection.Opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { gate.Release.TrySetResult(); }
        Assert.Equal(new[] { QuizStatus.Success, QuizStatus.ContentChanged }, await Task.WhenAll(first, second!).WaitAsync(TimeSpan.FromSeconds(30)));
        using var read = factory.Services.CreateScope(); var detail = await read.ServiceProvider.GetRequiredService<IQuizReader>().GetAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, quiz.Id, default);
        Assert.Equal("first", detail!.Title); Assert.Equal(2, detail.ContentRevision); Assert.Single(detail.Questions);
        Assert.Equal("first Q", detail.Questions[0].Text); Assert.Equal(new[] { "first A", "first B" }, detail.Questions[0].Options.Select(o => o.Text));
        Assert.Equal(1, detail.Questions[0].CorrectOptionIndex); Assert.Empty(firstDb.ChangeTracker.Entries()); Assert.Empty(secondDb.ChangeTracker.Entries());
        var take = await read.ServiceProvider.GetRequiredService<IQuizReader>().TakeAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, quiz.Id, default);
        Assert.Equal(2, take!.ContentRevision); Assert.Equal("first", take.Title); Assert.Equal("first Q", Assert.Single(take.Questions).Text);
    }

    [Theory]
    [InlineData(false, "none")] [InlineData(true, "none")]
    [InlineData(false, "active")] [InlineData(true, "active")]
    [InlineData(false, "completed")] [InlineData(true, "completed")]
    [InlineData(false, "edge")] [InlineData(true, "child")]
    public async Task Allowed_node_map_delete_cascades_quizzes_and_existing_blockers_prevent_partial_cascade(bool map, string blocker)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var g = await KnowledgeGraphFixture.CreateAsync(db);
        if (blocker != "edge") await db.KnowledgeEdges.ExecuteDeleteAsync();
        var targetMap = blocker == "child" ? g.Child : g.Map;
        var targetNode = blocker == "child" ? g.ChildNode : g.Second;
        var quiz = New(targetNode.Id); db.Quizzes.Add(quiz);
        if (blocker is "active" or "completed")
        { var study = StudySession.Start(targetNode.Id, Now); if (blocker == "completed") Assert.True(study.TryComplete(Now.AddMinutes(1))); db.StudySessions.Add(study); }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var graphWriter = scope.ServiceProvider.GetRequiredService<IKnowledgeMapStore>();
        if (map) graphWriter.RemoveMap((await graphWriter.GetMapAsync(targetMap.Id, g.Workspace.Id, default))!);
        else graphWriter.RemoveNode((await graphWriter.GetNodeAsync(targetNode.Id, targetMap.Id, g.Workspace.Id, default))!);
        var status = await graphWriter.SaveAsync(new(map ? KnowledgeWriteOperation.DeleteMap : KnowledgeWriteOperation.DeleteNode,
            g.Workspace.Id, targetMap.Id, map ? targetMap.Id : targetNode.Id), default);
        Assert.Equal(blocker == "none" ? KnowledgeWriteStatus.Success : map ? KnowledgeWriteStatus.MapInUse : KnowledgeWriteStatus.NodeInUse, status);
        Assert.Equal(blocker == "none" ? 0 : 1, await db.Quizzes.CountAsync());
        Assert.Equal(blocker == "none" ? 0 : 2, await db.QuizQuestions.CountAsync());
        Assert.Equal(blocker == "none" ? 0 : 5, await db.QuizOptions.CountAsync());
    }

    [Fact]
    public async Task Migration_up_down_reapply_preserves_previous_data_indexes_and_graph_triggers()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var g = await KnowledgeGraphFixture.CreateAsync(db);
        var card = Talynor.Domain.Flashcards.Flashcard.Create(g.Second.Id, "earlier front", "earlier back", Now);
        var content = Talynor.Domain.KnowledgeContents.KnowledgeContent.Create(g.Second.Id,
            Talynor.Domain.KnowledgeContents.KnowledgeContentType.Note, "earlier note", "earlier body", null, Now);
        var session = StudySession.Start(g.Second.Id, Now);
        db.Flashcards.Add(card); db.KnowledgeContents.Add(content); db.StudySessions.Add(session);
        db.Quizzes.Add(New(g.Second.Id)); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.EndsWith("_AddQuizzes", (await db.Database.GetAppliedMigrationsAsync()).Last()); Assert.False(db.Database.HasPendingModelChanges());
        async Task<string[]> Schema() => await db.Database.SqlQueryRaw<string>("SELECT sql AS Value FROM sqlite_master WHERE type IN ('index','trigger') AND tbl_name NOT IN ('Quizzes','QuizQuestions','QuizOptions') AND sql IS NOT NULL ORDER BY name").ToArrayAsync();
        var priorSchema = await Schema();
        await db.GetService<IMigrator>().MigrateAsync("20261020115944_AddFlashcardReviews");
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("SELECT Id FROM Quizzes"));
        Assert.Equal(priorSchema, await Schema()); Assert.Equal(3, await db.KnowledgeNodes.CountAsync());
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE KnowledgeNodes SET ChildMapId={g.OtherMap.Id} WHERE Id={g.First.Id}"));
        await db.Database.MigrateAsync(); Assert.Equal(priorSchema, await Schema()); Assert.Empty(await db.Quizzes.ToArrayAsync());
        Assert.Equal("earlier front", (await db.Flashcards.AsNoTracking().SingleAsync(c => c.Id == card.Id)).Front);
        Assert.Equal("earlier body", (await db.KnowledgeContents.AsNoTracking().SingleAsync(c => c.Id == content.Id)).Body);
        Assert.Equal(Now, (await db.StudySessions.AsNoTracking().SingleAsync(s => s.Id == session.Id)).StartedAtUtc);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync()); Assert.False(db.Database.HasPendingModelChanges());
        db.Quizzes.Add(New(g.Second.Id)); await db.SaveChangesAsync();
        Assert.Equal(2, await db.QuizQuestions.CountAsync()); Assert.Equal(5, await db.QuizOptions.CountAsync());
        Assert.Equal("ok", Assert.Single(await db.Database.SqlQueryRaw<string>("PRAGMA integrity_check").ToArrayAsync()));
        var connection = db.Database.GetDbConnection(); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "PRAGMA foreign_key_check";
        await using var rows = await command.ExecuteReaderAsync(); Assert.False(await rows.ReadAsync());
    }

    private sealed class Saves : SaveChangesInterceptor
    {
        public int Count; public bool Fail, FailAfter;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
        { Count++; if (Fail) throw new InvalidOperationException("Synthetic pre-save failure"); return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData e, int result, CancellationToken ct = default)
        { if (FailAfter) throw new InvalidOperationException("Synthetic post-save/pre-commit failure"); return ValueTask.FromResult(result); }
    }
    private sealed class Commands : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData e, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Sql.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
    private sealed class ReadGate : DbCommandInterceptor
    {
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData e, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { if (command.CommandText.Contains("FROM \"Quizzes\"")) { Entered.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), ct); } return result; }
    }
    private sealed class OpenGate : DbConnectionInterceptor
    {
        public TaskCompletionSource Opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData e, CancellationToken ct = default)
        { Opened.TrySetResult(); return Task.CompletedTask; }
    }
}

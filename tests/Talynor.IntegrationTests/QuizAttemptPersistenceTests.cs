using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Application.KnowledgeMaps;
using Talynor.Application.Quizzes;
using Talynor.Domain.Quizzes;
using Talynor.Domain.StudySessions;
using Talynor.Infrastructure.Persistence;
using Talynor.Infrastructure.Quizzes;

namespace Talynor.IntegrationTests;

public class QuizAttemptPersistenceTests
{
    internal static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddTicks(1234567);
    internal static Quiz NewQuiz(Guid node) => Quiz.Create(node, "original title", Enumerable.Range(0, 3)
        .Select(i => new QuizQuestionInput($"question {i}\n", ["first ", "second", "third"], i)).ToArray(), Now);
    internal static QuizAnswerInput[] Answers(Quiz quiz, int correct = 2) => quiz.Questions.Select((q, i) =>
        new QuizAnswerInput(q.Id, q.Options[i < correct ? i : (i + 1) % 3].Id)).ToArray();
    internal static async Task<(KnowledgeGraphFixture Graph, Quiz Quiz)> Seed(TalynorDbContext db)
    {
        var graph = await KnowledgeGraphFixture.CreateAsync(db); var quiz = NewQuiz(graph.Second.Id);
        db.Quizzes.Add(quiz); await db.SaveChangesAsync(); db.ChangeTracker.Clear(); return (graph, quiz);
    }
    private static Task<QuizAttemptOperationResult> Submit(TalynorDbContext db, KnowledgeGraphFixture g, Quiz q, Guid id,
        QuizAnswerInput[]? answers = null, int revision = 1, DateTimeOffset? now = null) => new QuizAttemptStore(db)
        .SubmitAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, q.Id, id, revision, answers ?? Answers(q), now ?? Now, default);

    [Fact]
    public async Task Model_and_real_constraints_keep_history_independent_from_current_children()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var (g, q) = await Seed(db); var id = Guid.NewGuid();
        var result = await Submit(db, g, q, id, now: Now.ToOffset(TimeSpan.FromHours(-3)));
        Assert.Equal(QuizStatus.Success, result.Status); Assert.Equal(66.67m, result.Attempt!.AccuracyPercentage);
        var attempt = await db.QuizAttempts.AsNoTracking().SingleAsync(); Assert.Equal(TimeSpan.Zero, attempt.SubmittedAtUtc.Offset); Assert.Equal(Now, attempt.SubmittedAtUtc);
        var model = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(QuizAttempt))!;
        Assert.Equal(new[] { "AttemptId", "ContentRevision", "CorrectCount", "QuestionCount", "QuizId", "QuizTitle", "SubmittedAtUtc" }, model.GetProperties().Select(p => p.Name).Order());
        var index = Assert.Single(model.GetIndexes()); Assert.Equal(new[] { "QuizId", "SubmittedAtUtc", "AttemptId" }, index.Properties.Select(p => p.Name));
        Assert.Equal(new[] { false, true, true }, index.IsDescending); Assert.Equal(typeof(DateTime), model.FindProperty("SubmittedAtUtc")!.GetValueConverter()!.ProviderClrType);
        Assert.Equal(typeof(Quiz), Assert.Single(model.GetForeignKeys()).PrincipalEntityType.ClrType);
        var answerModel = db.Model.FindEntityType(typeof(QuizAttemptAnswer))!;
        Assert.Equal(typeof(QuizAttempt), Assert.Single(answerModel.GetForeignKeys()).PrincipalEntityType.ClrType);
        Assert.Null(answerModel.FindProperty("IsCorrect")); Assert.Equal(1000, answerModel.FindProperty("Text")!.GetMaxLength());
        var optionModel = db.Model.FindEntityType(typeof(QuizAttemptAnswerOption))!;
        Assert.Equal(typeof(QuizAttemptAnswer), Assert.Single(optionModel.GetForeignKeys()).PrincipalEntityType.ClrType);
        Assert.Equal(500, optionModel.FindProperty("Text")!.GetMaxLength());
        foreach (var sql in new[] { "UPDATE QuizAttempts SET ContentRevision=0", "UPDATE QuizAttempts SET QuestionCount=0",
            "UPDATE QuizAttempts SET QuestionCount=11", "UPDATE QuizAttempts SET CorrectCount=-1", "UPDATE QuizAttempts SET CorrectCount=4",
            "UPDATE QuizAttemptAnswers SET Position=0", "UPDATE QuizAttemptAnswerOptions SET Position=5" })
            await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(sql));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuizAttempts SET QuizId={Guid.NewGuid()} WHERE AttemptId={id}"));
        var question = q.Questions[0];
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuizAttemptAnswers SET Position=1 WHERE QuestionId={q.Questions[1].Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuizAttemptAnswerOptions SET Position=1 WHERE OptionId={question.Options[1].Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuizAttemptAnswers SET AttemptId={Guid.NewGuid()} WHERE QuestionId={question.Id}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuizAttemptAnswerOptions SET QuestionId={Guid.NewGuid()} WHERE OptionId={question.Options[0].Id}"));
        // Historical IDs are values, not current-child FKs.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE QuizAttemptAnswerOptions SET OptionId={Guid.NewGuid()} WHERE OptionId={question.Options[0].Id}");
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Replay_precedes_revision_and_preserves_complete_snapshot_after_replace()
    {
        var saves = new Saves(); using var factory = new RegistrationTestFactory(saves); await factory.InitializeAsync(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var (g, q) = await Seed(db); var id = Guid.NewGuid();
        var first = (await Submit(db, g, q, id)).Attempt!; var json = JsonSerializer.Serialize(first); var before = saves.Count;
        var replay = await Submit(db, g, q, id, Answers(q).Reverse().ToArray(), now: Now.AddDays(1));
        Assert.Equal(json, JsonSerializer.Serialize(replay.Attempt)); Assert.Equal(before, saves.Count);
        var edit = await new QuizStore(db).ReplaceAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, q.Id, 1,
            new("changed title", [new("changed Q", ["changed A", "changed B"], 1)]), Now.AddHours(1), default);
        Assert.Equal(QuizStatus.Success, edit); before = saves.Count;
        Assert.Equal(json, JsonSerializer.Serialize((await Submit(db, g, q, id, now: Now.AddDays(2))).Attempt));
        var reader = new QuizAttemptReader(db);
        Assert.Equal(json, JsonSerializer.Serialize(await reader.GetAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, q.Id, id, default)));
        Assert.Equal(QuizStatus.ContentChanged, (await Submit(db, g, q, Guid.NewGuid())).Status);
        Assert.Equal(QuizStatus.AttemptIdConflict, (await Submit(db, g, q, id, revision: 2)).Status);
        Assert.Equal(QuizStatus.AttemptIdConflict, (await Submit(db, g, q, id, Answers(q, 3))).Status);
        Assert.Equal(before, saves.Count); Assert.Single(await db.QuizAttempts.AsNoTracking().ToArrayAsync());
        Assert.Equal(3, await db.QuizAttemptAnswers.CountAsync()); Assert.Equal(9, await db.QuizAttemptAnswerOptions.CountAsync()); Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task History_is_sql_scoped_ordered_with_adversarial_id_ties_and_bounded_without_count()
    {
        var sql = new Commands(); using var factory = new RegistrationTestFactory(configureServices: s => s.AddDbContext<TalynorDbContext>(o => o.AddInterceptors(sql)));
        await factory.InitializeAsync(); using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var (g, q) = await Seed(db); var low = Guid.Parse("00000000-0000-0000-0000-000000000001"); var high = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"); var later = Guid.NewGuid();
        await Submit(db, g, q, low); await Submit(db, g, q, high); await Submit(db, g, q, later, now: Now.AddSeconds(1));
        var reader = new QuizAttemptReader(db); sql.Sql.Clear(); var page = await reader.ListAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, q.Id, 0, 2, default);
        Assert.Equal(new[] { later, high }, page!.Attempts.Select(a => a.AttemptId)); Assert.True(page.HasMore);
        var statement = sql.Sql.Last().Replace("\"", ""); Assert.Matches(@"ORDER BY \w+\.SubmittedAtUtc DESC, \w+\.AttemptId DESC\s+LIMIT", statement);
        Assert.DoesNotContain("COUNT(", statement.ToUpperInvariant()); Assert.DoesNotContain("QuizAttemptAnswers", statement); Assert.Contains("WorkspaceId", statement);
        Assert.Equal(low, Assert.Single((await reader.ListAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, q.Id, 2, 2, default))!.Attempts).AttemptId);
        Assert.False((await reader.ListAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, q.Id, 0, 3, default))!.HasMore);
        sql.Sql.Clear(); var detail = await reader.GetAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, q.Id, high, default);
        Assert.Single(sql.Sql); Assert.DoesNotContain("QuizQuestions", sql.Sql[0]); Assert.DoesNotContain("QuizOptions", sql.Sql[0]);
        Assert.Equal(new[] { 1, 2, 3 }, detail!.Answers.Select(a => a.Position)); Assert.All(detail.Answers, a => Assert.Equal(new[] { 1, 2, 3 }, a.Options.Select(o => o.Position)));
        Assert.Null(await reader.ListAsync(g.OtherWorkspace.Id, g.Map.Id, g.Second.Id, q.Id, 0, 20, default));
        Assert.Null(await reader.GetAsync(g.OtherWorkspace.Id, g.Map.Id, g.Second.Id, q.Id, high, default));
        Assert.Null(await reader.GetAsync(g.Workspace.Id, g.Child.Id, g.Second.Id, q.Id, high, default)); Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Invalid_sheets_and_global_id_collision_never_write_or_disclose_foreign_snapshot()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var (g, q) = await Seed(db); var id = Guid.NewGuid(); await Submit(db, g, q, id);
        var other = NewQuiz(g.First.Id); db.Quizzes.Add(other); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var store = new QuizAttemptStore(db);
        var collision = await store.SubmitAsync(g.Workspace.Id, g.Map.Id, g.First.Id, other.Id, id, 1, Answers(other), Now, default);
        Assert.Equal(QuizStatus.AttemptIdConflict, collision.Status); Assert.Null(collision.Attempt);
        var foreign = await store.SubmitAsync(g.OtherWorkspace.Id, g.Map.Id, g.Second.Id, q.Id, id, 1, Answers(q), Now, default);
        Assert.Equal(QuizStatus.NotFound, foreign.Status); Assert.Null(foreign.Attempt);
        QuizAnswerInput[][] invalidSheets = [Answers(q)[..2], [..Answers(q), new QuizAnswerInput(Guid.NewGuid(), Guid.NewGuid())],
            [Answers(q)[0], Answers(q)[0], Answers(q)[2]], [new(q.Questions[0].Id, q.Questions[1].Options[0].Id), ..Answers(q)[1..]]];
        foreach (var answers in invalidSheets)
            Assert.Equal(QuizStatus.InvalidInput, (await Submit(db, g, q, Guid.NewGuid(), answers)).Status);
        Assert.Single(await db.QuizAttempts.AsNoTracking().ToArrayAsync()); Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Failure_before_save_or_after_save_before_commit_rolls_back_and_same_context_retries(bool afterSave)
    {
        var saves = new Saves(); using var factory = new RegistrationTestFactory(saves); await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var (g, q) = await Seed(db);
        var id = Guid.NewGuid(); saves.FailBefore = !afterSave; saves.FailAfter = afterSave;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(db, g, q, id)); saves.FailBefore = saves.FailAfter = false;
        Assert.Empty(db.ChangeTracker.Entries()); Assert.Empty(await db.QuizAttempts.ToArrayAsync()); Assert.Empty(await db.QuizAttemptAnswers.ToArrayAsync()); Assert.Empty(await db.QuizAttemptAnswerOptions.ToArrayAsync());
        Assert.Equal(QuizStatus.Success, (await Submit(db, g, q, id)).Status); Assert.Single(await db.QuizAttempts.ToArrayAsync());
        Assert.Equal(3, await db.QuizAttemptAnswers.CountAsync()); Assert.Equal(9, await db.QuizAttemptAnswerOptions.CountAsync());
    }

    [Theory]
    [InlineData("same")] [InlineData("conflict")] [InlineData("different")]
    public async Task Independent_submissions_serialize_same_and_distinct_ids(string kind)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var (g, q) = await Seed(db); var id = Guid.NewGuid();
        var (first, second) = await Contend(db.Database.GetConnectionString()!,
            d => Submit(d, g, q, id), d => Submit(d, g, q, kind == "different" ? Guid.NewGuid() : id,
                kind == "conflict" ? Answers(q, 3) : Answers(q).Reverse().ToArray(), now: Now.AddDays(1)));
        Assert.Equal(QuizStatus.Success, first.Status);
        Assert.Equal(kind == "conflict" ? QuizStatus.AttemptIdConflict : QuizStatus.Success, second.Status);
        if (kind == "same") Assert.Equal(JsonSerializer.Serialize(first.Attempt), JsonSerializer.Serialize(second.Attempt));
        Assert.Equal(kind == "different" ? 2 : 1, await db.QuizAttempts.CountAsync());
        Assert.Equal(kind == "different" ? 6 : 3, await db.QuizAttemptAnswers.CountAsync());
        Assert.Equal(kind == "different" ? 18 : 9, await db.QuizAttemptAnswerOptions.CountAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Edit_and_submit_both_lock_orders_preserve_revision_and_historical_definition(bool editFirst)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var (g, q) = await Seed(db); var id = Guid.NewGuid();
        Task<QuizStatus> Edit(TalynorDbContext d) => new QuizStore(d).ReplaceAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, q.Id, 1,
            new("new title", [new("new question", ["new a", "new b"], 1)]), Now.AddHours(1), default);
        QuizAttemptOperationResult result;
        if (editFirst)
        { var race = await Contend(db.Database.GetConnectionString()!, Edit, d => Submit(d, g, q, id)); Assert.Equal(QuizStatus.Success, race.First); result = race.Second; }
        else
        { var race = await Contend(db.Database.GetConnectionString()!, d => Submit(d, g, q, id), Edit); Assert.Equal(QuizStatus.Success, race.Second); result = race.First; }
        Assert.Equal(editFirst ? QuizStatus.ContentChanged : QuizStatus.Success, result.Status);
        Assert.Equal(editFirst ? 0 : 1, await db.QuizAttempts.CountAsync());
        if (!editFirst)
        { var historical = await new QuizAttemptReader(db).GetAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, q.Id, id, default);
            Assert.Equal("original title", historical!.QuizTitle); Assert.Equal(1, historical.ContentRevision); Assert.Equal(3, historical.Answers.Count); Assert.Equal(66.67m, historical.AccuracyPercentage); }
        Assert.Equal(2, (await db.Quizzes.AsNoTracking().SingleAsync()).ContentRevision);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Delete_and_submit_both_orders_leave_no_orphans(bool deleteFirst)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var (g, q) = await Seed(db); var id = Guid.NewGuid();
        if (deleteFirst)
        {
            var gate = new ReadGate();
            async Task<QuizStatus> Delete(TalynorDbContext d)
            {
                await d.Database.OpenConnectionAsync(); await using var tx = ((SqliteConnection)d.Database.GetDbConnection()).BeginTransaction(deferred: false);
                await using var enlist = await d.Database.UseTransactionAsync(tx);
                var status = await new QuizStore(d).DeleteAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, q.Id, default);
                gate.Entered.TrySetResult(); await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(15)); await tx.CommitAsync(); return status;
            }
            var race = await Contend(db.Database.GetConnectionString()!, Delete, d => Submit(d, g, q, id), gate, interceptRead: false);
            Assert.Equal(QuizStatus.Success, race.First); Assert.Equal(QuizStatus.NotFound, race.Second.Status);
        }
        else
        {
            var race = await Contend(db.Database.GetConnectionString()!, d => Submit(d, g, q, id),
                d => new QuizStore(d).DeleteAsync(g.Workspace.Id, g.Map.Id, g.Second.Id, q.Id, default));
            Assert.Equal(QuizStatus.Success, race.First.Status); Assert.Equal(QuizStatus.Success, race.Second);
        }
        Assert.Empty(await db.Quizzes.ToArrayAsync()); Assert.Empty(await db.QuizAttempts.ToArrayAsync());
        Assert.Empty(await db.QuizAttemptAnswers.ToArrayAsync()); Assert.Empty(await db.QuizAttemptAnswerOptions.ToArrayAsync());
        Assert.Equal(QuizStatus.NotFound, (await Submit(db, g, q, id)).Status);
    }

    [Theory]
    [InlineData(false, "none")] [InlineData(true, "none")]
    [InlineData(false, "active")] [InlineData(true, "active")]
    [InlineData(false, "completed")] [InlineData(true, "completed")]
    [InlineData(false, "edge")] [InlineData(true, "child")]
    public async Task Node_map_cascades_preserve_existing_blockers_and_never_partially_remove_history(bool map, string blocker)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var g = await KnowledgeGraphFixture.CreateAsync(db);
        if (blocker != "edge") await db.KnowledgeEdges.ExecuteDeleteAsync();
        var targetMap = blocker == "child" ? g.Child : g.Map; var targetNode = blocker == "child" ? g.ChildNode : g.Second;
        var q = NewQuiz(targetNode.Id); db.Quizzes.Add(q); db.QuizAttempts.Add(QuizAttempt.Grade(Guid.NewGuid(), q, 1, Answers(q), Now));
        if (blocker is "active" or "completed") { var study = StudySession.Start(targetNode.Id, Now); if (blocker == "completed") Assert.True(study.TryComplete(Now.AddMinutes(1))); db.StudySessions.Add(study); }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); var graph = scope.ServiceProvider.GetRequiredService<IKnowledgeMapStore>();
        if (map) graph.RemoveMap((await graph.GetMapAsync(targetMap.Id, g.Workspace.Id, default))!);
        else graph.RemoveNode((await graph.GetNodeAsync(targetNode.Id, targetMap.Id, g.Workspace.Id, default))!);
        var status = await graph.SaveAsync(new(map ? KnowledgeWriteOperation.DeleteMap : KnowledgeWriteOperation.DeleteNode,
            g.Workspace.Id, targetMap.Id, map ? targetMap.Id : targetNode.Id), default);
        Assert.Equal(blocker == "none" ? KnowledgeWriteStatus.Success : map ? KnowledgeWriteStatus.MapInUse : KnowledgeWriteStatus.NodeInUse, status);
        Assert.Equal(blocker == "none" ? 0 : 1, await db.QuizAttempts.CountAsync()); Assert.Equal(blocker == "none" ? 0 : 3, await db.QuizAttemptAnswers.CountAsync());
        Assert.Equal(blocker == "none" ? 0 : 9, await db.QuizAttemptAnswerOptions.CountAsync());
    }

    [Fact]
    public async Task Migration_down_to_add_quizzes_and_reapply_preserves_prior_schema_data_and_triggers()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); var (g, q) = await Seed(db);
        var card = Talynor.Domain.Flashcards.Flashcard.Create(g.Second.Id, "old front", "old back", Now);
        var note = Talynor.Domain.KnowledgeContents.KnowledgeContent.Create(g.Second.Id, Talynor.Domain.KnowledgeContents.KnowledgeContentType.Note, "old note", "old body", null, Now);
        var session = StudySession.Start(g.Second.Id, Now); db.Flashcards.Add(card); db.KnowledgeContents.Add(note); db.StudySessions.Add(session); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await Submit(db, g, q, Guid.NewGuid()); Assert.EndsWith("_AddQuizAttempts", (await db.Database.GetAppliedMigrationsAsync()).Last());
        async Task<string[]> Schema() => await db.Database.SqlQueryRaw<string>("SELECT sql AS Value FROM sqlite_master WHERE type IN ('index','trigger') AND tbl_name NOT IN ('QuizAttempts','QuizAttemptAnswers','QuizAttemptAnswerOptions') AND sql IS NOT NULL ORDER BY name").ToArrayAsync();
        var before = await Schema(); await db.GetService<IMigrator>().MigrateAsync("20261021175059_AddQuizzes");
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("SELECT AttemptId FROM QuizAttempts")); Assert.Equal(before, await Schema());
        await db.Database.MigrateAsync(); Assert.Equal(before, await Schema()); Assert.Empty(await db.QuizAttempts.ToArrayAsync());
        Assert.Equal("original title", (await db.Quizzes.AsNoTracking().SingleAsync()).Title); Assert.Equal(3, await db.QuizQuestions.CountAsync()); Assert.Equal(9, await db.QuizOptions.CountAsync());
        Assert.Equal("old body", (await db.KnowledgeContents.AsNoTracking().SingleAsync()).Body); Assert.Equal("old front", (await db.Flashcards.AsNoTracking().SingleAsync()).Front);
        Assert.Equal(Now, (await db.StudySessions.AsNoTracking().SingleAsync()).StartedAtUtc);
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE KnowledgeNodes SET ChildMapId={g.OtherMap.Id} WHERE Id={g.First.Id}"));
        Assert.Equal(QuizStatus.Success, (await Submit(db, g, q, Guid.NewGuid())).Status);
        Assert.False(db.Database.HasPendingModelChanges()); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal("ok", Assert.Single(await db.Database.SqlQueryRaw<string>("PRAGMA integrity_check").ToArrayAsync()));
        await db.Database.OpenConnectionAsync(); await using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = "PRAGMA foreign_key_check";
        await using var reader = await command.ExecuteReaderAsync(); Assert.False(await reader.ReadAsync());
    }

    private static async Task<(TFirst First, TSecond Second)> Contend<TFirst, TSecond>(string connection,
        Func<TalynorDbContext, Task<TFirst>> firstAction, Func<TalynorDbContext, Task<TSecond>> secondAction,
        ReadGate? gate = null, bool interceptRead = true)
    {
        gate ??= new ReadGate(); var opened = new OpenGate();
        var options = new DbContextOptionsBuilder<TalynorDbContext>().UseSqlite(connection);
        if (interceptRead) options.AddInterceptors(gate);
        await using var firstDb = new TalynorDbContext(options.Options);
        await using var secondDb = new TalynorDbContext(new DbContextOptionsBuilder<TalynorDbContext>().UseSqlite(connection).AddInterceptors(opened).Options);
        var first = Task.Run(() => firstAction(firstDb)); Task<TSecond>? second = null;
        try { await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); second = Task.Run(() => secondAction(secondDb)); await opened.Opened.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { gate.Release.TrySetResult(); }
        var result = (await first.WaitAsync(TimeSpan.FromSeconds(30)), await second!.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Empty(firstDb.ChangeTracker.Entries()); Assert.Empty(secondDb.ChangeTracker.Entries()); return result;
    }
    private sealed class ReadGate : DbCommandInterceptor
    {
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData e, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { if (command.CommandText.Contains("Quizzes")) { Entered.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), ct); } return result; }
    }
    private sealed class OpenGate : DbConnectionInterceptor
    {
        public TaskCompletionSource Opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData e, CancellationToken ct = default) { Opened.TrySetResult(); return Task.CompletedTask; }
    }
    private sealed class Saves : SaveChangesInterceptor
    {
        public int Count; public bool FailBefore, FailAfter;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
        { Count++; if (FailBefore) throw new InvalidOperationException("Synthetic before-save failure"); return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData e, int result, CancellationToken ct = default)
        { if (FailAfter) throw new InvalidOperationException("Synthetic pre-commit failure"); return ValueTask.FromResult(result); }
    }
    private sealed class Commands : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData e, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Sql.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}

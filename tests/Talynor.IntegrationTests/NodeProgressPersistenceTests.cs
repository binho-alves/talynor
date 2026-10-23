using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Talynor.Application.Flashcards;
using Talynor.Application.KnowledgeMaps;
using Talynor.Application.Progress;
using Talynor.Application.Quizzes;
using Talynor.Domain.Flashcards;
using Talynor.Domain.StudySessions;
using Talynor.Infrastructure.Flashcards;
using Talynor.Infrastructure.KnowledgeMaps;
using Talynor.Infrastructure.Progress;
using Talynor.Infrastructure.Quizzes;
using Talynor.Infrastructure.StudySessions;
using Xunit.Abstractions;

namespace Talynor.IntegrationTests;

public class NodeProgressPersistenceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Owned_empty_node_returns_zeros_and_nulls_with_no_tracking_or_writes()
    {
        await using var f = await NodeProgressFixture.CreateAsync(); await using var db = f.Context();
        var result = (await f.Read(db))!;
        Assert.Equal(f.Period, result.Period); Assert.Equal(f.Period.ToUtc, result.CalculatedAtUtc);
        Assert.Equal(new StudyProgress(0, 0), result.Study); Assert.Equal(new FlashcardProgress(0, 0, null), result.Flashcards);
        Assert.Equal(new QuizProgress(0, 0, 0, null), result.Quizzes);
        Assert.Empty(db.ChangeTracker.Entries()); Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData("foreign-workspace")] [InlineData("foreign-map")] [InlineData("foreign-node")]
    [InlineData("mismatch")] [InlineData("missing-node")] [InlineData("missing-map")]
    public async Task SQL_parent_scope_rejects_foreign_missing_and_mixed_paths(string path)
    {
        await using var f = await NodeProgressFixture.CreateAsync();
        await using (var seed = f.Context()) await NodeProgressFixture.AddActivity(seed, f.Foreign.Id, NodeProgressFixture.Now.AddSeconds(-1));
        var probe = new Commands(); await using var db = f.Context(probe);
        var workspace = path == "foreign-workspace" ? f.Graph.OtherWorkspace.Id : f.Graph.Workspace.Id;
        var map = path == "foreign-map" ? f.Graph.OtherMap.Id : path == "missing-map" ? Guid.NewGuid() : f.Graph.Map.Id;
        var node = path is "foreign-node" or "foreign-map" ? f.Foreign.Id : path == "mismatch" ? f.Graph.ChildNode.Id :
            path == "missing-node" ? Guid.NewGuid() : f.Graph.First.Id;
        Assert.Null(await new NodeProgressReader(db).ReadAsync(workspace, map, node, f.Period, default));
        Assert.Single(probe.Items); Assert.Contains("WorkspaceId", probe.Items[0].Sql);
        Assert.Null(db.Database.CurrentTransaction); Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Direct_node_only_excludes_foreign_same_map_sibling_and_child_map_even_with_cycles()
    {
        await using var f = await NodeProgressFixture.CreateAsync(); await using var db = f.Context();
        await NodeProgressFixture.AddActivity(db, f.Graph.First.Id, NodeProgressFixture.Now.AddSeconds(-1));
        await NodeProgressFixture.AddActivity(db, f.Graph.Second.Id, NodeProgressFixture.Now.AddSeconds(-1));
        await NodeProgressFixture.AddActivity(db, f.Graph.ChildNode.Id, NodeProgressFixture.Now.AddSeconds(-1));
        await NodeProgressFixture.AddActivity(db, f.Foreign.Id, NodeProgressFixture.Now.AddSeconds(-1));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE KnowledgeNodes SET ChildMapId = {f.Graph.Map.Id} WHERE Id = {f.Graph.ChildNode.Id}");
        db.ChangeTracker.Clear(); var result = (await f.Read(db))!;
        Assert.Equal(new StudyProgress(1, 1), result.Study); Assert.Equal(new FlashcardProgress(1, 1, 100), result.Flashcards);
        Assert.Equal(new QuizProgress(1, 1, 1, 100), result.Quizzes);
    }

    [Theory]
    [InlineData(-1, 0)] [InlineData(0, 1)] [InlineData(1, 1)]
    [InlineData(30L * TimeSpan.TicksPerDay - 1, 1)] [InlineData(30L * TimeSpan.TicksPerDay, 0)]
    public async Task All_sources_share_inclusive_from_exclusive_to_with_tick_precision(long ticksFromStart, long expected)
    {
        await using var f = await NodeProgressFixture.CreateAsync(); await using var db = f.Context();
        await NodeProgressFixture.AddActivity(db, f.Graph.First.Id, f.Period.FromUtc.AddTicks(ticksFromStart));
        db.ChangeTracker.Clear(); var result = (await f.Read(db))!;
        Assert.Equal(expected, result.Study.CompletedSessionCount); Assert.Equal(expected, result.Study.TotalDurationSeconds);
        Assert.Equal(expected, result.Flashcards.ReviewCount); Assert.Equal(expected, result.Quizzes.AttemptCount);
    }

    [Fact]
    public async Task Study_uses_full_end_attribution_zero_duration_and_aggregate_only_floor_excluding_active()
    {
        await using var f = await NodeProgressFixture.CreateAsync(); await using var db = f.Context();
        var end = f.Period.FromUtc.AddHours(1); var node = f.Graph.First.Id;
        db.StudySessions.AddRange(NodeProgressFixture.Session(node, f.Period.FromUtc.AddHours(-1), end),
            NodeProgressFixture.Session(node, end, end), NodeProgressFixture.Session(node, end.AddTicks(-6_000_000), end),
            NodeProgressFixture.Session(node, end.AddTicks(-6_000_000), end), StudySession.Start(node, end));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal(new StudyProgress(4, 7201), (await f.Read(db))!.Study);
    }

    [Fact]
    public async Task Review_history_and_weighted_attempt_counts_are_independent_of_current_state_and_text()
    {
        await using var f = await NodeProgressFixture.CreateAsync(); await using var db = f.Context();
        var node = f.Graph.First.Id; var end = NodeProgressFixture.Now.AddMinutes(-1);
        var card = Flashcard.Create(node, "Front", "Back", end); var q1 = NodeProgressFixture.QuizFor(node);
        var q10 = NodeProgressFixture.QuizFor(node, 10); db.AddRange(card, q1, q10);
        db.FlashcardReviews.AddRange(FlashcardReview.Create(Guid.NewGuid(), card.Id, 1, FlashcardAnswerResult.Correct, end),
            FlashcardReview.Create(Guid.NewGuid(), card.Id, 1, FlashcardAnswerResult.Incorrect, end),
            FlashcardReview.Create(Guid.NewGuid(), card.Id, 1, FlashcardAnswerResult.Correct, end));
        db.QuizAttempts.AddRange(NodeProgressFixture.Attempt(q1, 1, end), NodeProgressFixture.Attempt(q10, 5, end));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Empty(await db.FlashcardReviewStates.ToArrayAsync());
        var result = (await f.Read(db))!;
        Assert.Equal(new FlashcardProgress(3, 2, 66.67m), result.Flashcards);
        Assert.Equal(new QuizProgress(2, 11, 6, 54.55m), result.Quizzes);
    }

    [Fact]
    public async Task Official_replay_edit_reset_and_delete_preserve_historical_meaning_without_double_counting()
    {
        await using var f = await NodeProgressFixture.CreateAsync(); await using var db = f.Context();
        var node = f.Graph.First.Id; var w = f.Graph.Workspace.Id; var map = f.Graph.Map.Id; var at = NodeProgressFixture.Now.AddMinutes(-1);
        var card = Flashcard.Create(node, "Front", "Back", at); var quiz = NodeProgressFixture.QuizFor(node);
        db.AddRange(card, quiz); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var reviewId = Guid.NewGuid(); var attemptId = Guid.NewGuid(); var answers = NodeProgressFixture.Answers(quiz, 1);
        var study = new FlashcardStudyStore(db, new Clock(at)); var attempts = new QuizAttemptStore(db);
        Assert.Equal(FlashcardStatus.Success, (await study.ReviewAsync(w, map, node, card.Id, reviewId, 1, FlashcardAnswerResult.Correct, default)).Status);
        Assert.Equal(QuizStatus.Success, (await attempts.SubmitAsync(w, map, node, quiz.Id, attemptId, 1, answers, at, default)).Status);
        Assert.Equal(FlashcardStatus.Success, await new FlashcardStore(db).UpdateAsync(w, map, node, card.Id, new("Edited", FrontSpecified: true), at, default));
        Assert.Equal(QuizStatus.Success, await new QuizStore(db).ReplaceAsync(w, map, node, quiz.Id, 1, new("Edited quiz", NodeProgressFixture.Definition(3)), at, default));
        Assert.Equal(FlashcardStatus.Success, (await study.ReviewAsync(w, map, node, card.Id, reviewId, 1, FlashcardAnswerResult.Correct, default)).Status);
        Assert.Equal(QuizStatus.Success, (await attempts.SubmitAsync(w, map, node, quiz.Id, attemptId, 1, answers, at, default)).Status);
        Assert.Equal(new FlashcardProgress(1, 1, 100), (await f.Read(db))!.Flashcards);
        Assert.Equal(new QuizProgress(1, 1, 1, 100), (await f.Read(db))!.Quizzes);
        Assert.Empty(await db.FlashcardReviewStates.ToArrayAsync());
        Assert.Equal(FlashcardStatus.Success, (await study.ReviewAsync(w, map, node, card.Id, Guid.NewGuid(), 2, FlashcardAnswerResult.Incorrect, default)).Status);
        var editedQuiz = await db.Quizzes.AsNoTracking().Include(q => q.Questions).ThenInclude(q => q.Options).SingleAsync();
        Assert.Equal(QuizStatus.Success, (await attempts.SubmitAsync(w, map, node, quiz.Id, Guid.NewGuid(), 2,
            NodeProgressFixture.Answers(editedQuiz, 0), at, default)).Status);
        Assert.Equal(new FlashcardProgress(2, 1, 50), (await f.Read(db))!.Flashcards);
        Assert.Equal(new QuizProgress(2, 4, 1, 25), (await f.Read(db))!.Quizzes);
        Assert.Equal(FlashcardStatus.Success, await new FlashcardStore(db).DeleteAsync(w, map, node, card.Id, default));
        Assert.Equal(QuizStatus.Success, await new QuizStore(db).DeleteAsync(w, map, node, quiz.Id, default));
        Assert.Equal(new FlashcardProgress(0, 0, null), (await f.Read(db))!.Flashcards);
        Assert.Equal(new QuizProgress(0, 0, 0, null), (await f.Read(db))!.Quizzes);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Existing_study_blockers_and_allowed_node_map_cascades_remain_authoritative(bool mapDelete)
    {
        await using var f = await NodeProgressFixture.CreateAsync(); await using var db = f.Context();
        await NodeProgressFixture.AddActivity(db, f.Graph.First.Id, NodeProgressFixture.Now.AddSeconds(-1));
        await db.KnowledgeEdges.ExecuteDeleteAsync(); db.ChangeTracker.Clear();
        var w = f.Graph.Workspace.Id; var map = f.Graph.Map.Id; var node = f.Graph.First.Id; var store = new KnowledgeMapStore(db);
        Assert.True(mapDelete ? await store.MapInUseAsync(map, w, default) : await store.NodeInUseAsync(node, map, w, default));
        if (mapDelete) store.RemoveMap((await store.GetMapAsync(map, w, default))!);
        else store.RemoveNode((await store.GetNodeAsync(node, map, w, default))!);
        var operation = new KnowledgeWriteAttempt(mapDelete ? KnowledgeWriteOperation.DeleteMap : KnowledgeWriteOperation.DeleteNode, w, map, mapDelete ? map : node);
        Assert.Equal(mapDelete ? KnowledgeWriteStatus.MapInUse : KnowledgeWriteStatus.NodeInUse, await store.SaveAsync(operation, default));
        Assert.Equal(1, (await f.Read(db))!.Study.CompletedSessionCount);
        db.ChangeTracker.Clear(); var id = await db.StudySessions.Select(s => s.Id).SingleAsync();
        Assert.Equal(Talynor.Application.StudySessions.StudySessionWriteStatus.Success, await new StudySessionStore(db).DeleteAsync(w, map, node, id, default));
        Assert.Equal(new StudyProgress(0, 0), (await f.Read(db))!.Study);
        if (mapDelete) store.RemoveMap((await store.GetMapAsync(map, w, default))!);
        else store.RemoveNode((await store.GetNodeAsync(node, map, w, default))!);
        Assert.Equal(KnowledgeWriteStatus.Success, await store.SaveAsync(operation, default));
        Assert.Null(await f.Read(db)); Assert.Empty(await db.FlashcardReviews.ToArrayAsync()); Assert.Empty(await db.QuizAttempts.ToArrayAsync());
    }

    [Theory]
    [InlineData("study")] [InlineData("review")] [InlineData("quiz")]
    public async Task Corrupt_persisted_source_fails_instead_of_fabricating_a_summary(string source)
    {
        await using var f = await NodeProgressFixture.CreateAsync(); await using var db = f.Context();
        await NodeProgressFixture.AddActivity(db, f.Graph.First.Id, NodeProgressFixture.Now.AddMinutes(-1));
        db.ChangeTracker.Clear(); await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints=ON");
        await db.Database.ExecuteSqlRawAsync(source switch
        {
            "study" => "UPDATE StudySessions SET StartedAtUtc = datetime(EndedAtUtc, '+1 day')",
            "review" => "UPDATE FlashcardReviews SET Result = 'invalid'",
            _ => "UPDATE QuizAttempts SET CorrectCount = -1"
        });
        await db.Database.CloseConnectionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Read(db)); Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Failure_and_cancellation_dispose_snapshot_without_retry_and_allow_reuse(bool cancel)
    {
        await using var f = await NodeProgressFixture.CreateAsync(); using var cancellation = new CancellationTokenSource();
        var probe = new Commands { FailAt = 2, Cancellation = cancel ? cancellation : null }; await using var db = f.Context(probe);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new NodeProgressReader(db).ReadAsync(f.Graph.Workspace.Id,
            f.Graph.Map.Id, f.Graph.First.Id, f.Period, cancellation.Token));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => f.Read(db));
        Assert.Equal(2, probe.Items.Count); Assert.Null(db.Database.CurrentTransaction);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
        probe.FailAt = 0; Assert.NotNull(await f.Read(db));
    }

    [Fact]
    public async Task SQL_is_scoped_scalar_aggregated_bounded_and_uses_existing_parent_history_indexes()
    {
        await using var f = await NodeProgressFixture.CreateAsync(); var probe = new Commands();
        await using var db = f.Context(probe); Assert.NotNull(await f.Read(db)); var first = probe.Items.ToArray(); probe.Items.Clear();
        await NodeProgressFixture.AddActivity(db, f.Graph.First.Id, NodeProgressFixture.Now.AddSeconds(-1));
        for (var i = 0; i < 12; i++) await NodeProgressFixture.AddActivity(db, f.Graph.First.Id, NodeProgressFixture.Now.AddSeconds(-i - 1));
        db.ChangeTracker.Clear(); probe.Items.Clear(); var summary = (await f.Read(db))!;
        Assert.Equal(13, summary.Study.CompletedSessionCount); Assert.Equal(13, summary.Flashcards.ReviewCount); Assert.Equal(13, summary.Quizzes.AttemptCount);
        Assert.Equal(4, first.Length); Assert.Equal(4, probe.Items.Count);
        Assert.All(probe.Items, item => Assert.Same(probe.Items[0].Transaction, item.Transaction)); Assert.NotNull(probe.Items[0].Transaction);
        Assert.All(probe.Items, item =>
        {
            Assert.Contains("WorkspaceId", item.Sql); Assert.Contains("KnowledgeMapId", item.Sql);
            foreach (var privateColumn in new[] { "\"Front\"", "\"Back\"", "\"Text\"", "\"QuizTitle\"", "FlashcardReviewStates", "QuizAttemptAnswers" })
                Assert.DoesNotContain(privateColumn, item.Sql);
        });
        Assert.Contains("COUNT", probe.Items[2].Sql); Assert.Contains("SUM", probe.Items[3].Sql);
        Assert.DoesNotContain("FlashcardReviews", probe.Items[1].Sql); Assert.DoesNotContain("QuizAttempts", probe.Items[2].Sql);
        Assert.Empty(db.ChangeTracker.Entries());
        var queries = probe.Items.ToArray(); var plans = new List<string>(); await db.Database.OpenConnectionAsync();
        foreach (var query in queries)
        {
            using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = "EXPLAIN QUERY PLAN " + query.Sql;
            foreach (var (name, value) in query.Parameters) { var p = command.CreateParameter(); p.ParameterName = name; p.Value = value; command.Parameters.Add(p); }
            using var rows = await command.ExecuteReaderAsync(); while (await rows.ReadAsync()) plans.Add(rows.GetString(3));
        }
        await db.Database.CloseConnectionAsync();
        foreach (var index in new[] { "IX_StudySessions_KnowledgeNodeId_StartedAtUtc_Id",
            "IX_FlashcardReviews_FlashcardId_ReviewedAtUtc_ReviewId", "IX_QuizAttempts_QuizId_SubmittedAtUtc_AttemptId" })
            Assert.True(plans.Any(p => p.Contains(index, StringComparison.Ordinal)), string.Join(Environment.NewLine, plans));
        // Synthetic fixture evidence: SQL/plan shape only, no parameter values or activity totals.
        foreach (var query in queries) output.WriteLine(query.Sql);
        foreach (var plan in plans) output.WriteLine(plan);
    }

    internal sealed class Commands : DbCommandInterceptor
    {
        public List<(string Sql, DbTransaction? Transaction, (string Name, object Value)[] Parameters)> Items = [];
        public int FailAt; public CancellationTokenSource? Cancellation;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            Items.Add((command.CommandText, command.Transaction, command.Parameters.Cast<DbParameter>().Select(p => (p.ParameterName, p.Value!)).ToArray()));
            if (Items.Count == FailAt) { if (Cancellation is null) throw new InvalidOperationException("Synthetic read failure."); Cancellation.Cancel(); ct.ThrowIfCancellationRequested(); }
            return ValueTask.FromResult(result);
        }
    }
    internal sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}

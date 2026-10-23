using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Talynor.Application.Flashcards;
using Talynor.Application.Progress;
using Talynor.Application.Quizzes;
using Talynor.Application.StudySessions;
using Talynor.Domain.Flashcards;
using Talynor.Domain.StudySessions;
using Talynor.Infrastructure.Flashcards;
using Talynor.Infrastructure.Persistence;
using Talynor.Infrastructure.Progress;
using Talynor.Infrastructure.Quizzes;
using Talynor.Infrastructure.StudySessions;

namespace Talynor.IntegrationTests;

public class NodeProgressSnapshotTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task Actual_default_WAL_snapshot_cannot_mix_metrics_despite_interleaved_official_commits()
    {
        await using var f = await NodeProgressFixture.CreateAsync(); var (session, card, quiz) = await Seed(f);
        using var readerConnection = new ConnectionProbe(f.ConnectionString);
        var hold = new BeforeReviewRead(); await using var readerDb = new TalynorDbContext(new DbContextOptionsBuilder<TalynorDbContext>()
            .UseSqlite(readerConnection).AddInterceptors(hold).Options);
        // Observe, rather than assume, the real fresh EF/SQLite database journal mode.
        Assert.Equal("wal", await Journal(f));
        var reading = Task.Run(() => f.Read(readerDb));
        try
        {
            await hold.Entered.Task.WaitAsync(Timeout);
            await Task.Run(async () =>
            {
                await using var writer = f.Context(); var at = NodeProgressFixture.Now.AddSeconds(-1);
                Assert.Equal(StudySessionWriteStatus.Success, await new StudySessionStore(writer).CompleteAsync(
                    f.Graph.Workspace.Id, f.Graph.Map.Id, f.Graph.First.Id, session.Id, at, default));
                Assert.Equal(FlashcardStatus.Success, (await new FlashcardStudyStore(writer, new NodeProgressPersistenceTests.Clock(at)).ReviewAsync(
                    f.Graph.Workspace.Id, f.Graph.Map.Id, f.Graph.First.Id, card.Id, Guid.NewGuid(), 1, FlashcardAnswerResult.Correct, default)).Status);
                Assert.Equal(QuizStatus.Success, (await new QuizAttemptStore(writer).SubmitAsync(f.Graph.Workspace.Id, f.Graph.Map.Id,
                    f.Graph.First.Id, quiz.Id, Guid.NewGuid(), 1, NodeProgressFixture.Answers(quiz, 1), at, default)).Status);
            }).WaitAsync(Timeout);
            Assert.False(reading.IsCompleted); // Commits completed after study was read, before either other source was read.
        }
        finally { hold.Release.TrySetResult(); }
        var before = (await reading.WaitAsync(Timeout))!;
        Assert.Equal(new StudyProgress(0, 0), before.Study); Assert.Equal(new FlashcardProgress(0, 0, null), before.Flashcards);
        Assert.Equal(new QuizProgress(0, 0, 0, null), before.Quizzes);
        Assert.True(readerConnection.Deferred); Assert.Equal(1, readerConnection.Begins);
        Assert.Null(readerDb.Database.CurrentTransaction); Assert.Equal(System.Data.ConnectionState.Closed, readerConnection.State);
        await using var nextDb = f.Context(); var after = (await f.Read(nextDb))!;
        Assert.Equal(new StudyProgress(1, 1), after.Study); Assert.Equal(new FlashcardProgress(1, 1, 100), after.Flashcards);
        Assert.Equal(new QuizProgress(1, 1, 1, 100), after.Quizzes);
    }

    [Fact]
    public async Task Rollback_journal_reader_remains_coherent_and_releases_the_concurrent_writer_commit()
    {
        await using var f = await NodeProgressFixture.CreateAsync(); var (session, _, _) = await Seed(f);
        Assert.Equal("delete", await Journal(f, "DELETE"));
        var hold = new BeforeReviewRead(); var committing = new CommitSignal();
        await using var reader = f.Context(hold); var reading = Task.Run(() => f.Read(reader));
        Task? writing = null;
        try
        {
            await hold.Entered.Task.WaitAsync(Timeout);
            writing = Task.Run(async () =>
            {
                await using var writer = f.Context(committing); await using var transaction = await writer.Database.BeginTransactionAsync();
                Assert.Equal(StudySessionWriteStatus.Success, await new StudySessionStore(writer).CompleteAsync(f.Graph.Workspace.Id,
                    f.Graph.Map.Id, f.Graph.First.Id, session.Id, NodeProgressFixture.Now.AddSeconds(-1), default));
                await transaction.CommitAsync();
            });
            await committing.Entered.Task.WaitAsync(Timeout); Assert.False(writing.IsCompleted);
        }
        finally { hold.Release.TrySetResult(); }
        Assert.Equal(new StudyProgress(0, 0), (await reading.WaitAsync(Timeout))!.Study);
        await writing!.WaitAsync(Timeout);
        await using var next = f.Context(); Assert.Equal(new StudyProgress(1, 1), (await f.Read(next))!.Study);
    }

    [Fact]
    public async Task Uncommitted_and_rolled_back_sources_never_contribute()
    {
        await using var f = await NodeProgressFixture.CreateAsync(); await using var writer = f.Context();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await NodeProgressFixture.AddActivity(writer, f.Graph.First.Id, NodeProgressFixture.Now.AddSeconds(-1));
        await using var reader = f.Context(); var before = (await f.Read(reader))!;
        Assert.Equal(0, before.Study.CompletedSessionCount); Assert.Equal(0, before.Flashcards.ReviewCount); Assert.Equal(0, before.Quizzes.AttemptCount);
        await transaction.RollbackAsync(); var after = (await f.Read(reader))!;
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Native_busy_read_propagates_as_failure_without_empty_result_or_application_retry()
    {
        await using var f = await NodeProgressFixture.CreateAsync(); Assert.Equal("delete", await Journal(f, "DELETE"));
        using var blocker = new SqliteConnection(f.ConnectionString); await blocker.OpenAsync();
        using var command = blocker.CreateCommand(); command.CommandText = "BEGIN EXCLUSIVE"; await command.ExecuteNonQueryAsync();
        try
        {
            var probe = new NodeProgressPersistenceTests.Commands(); await using var db = f.Context(probe);
            var error = await Assert.ThrowsAsync<SqliteException>(() => Task.Run(() => f.Read(db)).WaitAsync(Timeout));
            Assert.Contains(error.SqliteErrorCode, new[] { 5, 6 }); Assert.Single(probe.Items);
            Assert.Null(db.Database.CurrentTransaction); Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
        }
        finally { command.CommandText = "ROLLBACK"; await command.ExecuteNonQueryAsync(); }
    }

    private static async Task<(StudySession, Flashcard, Talynor.Domain.Quizzes.Quiz)> Seed(NodeProgressFixture f)
    {
        await using var db = f.Context(); var at = NodeProgressFixture.Now.AddSeconds(-2);
        var session = StudySession.Start(f.Graph.First.Id, at);
        var card = Flashcard.Create(f.Graph.First.Id, "front", "back", at); var quiz = NodeProgressFixture.QuizFor(f.Graph.First.Id);
        db.AddRange(session, card, quiz); await db.SaveChangesAsync(); return (session, card, quiz);
    }
    private static async Task<string> Journal(NodeProgressFixture f, string? mode = null)
    {
        using var connection = new SqliteConnection(f.ConnectionString); await connection.OpenAsync();
        using var command = connection.CreateCommand(); command.CommandText = mode is null ? "PRAGMA journal_mode" : $"PRAGMA journal_mode={mode}";
        return (string)(await command.ExecuteScalarAsync())!;
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class BeforeReviewRead : DbCommandInterceptor
    {
        public TaskCompletionSource Entered = Signal(), Release = Signal();
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("FlashcardReviews", StringComparison.Ordinal))
            { Assert.NotNull(command.Transaction); Entered.TrySetResult(); await Release.Task.WaitAsync(Timeout, ct); }
            return result;
        }
    }
    private sealed class CommitSignal : DbTransactionInterceptor
    {
        public TaskCompletionSource Entered = Signal();
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData data,
            InterceptionResult result, CancellationToken ct = default)
        { Entered.TrySetResult(); return ValueTask.FromResult(result); }
    }
    private sealed class ConnectionProbe(string cs) : SqliteConnection(cs)
    {
        public bool Deferred; public int Begins;
        public override SqliteTransaction BeginTransaction(bool deferred)
        { Deferred = deferred; Begins++; return base.BeginTransaction(deferred); }
    }
}

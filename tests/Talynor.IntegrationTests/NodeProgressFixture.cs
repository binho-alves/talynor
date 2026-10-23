using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Talynor.Application.Progress;
using Talynor.Domain.Flashcards;
using Talynor.Domain.KnowledgeMaps;
using Talynor.Domain.Quizzes;
using Talynor.Domain.StudySessions;
using Talynor.Infrastructure.Persistence;
using Talynor.Infrastructure.Progress;

namespace Talynor.IntegrationTests;

// Migrated disposable SQLite only; no host, HTTP route, Development data or runtime seed.
internal sealed class NodeProgressFixture : IAsyncDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"talynor-progress-{Guid.NewGuid():N}");
    public static readonly DateTimeOffset Now = new(2026, 11, 5, 15, 0, 0, TimeSpan.Zero);
    public NodeProgressPeriod Period { get; } = NodeProgressPeriod.Create(30, Now);
    public KnowledgeGraphFixture Graph { get; private set; } = null!;
    public KnowledgeNode Foreign { get; private set; } = null!;
    public string ConnectionString => new SqliteConnectionStringBuilder
        { DataSource = Path.Combine(directory, "fixture.sqlite"), Pooling = false, DefaultTimeout = 2 }.ConnectionString;

    public static async Task<NodeProgressFixture> CreateAsync()
    {
        var fixture = new NodeProgressFixture(); Directory.CreateDirectory(fixture.directory);
        try
        {
            await using var db = fixture.Context(); await db.Database.MigrateAsync();
            fixture.Graph = await KnowledgeGraphFixture.CreateAsync(db);
            fixture.Foreign = KnowledgeNode.Create(fixture.Graph.OtherMap.Id, "Foreign", null, 0, 0, null, Now);
            db.KnowledgeNodes.Add(fixture.Foreign); await db.SaveChangesAsync();
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    public TalynorDbContext Context(params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<TalynorDbContext>().UseSqlite(ConnectionString).AddInterceptors(interceptors).Options);
    public Task<NodeProgressSummary?> Read(TalynorDbContext db) => new NodeProgressReader(db)
        .ReadAsync(Graph.Workspace.Id, Graph.Map.Id, Graph.First.Id, Period, default);

    public static Quiz QuizFor(Guid nodeId, int questions = 1) => Quiz.Create(nodeId, "Private quiz text",
        Definition(questions), Now.AddDays(-60));
    public static QuizQuestionInput[] Definition(int count) => Enumerable.Range(1, count)
        .Select(i => new QuizQuestionInput($"Private question {i}", ["right", "wrong"], 0)).ToArray();
    public static QuizAnswerInput[] Answers(Quiz quiz, int correct) => quiz.Questions.Select((q, i) =>
        new QuizAnswerInput(q.Id, q.Options.Single(o => o.IsCorrect == (i < correct)).Id)).ToArray();
    public static QuizAttempt Attempt(Quiz quiz, int correct, DateTimeOffset at) =>
        QuizAttempt.Grade(Guid.NewGuid(), quiz, quiz.ContentRevision, Answers(quiz, correct), at);
    public static StudySession Session(Guid node, DateTimeOffset start, DateTimeOffset end) => new(Guid.NewGuid(), node, start, end);

    public static async Task<(Flashcard Card, Quiz Quiz)> AddActivity(TalynorDbContext db, Guid node, DateTimeOffset at)
    {
        var card = Flashcard.Create(node, "private front", "private back", Now.AddDays(-60)); var quiz = QuizFor(node);
        db.Flashcards.Add(card); db.Quizzes.Add(quiz);
        db.StudySessions.Add(Session(node, at.AddSeconds(-1), at));
        db.FlashcardReviews.Add(FlashcardReview.Create(Guid.NewGuid(), card.Id, 1, FlashcardAnswerResult.Correct, at));
        db.QuizAttempts.Add(Attempt(quiz, 1, at));
        await db.SaveChangesAsync(); return (card, quiz);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" }) File.Delete(Path.Combine(directory, "fixture.sqlite") + suffix);
        if (Directory.Exists(directory)) Directory.Delete(directory);
        return ValueTask.CompletedTask;
    }
}

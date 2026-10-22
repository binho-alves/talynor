using Talynor.Application.Accounts;
using Talynor.Application.Quizzes;
using Talynor.Application.Workspaces;
using Talynor.Domain.Quizzes;
using Talynor.Domain.Workspaces;

namespace Talynor.UnitTests;

public class QuizAttemptUseCaseTests
{
    [Theory]
    [InlineData(false, "owner")] [InlineData(true, null)] [InlineData(true, "other")]
    public async Task Missing_or_mismatched_account_never_accesses_attempt_storage(bool auth, string? account)
    {
        var f = new Fixture(); f.Current.Auth = auth; f.Accounts.Id = account;
        Assert.Equal(QuizStatus.AuthenticationRequired, (await f.Submit.ExecuteAsync(f.Map, f.Node, f.Quiz, f.Id, 1, f.Answers, default)).Status);
        Assert.Equal(QuizStatus.AuthenticationRequired, (await f.List.ExecuteAsync(f.Map, f.Node, f.Quiz, 1, 20, default)).Status);
        Assert.Equal(QuizStatus.AuthenticationRequired, (await f.Get.ExecuteAsync(f.Map, f.Node, f.Quiz, f.Id, default)).Status);
        Assert.Equal(0, f.Storage.Calls);
    }

    [Theory]
    [InlineData(0, 20)] [InlineData(-1, 20)] [InlineData(1, 0)] [InlineData(1, 101)] [InlineData(int.MaxValue, 100)]
    public async Task Invalid_pagination_never_reaches_reader(int page, int size)
    {
        var f = new Fixture(); Assert.Equal(QuizStatus.InvalidInput, (await f.List.ExecuteAsync(f.Map, f.Node, f.Quiz, page, size, default)).Status);
        Assert.Equal(0, f.Storage.Calls);
    }

    [Fact]
    public async Task Invalid_submission_never_reaches_writer()
    {
        var f = new Fixture();
        foreach (var (id, rev, answers) in new (Guid, int, QuizAnswerInput[])[] { (Guid.Empty, 1, f.Answers), (f.Id, 0, f.Answers),
            (f.Id, 1, []), (f.Id, 1, [f.Answers[0], f.Answers[0]]), (f.Id, 1, [new(Guid.Empty, Guid.NewGuid())]), (f.Id, 1, null!) })
            Assert.Equal(QuizStatus.InvalidInput, (await f.Submit.ExecuteAsync(f.Map, f.Node, f.Quiz, id, rev, answers, default)).Status);
        Assert.Equal(0, f.Storage.Calls);
    }

    [Theory]
    [InlineData(QuizStatus.Success)] [InlineData(QuizStatus.ContentChanged)] [InlineData(QuizStatus.AttemptIdConflict)]
    [InlineData(QuizStatus.NotFound)] [InlineData(QuizStatus.InvalidInput)]
    public async Task Submission_forwards_server_scope_clock_and_original_semantics(QuizStatus status)
    {
        var f = new Fixture(); f.Storage.Status = status;
        var result = await f.Submit.ExecuteAsync(f.Map, f.Node, f.Quiz, f.Id, 7, f.Answers, default);
        Assert.Equal(status, result.Status); Assert.Same(f.Storage.Result, result.Attempt);
        Assert.Equal((f.Workspace.Id, f.Map, f.Node, f.Quiz, f.Id), f.Storage.Scope); Assert.Equal(7, f.Storage.Revision);
        Assert.Same(f.Answers, f.Storage.Answers); Assert.Equal(f.Clock.GetUtcNow(), f.Storage.Now);
    }

    [Fact]
    public async Task Reads_preserve_history_and_lookup_results_and_parent_missing()
    {
        var f = new Fixture(); var page = await f.List.ExecuteAsync(f.Map, f.Node, f.Quiz, 3, 10, default);
        Assert.Equal((20, 10), f.Storage.Page); Assert.Equal(3, page.Collection!.Page); Assert.True(page.Collection.HasMore);
        Assert.Same(f.Storage.Summaries, page.Collection.Attempts);
        Assert.Same(f.Storage.Result, (await f.Get.ExecuteAsync(f.Map, f.Node, f.Quiz, f.Id, default)).Attempt);
        f.Storage.Exists = false;
        Assert.Equal(QuizStatus.NotFound, (await f.List.ExecuteAsync(f.Map, f.Node, f.Quiz, 1, 20, default)).Status);
        Assert.Equal(QuizStatus.NotFound, (await f.Get.ExecuteAsync(f.Map, f.Node, f.Quiz, f.Id, default)).Status);
    }

    [Fact]
    public async Task Cancellation_and_operational_failure_are_not_empty_history()
    {
        var f = new Fixture(); var ct = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.List.ExecuteAsync(f.Map, f.Node, f.Quiz, 1, 20, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Get.ExecuteAsync(f.Map, f.Node, f.Quiz, f.Id, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Submit.ExecuteAsync(f.Map, f.Node, f.Quiz, f.Id, 1, f.Answers, ct));
        Assert.Equal(0, f.Storage.Calls); f.Storage.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Get.ExecuteAsync(f.Map, f.Node, f.Quiz, f.Id, default));
    }

    private sealed class Fixture
    {
        public Guid Map = Guid.NewGuid(), Node = Guid.NewGuid(), Quiz = Guid.NewGuid(), Id = Guid.NewGuid();
        public QuizAnswerInput[] Answers = [new(Guid.NewGuid(), Guid.NewGuid())];
        public PersonalWorkspace Workspace = PersonalWorkspace.Create("owner", DateTimeOffset.UnixEpoch);
        public Current Current = new(); public Accounts Accounts = new(); public Clock Clock = new(); public Storage Storage = new();
        private GetOrCreatePersonalWorkspace Resolver => new(Current, Accounts, new WorkspaceStore(Workspace), Clock);
        public SubmitQuizAttempt Submit => new(Resolver, Storage, Clock); public ListQuizAttempts List => new(Resolver, Storage);
        public GetQuizAttempt Get => new(Resolver, Storage);
    }
    private sealed class Current : ICurrentUser { public bool Auth = true; public bool IsAuthenticated => Auth; public string? UserId => "owner"; }
    private sealed class Accounts : IIdentityAccountService
    {
        public string? Id = "owner";
        public Task<AccountSummary?> GetByIdAsync(string id, CancellationToken ct) => Task.FromResult(Id is null ? null : new AccountSummary(Id, "synthetic@example.invalid"));
        public Task<RegisterUserResult> CreateAsync(string email, string password, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class WorkspaceStore(PersonalWorkspace workspace) : IPersonalWorkspaceStore
    {
        public Task<PersonalWorkspace?> GetByOwnerUserIdAsync(string id, CancellationToken ct) => Task.FromResult<PersonalWorkspace?>(workspace);
        public Task<PersonalWorkspace> AddOrGetExistingAsync(PersonalWorkspace value, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddDays(1); }
    private sealed class Storage : IQuizAttemptReader, IQuizAttemptStore
    {
        public int Calls, Revision; public bool Exists = true, Fail; public QuizStatus Status = QuizStatus.Success;
        public (Guid Workspace, Guid Map, Guid Node, Guid Quiz, Guid Attempt) Scope; public (int Offset, int Size) Page;
        public DateTimeOffset Now; public IReadOnlyList<QuizAnswerInput>? Answers;
        public QuizAttemptDetail Result = new(Guid.NewGuid(), Guid.NewGuid(), "old", 1, DateTimeOffset.UnixEpoch, 3, 2, []);
        public QuizAttemptSummary[] Summaries = [new(Guid.NewGuid(), Guid.NewGuid(), "old", 1, DateTimeOffset.UnixEpoch, 3, 2)];
        private void Capture(Guid w, Guid m, Guid n, Guid q, Guid a = default)
        { Calls++; Scope = (w, m, n, q, a); if (Fail) throw new InvalidOperationException("Synthetic failure"); }
        public Task<QuizAttemptPage?> ListAsync(Guid w, Guid m, Guid n, Guid q, int offset, int size, CancellationToken ct)
        { Capture(w, m, n, q); Page = (offset, size); return Task.FromResult(Exists ? new QuizAttemptPage(Summaries, true) : null); }
        public Task<QuizAttemptDetail?> GetAsync(Guid w, Guid m, Guid n, Guid q, Guid a, CancellationToken ct)
        { Capture(w, m, n, q, a); return Task.FromResult(Exists ? Result : null); }
        public Task<QuizAttemptOperationResult> SubmitAsync(Guid w, Guid m, Guid n, Guid q, Guid a, int revision,
            IReadOnlyList<QuizAnswerInput> answers, DateTimeOffset now, CancellationToken ct)
        { Capture(w, m, n, q, a); Revision = revision; Answers = answers; Now = now; return Task.FromResult(new QuizAttemptOperationResult(Status, Result)); }
    }
}

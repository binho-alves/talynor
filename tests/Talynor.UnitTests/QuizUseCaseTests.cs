using Talynor.Application.Accounts;
using Talynor.Application.Quizzes;
using Talynor.Application.Workspaces;
using Talynor.Domain.Quizzes;
using Talynor.Domain.Workspaces;

namespace Talynor.UnitTests;

public class QuizUseCaseTests
{
    private static readonly QuizInput Input = new(" title ", [new("q", ["a", "b"], 1)]);

    [Theory]
    [InlineData(false, "owner")] [InlineData(true, null)] [InlineData(true, "different")]
    public async Task Missing_or_mismatched_account_never_reaches_storage(bool authenticated, string? account)
    {
        var f = new Fixture(); f.Current.Authenticated = authenticated; f.Accounts.Id = account;
        Assert.Equal(QuizStatus.AuthenticationRequired, (await f.List.ExecuteAsync(f.Map, f.Node, 1, 20, default)).Status);
        Assert.Equal(QuizStatus.AuthenticationRequired, (await f.Get.ExecuteAsync(f.Map, f.Node, f.Id, default)).Status);
        Assert.Equal(QuizStatus.AuthenticationRequired, (await f.Take.ExecuteAsync(f.Map, f.Node, f.Id, default)).Status);
        Assert.Equal(QuizStatus.AuthenticationRequired, (await f.Create.ExecuteAsync(f.Map, f.Node, Input, default)).Status);
        Assert.Equal(QuizStatus.AuthenticationRequired, await f.Replace.ExecuteAsync(f.Map, f.Node, f.Id, 1, Input, default));
        Assert.Equal(QuizStatus.AuthenticationRequired, await f.Delete.ExecuteAsync(f.Map, f.Node, f.Id, default));
        Assert.Equal(0, f.Storage.Calls);
    }

    [Theory]
    [InlineData(0, 20)] [InlineData(-1, 20)] [InlineData(1, 0)] [InlineData(1, -1)]
    [InlineData(1, 101)] [InlineData(int.MaxValue, 100)]
    public async Task Invalid_or_overflowing_pagination_does_not_call_reader(int page, int size)
    {
        var f = new Fixture();
        Assert.Equal(QuizStatus.InvalidInput, (await f.List.ExecuteAsync(f.Map, f.Node, page, size, default)).Status);
        Assert.Equal(0, f.Storage.Calls);
    }

    [Fact]
    public async Task Valid_reads_use_resolved_workspace_and_preserve_contracts_order_and_page()
    {
        var f = new Fixture(); var result = await f.List.ExecuteAsync(f.Map, f.Node, 3, 10, default);
        Assert.Equal(QuizStatus.Success, result.Status); Assert.Equal(3, result.Collection!.Page);
        Assert.Equal(10, result.Collection.PageSize); Assert.True(result.Collection.HasMore);
        Assert.Equal((20, 10), f.Storage.Page); Assert.Equal((f.Workspace.Id, f.Map, f.Node), f.Storage.Scope);
        Assert.Same(f.Storage.Summaries, result.Collection.Quizzes);
        Assert.Same(f.Storage.Definition, (await f.Get.ExecuteAsync(f.Map, f.Node, f.Id, default)).Definition);
        Assert.Same(f.Storage.TakeDefinition, (await f.Take.ExecuteAsync(f.Map, f.Node, f.Id, default)).Definition);
        Assert.Equal(f.Id, f.Storage.QuizId);
    }

    [Fact]
    public async Task Missing_scoped_resources_are_generic_not_found_for_all_use_cases()
    {
        var f = new Fixture(); f.Storage.Exists = false;
        Assert.Equal(QuizStatus.NotFound, (await f.List.ExecuteAsync(f.Map, f.Node, 1, 20, default)).Status);
        Assert.Equal(QuizStatus.NotFound, (await f.Get.ExecuteAsync(f.Map, f.Node, f.Id, default)).Status);
        Assert.Equal(QuizStatus.NotFound, (await f.Take.ExecuteAsync(f.Map, f.Node, f.Id, default)).Status);
        Assert.Equal(QuizStatus.NotFound, (await f.Create.ExecuteAsync(f.Map, f.Node, Input, default)).Status);
        Assert.Null(f.Storage.Created);
        Assert.Equal(QuizStatus.NotFound, await f.Replace.ExecuteAsync(f.Map, f.Node, f.Id, 1, Input, default));
        Assert.Equal(QuizStatus.NotFound, await f.Delete.ExecuteAsync(f.Map, f.Node, f.Id, default));
    }

    [Fact]
    public async Task Create_validates_complete_domain_and_uses_server_clock_and_ids()
    {
        var f = new Fixture();
        Assert.Equal(QuizStatus.InvalidInput, (await f.Create.ExecuteAsync(f.Map, f.Node, new(" ", []), default)).Status);
        Assert.Null(f.Storage.Created);
        var result = await f.Create.ExecuteAsync(f.Map, f.Node, Input, default);
        Assert.Equal(QuizStatus.Success, result.Status); Assert.NotNull(f.Storage.Created);
        Assert.Equal("title", result.Definition!.Title); Assert.Equal(1, result.Definition.ContentRevision);
        Assert.Equal(f.Node, result.Definition.KnowledgeNodeId); Assert.Equal(f.Clock.GetUtcNow(), result.Definition.CreatedAtUtc);
        Assert.Equal(1, result.Definition.Questions[0].CorrectOptionIndex);
        f.Storage.Outcome = QuizStatus.NotFound;
        var lostParent = await f.Create.ExecuteAsync(f.Map, f.Node, Input, default);
        Assert.Equal(QuizStatus.NotFound, lostParent.Status); Assert.Null(lostParent.Definition);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)]
    public async Task Nonpositive_expected_revision_rejects_before_writer(int revision)
    {
        var f = new Fixture();
        Assert.Equal(QuizStatus.InvalidInput, await f.Replace.ExecuteAsync(f.Map, f.Node, f.Id, revision, Input, default));
        Assert.Equal(0, f.Storage.Calls);
    }

    [Theory]
    [InlineData(QuizStatus.ContentChanged)] [InlineData(QuizStatus.InvalidInput)]
    [InlineData(QuizStatus.NotFound)] [InlineData(QuizStatus.Success)]
    public async Task Replacement_and_delete_forward_scope_revision_clock_and_writer_results(QuizStatus status)
    {
        var f = new Fixture(); f.Storage.Outcome = status;
        Assert.Equal(status, await f.Replace.ExecuteAsync(f.Map, f.Node, f.Id, 7, Input, default));
        Assert.Same(Input, f.Storage.Input); Assert.Equal(7, f.Storage.Revision); Assert.Equal(f.Clock.GetUtcNow(), f.Storage.Now);
        Assert.Equal((f.Workspace.Id, f.Map, f.Node), f.Storage.Scope);
        Assert.Equal(status, await f.Delete.ExecuteAsync(f.Map, f.Node, f.Id, default)); Assert.Equal(f.Id, f.Storage.QuizId);
    }

    [Fact]
    public async Task Cancellation_precedes_storage_and_provider_errors_are_not_empty_data()
    {
        var f = new Fixture(); var ct = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.List.ExecuteAsync(f.Map, f.Node, 1, 20, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Get.ExecuteAsync(f.Map, f.Node, f.Id, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Take.ExecuteAsync(f.Map, f.Node, f.Id, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Create.ExecuteAsync(f.Map, f.Node, Input, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Replace.ExecuteAsync(f.Map, f.Node, f.Id, 1, Input, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Delete.ExecuteAsync(f.Map, f.Node, f.Id, ct));
        Assert.Equal(0, f.Storage.Calls); f.Storage.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Get.ExecuteAsync(f.Map, f.Node, f.Id, default));
    }

    private sealed class Fixture
    {
        public Guid Map = Guid.NewGuid(), Node = Guid.NewGuid(), Id = Guid.NewGuid();
        public PersonalWorkspace Workspace = PersonalWorkspace.Create("owner", DateTimeOffset.UnixEpoch);
        public Current Current = new(); public Accounts Accounts = new(); public Clock Clock = new(); public Storage Storage = new();
        private GetOrCreatePersonalWorkspace Resolver => new(Current, Accounts, new WorkspaceStore(Workspace), Clock);
        public ListQuizzes List => new(Resolver, Storage); public GetQuiz Get => new(Resolver, Storage);
        public TakeQuiz Take => new(Resolver, Storage); public CreateQuiz Create => new(Resolver, Storage, Clock);
        public ReplaceQuiz Replace => new(Resolver, Storage, Clock); public DeleteQuiz Delete => new(Resolver, Storage);
    }
    private sealed class Current : ICurrentUser { public bool Authenticated = true; public bool IsAuthenticated => Authenticated; public string? UserId => "owner"; }
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
    private sealed class Storage : IQuizReader, IQuizStore
    {
        public int Calls, Revision; public bool Exists = true, Fail; public QuizStatus Outcome = QuizStatus.Success;
        public (Guid Workspace, Guid Map, Guid Node) Scope; public (int Offset, int Size) Page;
        public Guid QuizId; public Quiz? Created; public QuizInput? Input; public DateTimeOffset Now;
        public QuizDefinition Definition = QuizDefinition.From(Quiz.Create(Guid.NewGuid(), "t", InputQuestions(), DateTimeOffset.UnixEpoch));
        public QuizTakeDefinition TakeDefinition = new(Guid.NewGuid(), "t", 1, []);
        public QuizSummary[] Summaries = [new(Guid.NewGuid(), "second", 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            new(Guid.NewGuid(), "first", 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)];
        private static QuizQuestionInput[] InputQuestions() => [new("q", ["a", "b"], 0)];
        private void Capture(Guid w, Guid m, Guid n) { Calls++; Scope = (w, m, n); if (Fail) throw new InvalidOperationException("Synthetic failure"); }
        public Task<QuizPage?> ListAsync(Guid w, Guid m, Guid n, int offset, int size, CancellationToken ct)
        { Capture(w, m, n); Page = (offset, size); return Task.FromResult(Exists ? new QuizPage(Summaries, true) : null); }
        public Task<QuizDefinition?> GetAsync(Guid w, Guid m, Guid n, Guid id, CancellationToken ct)
        { Capture(w, m, n); QuizId = id; return Task.FromResult(Exists ? Definition : null); }
        public Task<QuizTakeDefinition?> TakeAsync(Guid w, Guid m, Guid n, Guid id, CancellationToken ct)
        { Capture(w, m, n); QuizId = id; return Task.FromResult(Exists ? TakeDefinition : null); }
        public Task<bool> NodeExistsAsync(Guid w, Guid m, Guid n, CancellationToken ct) { Capture(w, m, n); return Task.FromResult(Exists); }
        public Task<QuizStatus> CreateAsync(Guid w, Guid m, Quiz quiz, CancellationToken ct)
        { Capture(w, m, quiz.KnowledgeNodeId); Created = quiz; return Task.FromResult(Outcome); }
        public Task<QuizStatus> ReplaceAsync(Guid w, Guid m, Guid n, Guid id, int revision, QuizInput input, DateTimeOffset now, CancellationToken ct)
        { Capture(w, m, n); QuizId = id; Revision = revision; Input = input; Now = now; return Task.FromResult(Exists ? Outcome : QuizStatus.NotFound); }
        public Task<QuizStatus> DeleteAsync(Guid w, Guid m, Guid n, Guid id, CancellationToken ct)
        { Capture(w, m, n); QuizId = id; return Task.FromResult(Exists ? Outcome : QuizStatus.NotFound); }
    }
}

using Talynor.Application.Accounts;
using Talynor.Application.StudySessions;
using Talynor.Application.Workspaces;
using Talynor.Domain.Workspaces;

namespace Talynor.UnitTests;

public class StudySessionReadTests
{
    [Theory]
    [InlineData(1, 20, 0)] [InlineData(3, 100, 200)] [InlineData(int.MaxValue, 1, int.MaxValue - 1)]
    public async Task Pagination_and_server_workspace_are_forwarded_without_reordering(int page, int size, int offset)
    {
        var f = new Fixture();
        f.Reader.Page = new([new(Guid.NewGuid(), f.Node, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)], true);
        using var cancellation = new CancellationTokenSource();
        var result = await f.List.ExecuteAsync(f.Map, f.Node, page, size, cancellation.Token);
        Assert.Equal(StudySessionReadStatus.Success, result.Status);
        Assert.Same(f.Reader.Page.Sessions, result.History!.Sessions);
        Assert.Equal(page, result.History.Page); Assert.Equal(size, result.History.PageSize); Assert.True(result.History.HasMore);
        Assert.Equal((f.Store.Existing!.Id, f.Map, f.Node, cancellation.Token), f.Reader.Call);
        Assert.Equal((offset, size), (f.Reader.Offset, f.Reader.Size));
    }

    [Theory]
    [InlineData(0, 20)] [InlineData(-1, 20)] [InlineData(int.MinValue, 20)]
    [InlineData(1, 0)] [InlineData(1, -1)] [InlineData(1, 101)] [InlineData(1, int.MaxValue)]
    [InlineData(int.MaxValue, 20)]
    public async Task Invalid_or_overflowing_pagination_is_rejected_without_reader_access(int page, int size)
    {
        var f = new Fixture();
        Assert.Equal(StudySessionReadStatus.Invalid, (await f.List.ExecuteAsync(f.Map, f.Node, page, size, CancellationToken.None)).Status);
        Assert.Equal(0, f.Reader.Calls);
    }

    [Fact]
    public async Task Detail_preserves_identity_and_fractional_duration_without_persistence_types()
    {
        var f = new Fixture(); var id = Guid.NewGuid();
        f.Reader.Session = new(id, f.Node, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddTicks(1234567));
        var result = await f.Get.ExecuteAsync(f.Map, f.Node, id, CancellationToken.None);
        Assert.Equal(StudySessionReadStatus.Success, result.Status); Assert.Same(f.Reader.Session, result.Session);
        Assert.Equal(0.1234567, result.Session!.DurationSeconds);
        Assert.Equal(id, f.Reader.SessionId); Assert.Equal(f.Store.Existing!.Id, f.Reader.Call.Workspace);
        Assert.Null((f.Reader.Session with { EndedAtUtc = null }).DurationSeconds);
    }

    [Fact]
    public async Task Absent_parent_or_detail_is_not_found_but_owned_empty_page_is_success()
    {
        var f = new Fixture();
        Assert.Equal(StudySessionReadStatus.NotFound, (await f.List.ExecuteAsync(f.Map, f.Node, 1, 20, CancellationToken.None)).Status);
        Assert.Equal(StudySessionReadStatus.NotFound, (await f.Get.ExecuteAsync(f.Map, f.Node, Guid.NewGuid(), CancellationToken.None)).Status);
        f.Reader.Page = new([], false);
        var result = await f.List.ExecuteAsync(f.Map, f.Node, 1, 20, CancellationToken.None);
        Assert.Equal(StudySessionReadStatus.Success, result.Status); Assert.Empty(result.History!.Sessions); Assert.False(result.History.HasMore);
    }

    [Theory]
    [InlineData("anonymous")] [InlineData("missing-id")] [InlineData("missing-account")] [InlineData("wrong-account")]
    public async Task Invalid_identity_never_accesses_workspace_or_reader(string scenario)
    {
        var f = new Fixture(); f.Current.IsAuthenticated = scenario != "anonymous";
        if (scenario == "missing-id") f.Current.UserId = null;
        if (scenario == "missing-account") f.Accounts.AccountId = null;
        if (scenario == "wrong-account") f.Accounts.AccountId = "other";
        Assert.Equal(StudySessionReadStatus.AuthenticationRequired, (await f.List.ExecuteAsync(f.Map, f.Node, 1, 20, CancellationToken.None)).Status);
        Assert.Equal(StudySessionReadStatus.AuthenticationRequired, (await f.Get.ExecuteAsync(f.Map, f.Node, Guid.NewGuid(), CancellationToken.None)).Status);
        Assert.Equal(0, f.Reader.Calls); Assert.Equal(0, f.Store.Reads);
    }

    [Fact]
    public async Task Existing_lazy_workspace_resolution_is_preserved()
    {
        var f = new Fixture(); f.Store.Existing = null; f.Reader.Page = new([], false);
        Assert.Equal(StudySessionReadStatus.Success, (await f.List.ExecuteAsync(f.Map, f.Node, 1, 20, CancellationToken.None)).Status);
        Assert.NotNull(f.Store.Added); Assert.Equal("owner", f.Store.Added.OwnerUserId);
        Assert.Equal(f.Store.Added.Id, f.Reader.Call.Workspace);
    }

    [Theory]
    [InlineData("account")] [InlineData("workspace")] [InlineData("reader")]
    public async Task Operational_failures_propagate_for_both_reads(string operation)
    {
        var f = new Fixture(); var error = new InvalidOperationException("Synthetic failure.");
        if (operation == "account") f.Accounts.Failure = error;
        else if (operation == "workspace") f.Store.Failure = error;
        else f.Reader.Failure = error;
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => f.List.ExecuteAsync(f.Map, f.Node, 1, 20, CancellationToken.None)));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Get.ExecuteAsync(f.Map, f.Node, Guid.NewGuid(), CancellationToken.None)));
    }

    [Fact]
    public async Task Cancellation_stops_work_before_account_workspace_and_reader_access()
    {
        var f = new Fixture(); var ct = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.List.ExecuteAsync(f.Map, f.Node, 1, 20, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Get.ExecuteAsync(f.Map, f.Node, Guid.NewGuid(), ct));
        Assert.Equal(0, f.Reader.Calls); Assert.Equal(0, f.Store.Reads); Assert.Equal(0, f.Accounts.Reads);
    }

    private sealed class Fixture
    {
        public Guid Map { get; } = Guid.NewGuid(); public Guid Node { get; } = Guid.NewGuid();
        public Current Current { get; } = new(); public Accounts Accounts { get; } = new();
        public Store Store { get; } = new(); public Reader Reader { get; } = new();
        public ListStudySessionsForNode List => new(new(Current, Accounts, Store, TimeProvider.System), Reader);
        public GetStudySessionForNode Get => new(new(Current, Accounts, Store, TimeProvider.System), Reader);
    }
    private sealed class Current : ICurrentUser
    { public bool IsAuthenticated { get; set; } = true; public string? UserId { get; set; } = "owner"; }
    private sealed class Accounts : IIdentityAccountService
    {
        public string? AccountId = "owner"; public Exception? Failure; public int Reads;
        public Task<AccountSummary?> GetByIdAsync(string id, CancellationToken ct)
        { Reads++; return Failure is not null ? Task.FromException<AccountSummary?>(Failure) : Task.FromResult(AccountId is null ? null : new AccountSummary(AccountId, "test@example.invalid")); }
        public Task<RegisterUserResult> CreateAsync(string email, string password, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Store : IPersonalWorkspaceStore
    {
        public PersonalWorkspace? Existing = PersonalWorkspace.Create("owner", DateTimeOffset.UnixEpoch);
        public PersonalWorkspace? Added; public Exception? Failure; public int Reads;
        public Task<PersonalWorkspace?> GetByOwnerUserIdAsync(string id, CancellationToken ct)
        { Reads++; return Failure is not null ? Task.FromException<PersonalWorkspace?>(Failure) : Task.FromResult(Existing); }
        public Task<PersonalWorkspace> AddOrGetExistingAsync(PersonalWorkspace workspace, CancellationToken ct)
        { Added = workspace; return Task.FromResult(workspace); }
    }
    private sealed class Reader : IStudySessionReader
    {
        public Task<ActiveStudySession?> GetActiveAsync(Guid workspaceId, CancellationToken ct) => throw new NotSupportedException();
        public StudySessionPage? Page; public StudySessionReadModel? Session;
        public Exception? Failure; public int Calls; public Guid SessionId; public int Offset; public int Size;
        public (Guid Workspace, Guid Map, Guid Node, CancellationToken Token) Call;
        public Task<StudySessionPage?> ListCompletedForNodeAsync(Guid workspace, Guid map, Guid node, int offset, int size, CancellationToken ct)
        { Calls++; Call = (workspace, map, node, ct); Offset = offset; Size = size; return Failure is not null ? Task.FromException<StudySessionPage?>(Failure) : Task.FromResult(Page); }
        public Task<StudySessionReadModel?> GetForNodeAsync(Guid workspace, Guid map, Guid node, Guid id, CancellationToken ct)
        { Calls++; Call = (workspace, map, node, ct); SessionId = id; return Failure is not null ? Task.FromException<StudySessionReadModel?>(Failure) : Task.FromResult(Session); }
    }
}

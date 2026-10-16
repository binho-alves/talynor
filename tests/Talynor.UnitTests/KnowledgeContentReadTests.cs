using Talynor.Application.Accounts;
using Talynor.Application.KnowledgeContents;
using Talynor.Application.Workspaces;
using Talynor.Domain.KnowledgeContents;
using Talynor.Domain.Workspaces;

namespace Talynor.UnitTests;

public class KnowledgeContentReadTests
{
    [Theory]
    [InlineData(0)] [InlineData(2)]
    public async Task List_preserves_reader_order_and_forwards_only_server_workspace_and_route_ids(int count)
    {
        var f = new Fixture();
        f.Reader.Contents = Enumerable.Range(0, count).Select(i => new KnowledgeContentSummary(Guid.NewGuid(),
            KnowledgeContentType.Note, $"Note {count - i}", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null)).ToArray();
        using var cancel = new CancellationTokenSource();
        var result = await f.List.ExecuteAsync(f.Map, f.Node, cancel.Token);
        Assert.Equal(KnowledgeContentReadStatus.Success, result.Status); Assert.Equal(f.Reader.Contents, result.Contents);
        Assert.Equal((f.Workspace.Id, f.Map, f.Node, cancel.Token), f.Reader.Call);
    }

    [Fact]
    public async Task Detail_returns_own_projection_and_forwards_content_id()
    {
        var f = new Fixture(); var id = Guid.NewGuid();
        f.Reader.Content = new(id, KnowledgeContentType.Link, null, null, "http://intranet", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var result = await f.Get.ExecuteAsync(f.Map, f.Node, id, CancellationToken.None);
        Assert.Equal(KnowledgeContentReadStatus.Success, result.Status); Assert.Same(f.Reader.Content, result.Content);
        Assert.Equal(id, f.Reader.ContentId); Assert.Equal(f.Workspace.Id, f.Reader.Call.Workspace);
    }

    [Fact]
    public async Task Scoped_reader_absence_is_uniform_not_found_not_empty_success()
    {
        var f = new Fixture();
        Assert.Equal(KnowledgeContentReadStatus.NotFound, (await f.List.ExecuteAsync(f.Map, f.Node, CancellationToken.None)).Status);
        Assert.Equal(KnowledgeContentReadStatus.NotFound, (await f.Get.ExecuteAsync(f.Map, f.Node, Guid.NewGuid(), CancellationToken.None)).Status);
    }

    [Theory]
    [InlineData("anonymous")] [InlineData("missing-id")] [InlineData("missing-account")]
    [InlineData("wrong-account")]
    public async Task Invalid_identity_never_accesses_reader_or_workspace(string scenario)
    {
        var f = new Fixture(); f.Current.IsAuthenticated = scenario != "anonymous";
        if (scenario == "missing-id") f.Current.UserId = null;
        if (scenario == "missing-account") f.Accounts.AccountId = null;
        if (scenario == "wrong-account") f.Accounts.AccountId = "other";
        Assert.Equal(KnowledgeContentReadStatus.AuthenticationRequired, (await f.List.ExecuteAsync(f.Map, f.Node, CancellationToken.None)).Status);
        Assert.Equal(KnowledgeContentReadStatus.AuthenticationRequired, (await f.Get.ExecuteAsync(f.Map, f.Node, Guid.NewGuid(), CancellationToken.None)).Status);
        Assert.Equal(0, f.Reader.Calls); Assert.Equal(0, f.Store.Reads);
    }

    [Fact]
    public async Task Absent_workspace_uses_existing_lazy_resolution_without_seeding_content()
    {
        var f = new Fixture(); f.Store.Existing = null; f.Reader.Contents = [];
        Assert.Equal(KnowledgeContentReadStatus.Success, (await f.List.ExecuteAsync(f.Map, f.Node, CancellationToken.None)).Status);
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
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => f.List.ExecuteAsync(f.Map, f.Node, CancellationToken.None)));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Get.ExecuteAsync(f.Map, f.Node, Guid.NewGuid(), CancellationToken.None)));
    }

    [Fact]
    public async Task Cancellation_prevents_account_workspace_and_reader_access()
    {
        var f = new Fixture(); var ct = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.List.ExecuteAsync(f.Map, f.Node, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Get.ExecuteAsync(f.Map, f.Node, Guid.NewGuid(), ct));
        Assert.Equal(0, f.Reader.Calls); Assert.Equal(0, f.Store.Reads); Assert.Equal(0, f.Accounts.Reads);
    }

    private sealed class Fixture
    {
        public Guid Map { get; } = Guid.NewGuid(); public Guid Node { get; } = Guid.NewGuid();
        public PersonalWorkspace Workspace => Store.Existing!;
        public Current Current { get; } = new(); public Accounts Accounts { get; } = new();
        public Store Store { get; } = new(); public Reader Reader { get; } = new();
        public ListKnowledgeContentsForNode List => new(new(Current, Accounts, Store, TimeProvider.System), Reader);
        public GetKnowledgeContentForNode Get => new(new(Current, Accounts, Store, TimeProvider.System), Reader);
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
    private sealed class Reader : IKnowledgeContentReader
    {
        public IReadOnlyList<KnowledgeContentSummary>? Contents; public KnowledgeContentDetail? Content;
        public Exception? Failure; public int Calls; public Guid ContentId;
        public (Guid Workspace, Guid Map, Guid Node, CancellationToken Token) Call;
        public Task<IReadOnlyList<KnowledgeContentSummary>?> ListForNodeAsync(Guid workspace, Guid map, Guid node, CancellationToken ct)
        { Calls++; Call = (workspace, map, node, ct); return Failure is not null ? Task.FromException<IReadOnlyList<KnowledgeContentSummary>?>(Failure) : Task.FromResult(Contents); }
        public Task<KnowledgeContentDetail?> GetForNodeAsync(Guid workspace, Guid map, Guid node, Guid id, CancellationToken ct)
        { Calls++; Call = (workspace, map, node, ct); ContentId = id; return Failure is not null ? Task.FromException<KnowledgeContentDetail?>(Failure) : Task.FromResult(Content); }
    }
}

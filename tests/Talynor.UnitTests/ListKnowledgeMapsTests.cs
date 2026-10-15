using Talynor.Application.Accounts;
using Talynor.Application.KnowledgeMaps;
using Talynor.Application.Workspaces;
using Talynor.Domain.Workspaces;

namespace Talynor.UnitTests;

public class ListKnowledgeMapsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Valid_account_lists_summaries_in_reader_order_using_resolved_workspace_and_cancellation(int count)
    {
        var fixture = new Fixture();
        fixture.Reader.Maps = Enumerable.Range(0, count)
            .Select(index => new KnowledgeMapSummary(Guid.NewGuid(), $"Title {count - index}")).ToArray();
        using var cancellation = new CancellationTokenSource();

        var result = await fixture.UseCase.ExecuteAsync(cancellation.Token);

        Assert.Equal(KnowledgeMapListStatus.Success, result.Status);
        Assert.Equal(fixture.Reader.Maps, result.Maps);
        Assert.Equal(fixture.Workspaces.Existing!.Id, fixture.Reader.WorkspaceId);
        Assert.Equal(cancellation.Token, fixture.Reader.Token);
        Assert.Equal("owner", fixture.Accounts.RequestedId);
        Assert.Equal("owner", fixture.Workspaces.Owner);
        Assert.Equal(1, fixture.Reader.Calls);
        Assert.Null(fixture.Workspaces.Added);
    }

    [Theory]
    [InlineData(false, "owner")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, " ")]
    public async Task Invalid_current_user_never_accesses_workspace_or_maps(bool authenticated, string? id)
    {
        var fixture = new Fixture();
        fixture.Current.IsAuthenticated = authenticated;
        fixture.Current.UserId = id;
        var result = await fixture.UseCase.ExecuteAsync(CancellationToken.None);
        Assert.Equal(KnowledgeMapListStatus.AuthenticationRequired, result.Status);
        Assert.Null(result.Maps);
        Assert.Null(fixture.Accounts.RequestedId);
        Assert.Null(fixture.Workspaces.Owner);
        Assert.Equal(0, fixture.Reader.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("another-account")]
    public async Task Missing_or_mismatched_account_is_not_an_empty_success(string? accountId)
    {
        var fixture = new Fixture();
        fixture.Accounts.AccountId = accountId;
        var result = await fixture.UseCase.ExecuteAsync(CancellationToken.None);
        Assert.Equal(KnowledgeMapListStatus.AuthenticationRequired, result.Status);
        Assert.Null(result.Maps);
        Assert.Null(fixture.Workspaces.Owner);
        Assert.Equal(0, fixture.Reader.Calls);
    }

    [Fact]
    public async Task Missing_workspace_is_resolved_lazily_without_creating_a_map()
    {
        var fixture = new Fixture();
        fixture.Workspaces.Existing = null;
        var result = await fixture.UseCase.ExecuteAsync(CancellationToken.None);
        Assert.Equal(KnowledgeMapListStatus.Success, result.Status);
        Assert.Empty(result.Maps!);
        Assert.NotNull(fixture.Workspaces.Added);
        Assert.Equal("owner", fixture.Workspaces.Added.OwnerUserId);
        Assert.Equal(fixture.Workspaces.Added.Id, fixture.Reader.WorkspaceId);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("workspace")]
    [InlineData("reader")]
    public async Task Operational_failures_propagate_instead_of_returning_empty_maps(string operation)
    {
        var fixture = new Fixture();
        var failure = new InvalidOperationException("Synthetic discovery failure.");
        if (operation == "account") fixture.Accounts.Failure = failure;
        else if (operation == "workspace") fixture.Workspaces.Failure = failure;
        else fixture.Reader.Failure = failure;
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.UseCase.ExecuteAsync(CancellationToken.None));
        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task Cancelled_request_does_not_access_account_workspace_or_maps()
    {
        var fixture = new Fixture();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.UseCase.ExecuteAsync(new CancellationToken(true)));
        Assert.Null(fixture.Accounts.RequestedId);
        Assert.Null(fixture.Workspaces.Owner);
        Assert.Equal(0, fixture.Reader.Calls);
    }

    private sealed class Fixture
    {
        public Current Current { get; } = new();
        public Accounts Accounts { get; } = new();
        public Workspaces Workspaces { get; } = new();
        public Reader Reader { get; } = new();
        public ListKnowledgeMapsForCurrentWorkspace UseCase => new(
            new(Current, Accounts, Workspaces, TimeProvider.System), Reader);
    }
    private sealed class Current : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = "owner";
    }
    private sealed class Accounts : IIdentityAccountService
    {
        public string? AccountId { get; set; } = "owner";
        public string? RequestedId { get; private set; }
        public Exception? Failure { get; set; }
        public Task<AccountSummary?> GetByIdAsync(string id, CancellationToken ct)
        {
            RequestedId = id;
            return Failure is not null ? Task.FromException<AccountSummary?>(Failure) :
                Task.FromResult(AccountId is null ? null : new AccountSummary(AccountId, "synthetic@example.invalid"));
        }
        public Task<RegisterUserResult> CreateAsync(string email, string password, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Workspaces : IPersonalWorkspaceStore
    {
        public PersonalWorkspace? Existing { get; set; } = PersonalWorkspace.Create("owner", DateTimeOffset.UnixEpoch);
        public PersonalWorkspace? Added { get; private set; }
        public string? Owner { get; private set; }
        public Exception? Failure { get; set; }
        public Task<PersonalWorkspace?> GetByOwnerUserIdAsync(string owner, CancellationToken ct)
        {
            Owner = owner;
            return Failure is null ? Task.FromResult(Existing) : Task.FromException<PersonalWorkspace?>(Failure);
        }
        public Task<PersonalWorkspace> AddOrGetExistingAsync(PersonalWorkspace workspace, CancellationToken ct)
        { Added = workspace; return Task.FromResult(workspace); }
    }
    private sealed class Reader : IPersistedKnowledgeMapReader
    {
        public IReadOnlyList<KnowledgeMapSummary> Maps { get; set; } = [];
        public Guid WorkspaceId { get; private set; }
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }
        public Task<IReadOnlyList<KnowledgeMapSummary>> ListAsync(Guid workspaceId, CancellationToken ct)
        {
            WorkspaceId = workspaceId; Token = ct; Calls++;
            return Failure is null ? Task.FromResult(Maps) : Task.FromException<IReadOnlyList<KnowledgeMapSummary>>(Failure);
        }
        public Task<PersistedKnowledgeMap?> GetAsync(Guid mapId, Guid workspaceId, CancellationToken ct) => throw new NotSupportedException();
    }
}

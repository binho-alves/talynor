using Talynor.Application.Accounts;
using Talynor.Application.Workspaces;
using Talynor.Domain.Workspaces;

namespace Talynor.UnitTests;

public class PersonalWorkspaceUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 15, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "owner")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, " ")]
    public async Task Invalid_current_user_does_not_read_or_create_workspace(bool authenticated, string? id)
    {
        var accounts = new Accounts();
        var store = new Store();
        var useCase = new GetOrCreatePersonalWorkspace(new Current(authenticated, id), accounts, store, new Clock());
        Assert.Null(await useCase.ExecuteAsync(CancellationToken.None));
        Assert.Equal(0, accounts.Calls);
        Assert.Equal(0, store.Reads);
        Assert.Equal(0, store.Adds);
    }

    [Fact]
    public async Task Existing_workspace_returns_projection_without_add_or_changing_creation_time()
    {
        var original = PersonalWorkspace.Create("owner", Now.AddDays(-2));
        var store = new Store { Existing = original };
        var summary = await UseCase(store).ExecuteAsync(CancellationToken.None);
        Assert.Equal(new PersonalWorkspaceSummary(original.Id, "owner", original.CreatedAtUtc), summary);
        Assert.Equal("owner", store.Owner);
        Assert.Equal(1, store.Reads);
        Assert.Equal(0, store.Adds);
    }

    [Fact]
    public async Task Missing_workspace_uses_authenticated_owner_clock_and_cancellation()
    {
        var accounts = new Accounts();
        var store = new Store();
        using var cancellation = new CancellationTokenSource();
        var summary = await UseCase(store, accounts).ExecuteAsync(cancellation.Token);
        Assert.NotNull(summary);
        Assert.NotEqual(Guid.Empty, summary.Id);
        Assert.Equal("owner", summary.OwnerUserId);
        Assert.Equal(Now, summary.CreatedAtUtc);
        Assert.Equal("owner", accounts.Id);
        Assert.Equal(cancellation.Token, store.Token);
        Assert.Equal(1, store.Reads);
        Assert.Equal(1, store.Adds);
        Assert.Equal(summary.Id, store.Added!.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("another-owner")]
    public async Task Removed_or_mismatched_account_fails_closed_without_workspace_access(string? accountId)
    {
        var accounts = new Accounts { Account = accountId is null ? null : new(accountId, "synthetic@example.invalid") };
        var store = new Store();
        Assert.Null(await UseCase(store, accounts).ExecuteAsync(CancellationToken.None));
        Assert.Equal(0, store.Reads);
        Assert.Equal(0, store.Adds);
    }

    [Fact]
    public async Task Store_ownership_mismatch_is_not_returned()
    {
        var store = new Store { Existing = PersonalWorkspace.Create("another-owner", Now) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => UseCase(store).ExecuteAsync(CancellationToken.None));
        Assert.Equal(0, store.Adds);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("read")]
    [InlineData("add")]
    public async Task Operational_failure_propagates_without_becoming_absence(string operation)
    {
        var failure = new InvalidOperationException("Synthetic operational failure.");
        var accounts = new Accounts { Failure = operation == "account" ? failure : null };
        var store = new Store { Failure = operation == "account" ? null : failure, FailOnAdd = operation == "add" };
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UseCase(store, accounts).ExecuteAsync(CancellationToken.None));
        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task Cancelled_request_does_not_access_accounts_or_workspaces()
    {
        var accounts = new Accounts();
        var store = new Store();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UseCase(store, accounts).ExecuteAsync(new CancellationToken(true)));
        Assert.Equal(0, accounts.Calls);
        Assert.Equal(0, store.Reads);
        Assert.Equal(0, store.Adds);
    }

    private static GetOrCreatePersonalWorkspace UseCase(Store store, Accounts? accounts = null) =>
        new(new Current(true, "owner"), accounts ?? new Accounts(), store, new Clock());
    private sealed record Current(bool IsAuthenticated, string? UserId) : ICurrentUser;
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Accounts : IIdentityAccountService
    {
        public AccountSummary? Account { get; init; } = new("owner", "synthetic@example.invalid");
        public Exception? Failure { get; init; }
        public int Calls { get; private set; }
        public string? Id { get; private set; }
        public Task<AccountSummary?> GetByIdAsync(string id, CancellationToken cancellationToken)
        {
            Calls++; Id = id;
            return Failure is null ? Task.FromResult(Account) : Task.FromException<AccountSummary?>(Failure);
        }
        public Task<RegisterUserResult> CreateAsync(string email, string password, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
    private sealed class Store : IPersonalWorkspaceStore
    {
        public PersonalWorkspace? Existing { get; init; }
        public PersonalWorkspace? Added { get; private set; }
        public Exception? Failure { get; init; }
        public bool FailOnAdd { get; init; }
        public int Reads { get; private set; }
        public int Adds { get; private set; }
        public string? Owner { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<PersonalWorkspace?> GetByOwnerUserIdAsync(string ownerUserId, CancellationToken cancellationToken)
        {
            Reads++; Owner = ownerUserId; Token = cancellationToken;
            return Failure is not null && !FailOnAdd
                ? Task.FromException<PersonalWorkspace?>(Failure) : Task.FromResult(Existing);
        }
        public Task<PersonalWorkspace> AddOrGetExistingAsync(PersonalWorkspace workspace, CancellationToken cancellationToken)
        {
            Adds++; Added = workspace; Token = cancellationToken;
            return Failure is null ? Task.FromResult(workspace) : Task.FromException<PersonalWorkspace>(Failure);
        }
    }
}

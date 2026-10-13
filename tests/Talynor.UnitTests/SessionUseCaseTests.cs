using Talynor.Application.Accounts;

namespace Talynor.UnitTests;

public class SessionUseCaseTests
{
    [Fact]
    public async Task Existing_session_does_not_call_login_adapter()
    {
        var sessions = new FakeSessions();
        var result = await new LoginUser(new Current(true, "id"), sessions)
            .ExecuteAsync("test@example.invalid", "unchanged", CancellationToken.None);
        Assert.Equal(LoginUserResult.AlreadyAuthenticated, result);
        Assert.Equal(0, sessions.Calls);
    }

    [Theory]
    [InlineData(LoginSessionResult.Success, LoginUserResult.Success)]
    [InlineData(LoginSessionResult.InvalidCredentials, LoginUserResult.InvalidCredentials)]
    public async Task Login_prepares_only_email_and_maps_specific_result(
        LoginSessionResult adapterResult, LoginUserResult expected)
    {
        var sessions = new FakeSessions { Result = adapterResult };
        using var cancellation = new CancellationTokenSource();
        const string password = "  ç short  ";
        var result = await new LoginUser(new Current(false, null), sessions)
            .ExecuteAsync("  Test@example.invalid  ", password, cancellation.Token);
        Assert.Equal(expected, result);
        Assert.Equal("Test@example.invalid", sessions.Email);
        Assert.Equal(password, sessions.Password);
        Assert.Equal(cancellation.Token, sessions.Token);
        Assert.Equal(1, sessions.Calls);
    }

    [Fact]
    public async Task Login_operational_failure_is_not_invalid_credentials()
    {
        var sessions = new FakeSessions { Failure = new InvalidOperationException("Synthetic operational failure.") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new LoginUser(new Current(false, null), sessions)
            .ExecuteAsync("test@example.invalid", "unchanged", CancellationToken.None));
    }

    [Fact]
    public async Task Cancelled_login_does_not_call_adapter()
    {
        var sessions = new FakeSessions();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LoginUser(new Current(false, null), sessions)
            .ExecuteAsync("test@example.invalid", "unchanged", new CancellationToken(true)));
        Assert.Equal(0, sessions.Calls);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, " ")]
    [InlineData(false, "id")]
    public async Task No_valid_current_identity_does_not_read_account(bool authenticated, string? id)
    {
        var accounts = new FakeAccounts();
        Assert.Null(await new GetCurrentUser(new Current(authenticated, id), accounts).ExecuteAsync(CancellationToken.None));
        Assert.Equal(0, accounts.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Current_account_found_or_removed_is_returned_without_framework_types(bool found)
    {
        var accounts = new FakeAccounts { Summary = found ? new("id", "current@example.invalid") : null };
        var result = await new GetCurrentUser(new Current(true, "id"), accounts).ExecuteAsync(CancellationToken.None);
        Assert.Same(accounts.Summary, result);
        Assert.Equal("id", accounts.Id);
        Assert.Equal(1, accounts.Calls);
    }

    [Fact]
    public async Task Current_account_operational_error_propagates()
    {
        var accounts = new FakeAccounts { Failure = new InvalidOperationException("Synthetic read failure.") };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GetCurrentUser(new Current(true, "id"), accounts).ExecuteAsync(CancellationToken.None));
    }

    private sealed record Current(bool IsAuthenticated, string? UserId) : ICurrentUser;
    private sealed class FakeSessions : ISessionService
    {
        public LoginSessionResult Result { get; init; }
        public Exception? Failure { get; init; }
        public int Calls { get; private set; }
        public string? Email { get; private set; }
        public string? Password { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<LoginSessionResult> LoginAsync(string email, string password, CancellationToken cancellationToken)
        {
            Calls++; Email = email; Password = password; Token = cancellationToken;
            return Failure is null ? Task.FromResult(Result) : Task.FromException<LoginSessionResult>(Failure);
        }
        public Task LogoutAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class FakeAccounts : IIdentityAccountService
    {
        public AccountSummary? Summary { get; init; }
        public Exception? Failure { get; init; }
        public int Calls { get; private set; }
        public string? Id { get; private set; }
        public Task<AccountSummary?> GetByIdAsync(string id, CancellationToken cancellationToken)
        {
            Calls++; Id = id;
            return Failure is null ? Task.FromResult(Summary) : Task.FromException<AccountSummary?>(Failure);
        }
        public Task<RegisterUserResult> CreateAsync(string email, string password, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

using Talynor.Application.Accounts;

namespace Talynor.UnitTests;

public class RegisterUserTests
{
    [Theory]
    [InlineData(RegisterUserStatus.Success)]
    [InlineData(RegisterUserStatus.Invalid)]
    [InlineData(RegisterUserStatus.Duplicate)]
    public async Task Prepares_only_email_and_forwards_password_token_and_result(RegisterUserStatus status)
    {
        var expected = new RegisterUserResult(status);
        var accounts = new FakeAccounts(expected);
        using var cancellation = new CancellationTokenSource();
        const string password = "  uma frase com ç e espaços  ";

        var result = await new RegisterUser(accounts).ExecuteAsync(
            "  Estudo@example.invalid  ", password, cancellation.Token);

        Assert.Same(expected, result);
        Assert.Equal("Estudo@example.invalid", accounts.Email);
        Assert.Equal(password, accounts.Password);
        Assert.Equal(cancellation.Token, accounts.Token);
        Assert.Equal(1, accounts.Calls);
        // The account-only interface has no capability to authenticate or issue cookies.
    }

    [Fact]
    public async Task Empty_prepared_email_does_not_call_account_adapter()
    {
        var accounts = new FakeAccounts(new(RegisterUserStatus.Success));
        var result = await new RegisterUser(accounts).ExecuteAsync(" \t ", "unchanged", CancellationToken.None);
        Assert.Equal(RegisterUserStatus.Invalid, result.Status);
        Assert.Contains("email", result.Errors!.Keys);
        Assert.Equal(0, accounts.Calls);
    }

    [Fact]
    public async Task Already_cancelled_request_does_not_call_account_adapter()
    {
        var accounts = new FakeAccounts(new(RegisterUserStatus.Success));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RegisterUser(accounts)
            .ExecuteAsync("test@example.invalid", "unchanged", cancellation.Token));
        Assert.Equal(0, accounts.Calls);
    }

    private sealed class FakeAccounts(RegisterUserResult result) : IIdentityAccountService
    {
        public Task<AccountSummary?> GetByIdAsync(string id, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Registration does not read accounts.");

        public string? Email { get; private set; }
        public string? Password { get; private set; }
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }

        public Task<RegisterUserResult> CreateAsync(string email, string password, CancellationToken cancellationToken)
        {
            Calls++;
            Email = email;
            Password = password;
            Token = cancellationToken;
            return Task.FromResult(result);
        }
    }
}

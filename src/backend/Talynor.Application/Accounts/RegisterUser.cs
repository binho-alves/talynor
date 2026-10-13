namespace Talynor.Application.Accounts;

public sealed class RegisterUser(IIdentityAccountService accounts)
{
    public Task<RegisterUserResult> ExecuteAsync(string email, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var preparedEmail = email.Trim();
        if (preparedEmail.Length == 0)
        {
            return Task.FromResult(new RegisterUserResult(RegisterUserStatus.Invalid,
                new Dictionary<string, string[]> { ["email"] = ["Informe um email válido."] }));
        }

        return accounts.CreateAsync(preparedEmail, password, cancellationToken);
    }
}

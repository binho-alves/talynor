namespace Talynor.Application.Accounts;

public sealed class GetCurrentUser(ICurrentUser currentUser, IIdentityAccountService accounts)
{
    public Task<AccountSummary?> ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = currentUser.UserId;
        return currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(id)
            ? accounts.GetByIdAsync(id, cancellationToken)
            : Task.FromResult<AccountSummary?>(null);
    }
}

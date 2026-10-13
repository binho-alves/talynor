namespace Talynor.Application.Accounts;

public interface IIdentityAccountService
{
    Task<RegisterUserResult> CreateAsync(string email, string password, CancellationToken cancellationToken);
    Task<AccountSummary?> GetByIdAsync(string id, CancellationToken cancellationToken);
}

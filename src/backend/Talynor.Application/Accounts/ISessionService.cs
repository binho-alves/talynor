namespace Talynor.Application.Accounts;

public interface ISessionService
{
    Task<LoginSessionResult> LoginAsync(string email, string password, CancellationToken cancellationToken);
    Task LogoutAsync(CancellationToken cancellationToken);
}

public enum LoginSessionResult { Success, InvalidCredentials }

namespace Talynor.Application.Accounts;

public enum LoginUserResult { Success, InvalidCredentials, AlreadyAuthenticated }

public sealed class LoginUser(ICurrentUser currentUser, ISessionService sessions)
{
    public async Task<LoginUserResult> ExecuteAsync(
        string email, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (currentUser.IsAuthenticated)
        {
            return LoginUserResult.AlreadyAuthenticated;
        }

        var preparedEmail = email.Trim();
        if (preparedEmail.Length == 0)
        {
            return LoginUserResult.InvalidCredentials;
        }

        return await sessions.LoginAsync(preparedEmail, password, cancellationToken) switch
        {
            LoginSessionResult.Success => LoginUserResult.Success,
            LoginSessionResult.InvalidCredentials => LoginUserResult.InvalidCredentials,
            _ => throw new InvalidOperationException("Unexpected login result.")
        };
    }
}

using Microsoft.AspNetCore.Identity;
using Talynor.Application.Accounts;

namespace Talynor.Api.Accounts;

// Cookie issuance requires the current HTTP response, so this adapter belongs to the API.
public sealed class IdentitySessionService(UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn)
    : ISessionService
{
    public async Task<LoginSessionResult> LoginAsync(
        string email, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await users.FindByEmailAsync(email);
        if (user is null) return LoginSessionResult.InvalidCredentials;
        // Identity APIs do not accept cancellation tokens. Do not catch operational failures as credentials.
        var result = await signIn.PasswordSignInAsync(user, password, isPersistent: false, lockoutOnFailure: true);
        return result.Succeeded ? LoginSessionResult.Success : LoginSessionResult.InvalidCredentials;
    }

    public Task LogoutAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return signIn.SignOutAsync();
    }
}

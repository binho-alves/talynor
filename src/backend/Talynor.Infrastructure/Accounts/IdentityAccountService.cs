using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Talynor.Application.Accounts;

namespace Talynor.Infrastructure.Accounts;

public sealed class IdentityAccountService(UserManager<IdentityUser> users) : IIdentityAccountService
{
    public async Task<AccountSummary?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(id);
        if (user is null) return null;
        var email = await users.GetEmailAsync(user);
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("Account has no email.");
        }
        return new(user.Id, email);
    }

    public async Task<RegisterUserResult> CreateAsync(
        string email, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = new IdentityUser { Email = email, UserName = email };
        IdentityResult result;
        try
        {
            // UserManager's API does not accept a cancellation token.
            result = await users.CreateAsync(user, password);
        }
        catch (DbUpdateException exception) when (IsDuplicateUserName(exception))
        {
            return new(RegisterUserStatus.Duplicate);
        }

        if (result.Succeeded)
        {
            return new(RegisterUserStatus.Success);
        }

        var errors = result.Errors.ToArray();
        if (errors.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName"))
        {
            return new(RegisterUserStatus.Duplicate);
        }

        var publicErrors = new Dictionary<string, string[]>();
        foreach (var error in errors)
        {
            var (field, message) = error.Code switch
            {
                "InvalidEmail" or "InvalidUserName" => ("email", "Informe um email válido."),
                "PasswordTooShort" => ("password", "Use uma senha com pelo menos 15 unidades UTF-16, que não contenha apenas espaços."),
                "PasswordRequiresUniqueChars" or "PasswordRequiresNonAlphanumeric" or
                "PasswordRequiresDigit" or "PasswordRequiresLower" or "PasswordRequiresUpper" =>
                    ("password", "A senha não atende à política de criação."),
                _ => throw new InvalidOperationException("Unexpected account validation failure.")
            };
            publicErrors[field] = publicErrors.TryGetValue(field, out var messages)
                ? messages.Append(message).Distinct().ToArray() : [message];
        }

        if (publicErrors.Count == 0)
        {
            throw new InvalidOperationException("Account creation failed without a known validation result.");
        }

        return new(RegisterUserStatus.Invalid, publicErrors);
    }

    private static bool IsDuplicateUserName(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067
        } sqlite &&
        // SQLite exposes the violated columns in its diagnostic, not a constraint-name property.
        // Match only this single-column UNIQUE failure; PK, busy and other constraints propagate.
        sqlite.Message == "SQLite Error 19: 'UNIQUE constraint failed: AspNetUsers.NormalizedUserName'.";
}

using Talynor.Application.Accounts;
using Talynor.Domain.Workspaces;

namespace Talynor.Application.Workspaces;

public sealed class GetOrCreatePersonalWorkspace(ICurrentUser currentUser, IIdentityAccountService accounts,
    IPersonalWorkspaceStore workspaces, TimeProvider timeProvider)
{
    public async Task<PersonalWorkspaceSummary?> ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = currentUser.UserId;
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(owner)) return null;
        // A cookie can outlive an account until the next native stamp check.
        var account = await accounts.GetByIdAsync(owner, cancellationToken);
        if (account is null || account.Id != owner) return null;

        var workspace = await workspaces.GetByOwnerUserIdAsync(owner, cancellationToken)
            ?? await workspaces.AddOrGetExistingAsync(
                PersonalWorkspace.Create(owner, timeProvider.GetUtcNow()), cancellationToken);
        if (workspace.OwnerUserId != owner)
            throw new InvalidOperationException("Workspace ownership mismatch.");
        return new(workspace.Id, workspace.OwnerUserId, workspace.CreatedAtUtc);
    }
}

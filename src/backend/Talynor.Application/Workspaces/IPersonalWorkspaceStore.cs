using Talynor.Domain.Workspaces;

namespace Talynor.Application.Workspaces;

// Owner values are resolved by the use case, never accepted from an HTTP request.
public interface IPersonalWorkspaceStore
{
    Task<PersonalWorkspace?> GetByOwnerUserIdAsync(string ownerUserId, CancellationToken cancellationToken);
    Task<PersonalWorkspace> AddOrGetExistingAsync(PersonalWorkspace workspace, CancellationToken cancellationToken);
}

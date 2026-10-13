using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Talynor.Application.Workspaces;
using Talynor.Domain.Workspaces;
using Talynor.Infrastructure.Persistence;

namespace Talynor.Infrastructure.Workspaces;

public sealed class PersonalWorkspaceStore(TalynorDbContext context) : IPersonalWorkspaceStore
{
    public Task<PersonalWorkspace?> GetByOwnerUserIdAsync(string ownerUserId, CancellationToken cancellationToken) =>
        context.PersonalWorkspaces.AsNoTracking()
            .SingleOrDefaultAsync(workspace => workspace.OwnerUserId == ownerUserId, cancellationToken);

    public async Task<PersonalWorkspace> AddOrGetExistingAsync(
        PersonalWorkspace workspace, CancellationToken cancellationToken)
    {
        context.PersonalWorkspaces.Add(workspace);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return workspace;
        }
        catch (DbUpdateException exception) when (IsDuplicateOwner(exception))
        {
            context.Entry(workspace).State = EntityState.Detached;
            var existing = await GetByOwnerUserIdAsync(workspace.OwnerUserId, cancellationToken);
            if (existing is null) throw;
            return existing;
        }
    }

    private static bool IsDuplicateOwner(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067
        } sqlite && sqlite.Message ==
            "SQLite Error 19: 'UNIQUE constraint failed: PersonalWorkspaces.OwnerUserId'.";
}

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Application.Workspaces;
using Talynor.Domain.Workspaces;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class PersonalWorkspacePersistenceTests
{
    [Fact]
    public async Task Workspace_roundtrips_across_contexts_and_expected_owner_conflict_recovers_existing_row()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        var original = PersonalWorkspace.Create("internal-owner", new(2026, 10, 13, 15, 0, 0, TimeSpan.Zero));
        using (var scope = factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IPersonalWorkspaceStore>();
            Assert.Same(original, await store.AddOrGetExistingAsync(original, CancellationToken.None));
        }
        using (var scope = factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IPersonalWorkspaceStore>();
            var read = await store.GetByOwnerUserIdAsync(original.OwnerUserId, CancellationToken.None);
            Assert.NotNull(read);
            Assert.Equal(original.Id, read.Id);
            Assert.Equal(original.CreatedAtUtc, read.CreatedAtUtc);
            Assert.Null(await store.GetByOwnerUserIdAsync("other-owner", CancellationToken.None));
            var candidate = PersonalWorkspace.Create(original.OwnerUserId, original.CreatedAtUtc.AddDays(1));
            var recovered = await store.AddOrGetExistingAsync(candidate, CancellationToken.None);
            Assert.Equal(original.Id, recovered.Id);
            Assert.Equal(original.CreatedAtUtc, recovered.CreatedAtUtc);
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            Assert.Equal(EntityState.Detached, db.Entry(candidate).State);
            Assert.Equal(1, await db.PersonalWorkspaces.CountAsync());

            // Reuse this store and DbContext after the failed insert and recovery.
            var subsequent = PersonalWorkspace.Create("other-owner", original.CreatedAtUtc.AddDays(2));
            Assert.Same(subsequent, await store.AddOrGetExistingAsync(subsequent, CancellationToken.None));
            var persisted = await db.PersonalWorkspaces.AsNoTracking().ToListAsync();
            Assert.Equal(2, persisted.Count);
            var winner = Assert.Single(persisted, workspace => workspace.OwnerUserId == original.OwnerUserId);
            Assert.Equal(original.Id, winner.Id);
            Assert.Equal(original.CreatedAtUtc, winner.CreatedAtUtc);
            var saved = Assert.Single(persisted, workspace => workspace.Id == subsequent.Id);
            Assert.Equal(subsequent.OwnerUserId, saved.OwnerUserId);
            Assert.Equal(subsequent.CreatedAtUtc, saved.CreatedAtUtc);
            Assert.DoesNotContain(persisted, workspace => workspace.Id == candidate.Id);
            Assert.Equal(EntityState.Detached, db.Entry(candidate).State);
        }
    }

    [Fact]
    public async Task Database_itself_rejects_duplicate_owner_without_store_recovery_and_has_no_identity_fk()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var entity = db.Model.FindEntityType(typeof(PersonalWorkspace))!;
        Assert.Empty(entity.GetForeignKeys());
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[] { "OwnerUserId" }));
        db.PersonalWorkspaces.Add(PersonalWorkspace.Create("owner", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        db.PersonalWorkspaces.Add(PersonalWorkspace.Create("owner", DateTimeOffset.UtcNow));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var sqlite = Assert.IsType<SqliteException>(error.InnerException);
        Assert.Equal(19, sqlite.SqliteErrorCode);
        Assert.Equal(2067, sqlite.SqliteExtendedErrorCode);
        Assert.Equal(1, await db.PersonalWorkspaces.AsNoTracking().CountAsync());
    }

    [Theory]
    [InlineData(5, 5, "database is locked")]
    [InlineData(19, 1555, "UNIQUE constraint failed: PersonalWorkspaces.Id")]
    [InlineData(19, 2067, "UNIQUE constraint failed: SomeOtherTable.OwnerUserId")]
    public async Task Other_database_failures_are_not_translated_to_existing_workspace(int code, int extended, string diagnostic)
    {
        using var factory = new RegistrationTestFactory(new WorkspaceFailureInterceptor(code, extended, diagnostic));
        await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPersonalWorkspaceStore>();
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => store.AddOrGetExistingAsync(
            PersonalWorkspace.Create("owner", DateTimeOffset.UtcNow), CancellationToken.None));
        Assert.Equal(extended, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
    }

    private sealed class WorkspaceFailureInterceptor(int code, int extended, string diagnostic) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            eventData.Context!.ChangeTracker.Entries<PersonalWorkspace>().Any(entry => entry.State == EntityState.Added)
                ? throw new DbUpdateException("Synthetic workspace failure.",
                    new SqliteException($"SQLite Error {code}: '{diagnostic}'.", code, extended))
                : ValueTask.FromResult(result);
    }
}

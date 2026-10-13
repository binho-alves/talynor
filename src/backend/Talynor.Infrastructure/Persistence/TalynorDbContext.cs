using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Talynor.Domain.Workspaces;

namespace Talynor.Infrastructure.Persistence;

public sealed class TalynorDbContext(DbContextOptions<TalynorDbContext> options)
    : IdentityUserContext<IdentityUser>(options)
{
    public DbSet<PersonalWorkspace> PersonalWorkspaces => Set<PersonalWorkspace>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<PersonalWorkspace>(workspace =>
        {
            workspace.ToTable("PersonalWorkspaces");
            workspace.HasKey(value => value.Id);
            workspace.Property(value => value.Id).ValueGeneratedNever();
            workspace.Property(value => value.OwnerUserId).IsRequired();
            workspace.HasIndex(value => value.OwnerUserId).IsUnique();
            workspace.Property(value => value.CreatedAtUtc).IsRequired();
            // Logical Identity reference only; account-deletion lifecycle remains outside this slice.
        });
    }
}

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Talynor.Infrastructure.Persistence;

// Accounts only: no roles, workspace, or knowledge-map persistence in Phase 3.1.
public sealed class TalynorDbContext(DbContextOptions<TalynorDbContext> options)
    : IdentityUserContext<IdentityUser>(options);

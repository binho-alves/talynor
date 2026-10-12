using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Talynor.Infrastructure.Persistence;

// EF tooling runs without starting the API or registering authentication services.
public sealed class TalynorDbContextFactory : IDesignTimeDbContextFactory<TalynorDbContext>
{
    public TalynorDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Talynor");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__Talynor explicitly before running EF commands. " +
                "Use an absolute SQLite file path to select the intended local database.");
        }

        var sqlite = new SqliteConnectionStringBuilder(connectionString);
        var isInMemory = sqlite.DataSource == ":memory:" || sqlite.Mode == SqliteOpenMode.Memory;
        if (!isInMemory && !Path.IsPathFullyQualified(sqlite.DataSource))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__Talynor must use a fully qualified absolute SQLite file path " +
                "for EF commands, or an explicit in-memory database (:memory: or Mode=Memory). " +
                "Relative or empty file paths are not accepted by the design-time factory.");
        }

        return new TalynorDbContext(new DbContextOptionsBuilder<TalynorDbContext>()
            .UseSqlite(sqlite.ConnectionString).Options);
    }
}

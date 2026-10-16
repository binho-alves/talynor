using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class IdentityPersistenceTests
{
    [Fact]
    public async Task Migrations_create_role_free_identity_workspace_and_graph_schema_and_can_be_reapplied()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TalynorDbContext>().UseSqlite(connection).Options;
        await using var context = new TalynorDbContext(options);

        await context.Database.MigrateAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' " +
            "AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EFMigrations%' ORDER BY name";
        var tables = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        Assert.Equal(new[] { "AspNetUserClaims", "AspNetUserLogins", "AspNetUserTokens", "AspNetUsers",
            "KnowledgeContents", "KnowledgeEdges", "KnowledgeMaps", "KnowledgeNodes", "PersonalWorkspaces" }, tables);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(4, applied.Length);
        Assert.EndsWith("_InitialIdentity", applied[0]);
        Assert.EndsWith("_AddPersonalWorkspace", applied[1]);
        Assert.EndsWith("_AddPersistentKnowledgeGraph", applied[2]);
        Assert.EndsWith("_AddKnowledgeContent", applied[3]);

        await context.Database.MigrateAsync();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(applied, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
    }

    [Fact]
    public async Task Identity_store_persists_user_across_closed_contexts_and_connections()
    {
        var path = Path.Combine(Path.GetTempPath(), $"talynor-persistence-{Guid.NewGuid():N}.sqlite");
        var options = new DbContextOptionsBuilder<TalynorDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString).Options;
        var user = new IdentityUser
        {
            UserName = "persistence@example.invalid",
            NormalizedUserName = "PERSISTENCE@EXAMPLE.INVALID",
            Email = "persistence@example.invalid",
            NormalizedEmail = "PERSISTENCE@EXAMPLE.INVALID"
        };

        try
        {
            await using (var context = new TalynorDbContext(options))
            {
                await context.Database.MigrateAsync();
                using var store = new UserOnlyStore<IdentityUser>(context);
                var result = await store.CreateAsync(user, CancellationToken.None);
                Assert.True(result.Succeeded);
            }

            await using (var context = new TalynorDbContext(options))
            {
                using var store = new UserOnlyStore<IdentityUser>(context);
                var recovered = await store.FindByIdAsync(user.Id, CancellationToken.None);
                Assert.NotNull(recovered);
                Assert.Equal(user.UserName, recovered.UserName);
                Assert.Equal(user.Email, recovered.Email);
                Assert.Null(recovered.PasswordHash);
                Assert.False(recovered.EmailConfirmed);
                Assert.Single(await context.Users.ToListAsync());
            }
        }
        finally
        {
            DeleteDatabaseFiles(path);
        }
    }

    [Fact]
    public async Task Api_starts_and_serves_demo_without_creating_or_migrating_database()
    {
        var path = Path.Combine(Path.GetTempPath(), $"talynor-host-{Guid.NewGuid():N}.sqlite");
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider());
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["ConnectionStrings:Talynor"] = $"Data Source={path}" }));
            });
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
            });
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);

            using var health = await client.GetAsync("/health");
            using var map = await client.GetAsync("/api/knowledge-maps/software");
            using var csrf = await client.GetAsync("/api/auth/csrf");
            health.EnsureSuccessStatusCode();
            map.EnsureSuccessStatusCode();
            csrf.EnsureSuccessStatusCode();
            Assert.False(File.Exists(path));
        }
        finally
        {
            DeleteDatabaseFiles(path);
        }
    }

    private static void DeleteDatabaseFiles(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            File.Delete(path + suffix);
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Talynor.Api.Contracts;
using Talynor.Domain.Workspaces;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class PersonalWorkspaceTests
{
    private const string Password = "synthetic workspace test passphrase";

    [Fact]
    public async Task Anonymous_workspace_is_json_401_no_store_without_creating_database()
    {
        using var factory = new RegistrationTestFactory(); // No migration required to reject an anonymous request.
        using var client = factory.NewClient();
        using var response = await client.GetAsync("/api/workspace");
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "authentication_required");
        using var scope = factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.GetConnectionString();
        Assert.False(File.Exists(new SqliteConnectionStringBuilder(connection).DataSource));
    }

    [Fact]
    public async Task Workspace_is_lazy_idempotent_and_returns_only_id_and_creation_time()
    {
        var now = new DateTimeOffset(2026, 10, 13, 15, 0, 0, TimeSpan.Zero);
        using var factory = new RegistrationTestFactory(configureServices: services =>
            services.AddSingleton<TimeProvider>(new FixedClock(now)));
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await AuthenticateAsync(factory, client, "first@example.invalid");
        using var first = await client.GetAsync("/api/workspace");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True(first.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "createdAtUtc", "id" }, json.RootElement.EnumerateObject().Select(x => x.Name).OrderBy(x => x));
        var workspace = (await first.Content.ReadFromJsonAsync<PersonalWorkspaceResponse>())!;
        Assert.NotEqual(Guid.Empty, workspace.Id);
        Assert.Equal(now, workspace.CreatedAtUtc);
        for (var call = 0; call < 2; call++)
        {
            using var repeated = await client.GetAsync("/api/workspace");
            Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
            Assert.True(repeated.Headers.CacheControl?.NoStore);
            Assert.Equal(workspace, await repeated.Content.ReadFromJsonAsync<PersonalWorkspaceResponse>());
        }
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var persisted = Assert.Single(await db.PersonalWorkspaces.AsNoTracking().ToListAsync());
        Assert.Equal(workspace.Id, persisted.Id);
        Assert.Equal(workspace.CreatedAtUtc, persisted.CreatedAtUtc);
        Assert.Equal((await db.Users.SingleAsync()).Id, persisted.OwnerUserId);
    }

    [Fact]
    public async Task Two_users_are_isolated_and_client_owner_or_workspace_ids_cannot_select_another_workspace()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var first = factory.NewClient();
        using var second = factory.NewClient();
        await AuthenticateAsync(factory, first, "a@example.invalid");
        await AuthenticateAsync(factory, second, "b@example.invalid");
        var a = await WorkspaceAsync(first);
        var b = await WorkspaceAsync(second);
        Assert.NotEqual(a.Id, b.Id);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var ownerB = (await db.Users.SingleAsync(user => user.Email == "b@example.invalid")).Id;
        using var attempted = await first.GetAsync($"/api/workspace?ownerId={ownerB}&userId={ownerB}&workspaceId={b.Id}");
        Assert.Equal(HttpStatusCode.OK, attempted.StatusCode);
        Assert.Equal(a, await attempted.Content.ReadFromJsonAsync<PersonalWorkspaceResponse>());
        using var byId = await first.GetAsync($"/api/workspace/{b.Id}");
        Assert.Equal(HttpStatusCode.NotFound, byId.StatusCode);
        Assert.Equal(a, await WorkspaceAsync(first));
        Assert.Equal(b, await WorkspaceAsync(second));
        Assert.Equal(2, await db.PersonalWorkspaces.CountAsync());
        using var anonymous = factory.NewClient();
        foreach (var map in new[] { "software", "csharp" })
        {
            using var response = await anonymous.GetAsync("/api/knowledge-maps/" + map);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Concurrent_first_access_recovers_real_owner_unique_violation_and_returns_same_workspace()
    {
        var race = new WorkspaceRaceInterceptor();
        using var factory = new RegistrationTestFactory(race);
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await AuthenticateAsync(factory, client, "race@example.invalid");
        var responses = await Task.WhenAll(client.GetAsync("/api/workspace"), client.GetAsync("/api/workspace"))
            .WaitAsync(TimeSpan.FromSeconds(45));
        try
        {
            foreach (var response in responses)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.True(response.Headers.CacheControl?.NoStore);
            }
            Assert.Equal(await responses[0].Content.ReadFromJsonAsync<PersonalWorkspaceResponse>(),
                await responses[1].Content.ReadFromJsonAsync<PersonalWorkspaceResponse>());
            Assert.Equal(2, race.Arrivals);
            Assert.Equal(1, race.UniqueFailures);
            using var scope = factory.Services.CreateScope();
            Assert.Single(await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().PersonalWorkspaces.ToListAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Removed_account_cannot_access_existing_workspace_and_workspace_lifecycle_is_not_deleted()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await AuthenticateAsync(factory, client, "removed@example.invalid");
        var workspace = await WorkspaceAsync(client);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True((await users.DeleteAsync((await users.FindByEmailAsync("removed@example.invalid"))!)).Succeeded);
        }
        using var rejected = await client.GetAsync("/api/workspace");
        await AssertProblemAsync(rejected, HttpStatusCode.Unauthorized, "authentication_required");
        Assert.Contains(rejected.Headers.GetValues("Set-Cookie").Select(value => SetCookieHeaderValue.Parse(value)),
            cookie => cookie.Name == "__Host-Talynor.Auth" && cookie.Value.ToString() == string.Empty);
        using var finalScope = factory.Services.CreateScope();
        Assert.Equal(workspace.Id, (await finalScope.ServiceProvider.GetRequiredService<TalynorDbContext>()
            .PersonalWorkspaces.SingleAsync()).Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Database_failure_in_workspace_or_native_stamp_validation_is_sanitized_500(bool stampFailure)
    {
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        using var factory = new RegistrationTestFactory(configureServices: services =>
        {
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = clock);
            services.PostConfigure<SecurityStampValidatorOptions>(options => options.TimeProvider = clock);
        });
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await AuthenticateAsync(factory, client, "failure@example.invalid");
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.ExecuteSqlRawAsync(
                stampFailure ? "DROP TABLE AspNetUsers" : "DROP TABLE PersonalWorkspaces");
        if (stampFailure) clock.Advance(TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(1));
        using var response = await client.GetAsync(stampFailure ? "/API/WORKSPACE/" : "/api/workspace");
        await AssertProblemAsync(response, HttpStatusCode.InternalServerError, "unexpected_error");
        var body = await response.Content.ReadAsStringAsync();
        foreach (var marker in new[] { "AspNetUsers", "PersonalWorkspaces", "SQLite", "failure@example.invalid", Password })
            Assert.DoesNotContain(marker, body);
    }

    [Fact]
    public async Task Workspace_is_in_development_openapi()
    {
        using var factory = new RegistrationTestFactory();
        using var client = factory.NewClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("paths").GetProperty("/api/workspace").TryGetProperty("get", out _));
    }

    private static async Task AuthenticateAsync(RegistrationTestFactory factory, HttpClient client, string email)
    {
        using var registration = await RegistrationTestFactory.RegisterAsync(client, new { email, password = Password },
            await RegistrationTestFactory.CsrfAsync(client));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        Assert.Empty(await registration.Content.ReadAsByteArrayAsync());
        Assert.False(registration.Headers.Contains("Set-Cookie"));
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().PersonalWorkspaces.CountAsync());
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            { Content = JsonContent.Create(new { email, password = Password }) };
        request.Headers.Add("X-CSRF-TOKEN", await RegistrationTestFactory.CsrfAsync(client));
        using var login = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().PersonalWorkspaces.CountAsync());
    }

    private static async Task<PersonalWorkspaceResponse> WorkspaceAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/workspace");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PersonalWorkspaceResponse>())!;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.Location);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.False(json.RootElement.TryGetProperty("detail", out _));
        Assert.False(json.RootElement.TryGetProperty("exception", out _));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan time) => now += time;
    }
    private sealed class WorkspaceRaceInterceptor : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        private int failures;
        public int Arrivals => arrivals;
        public int UniqueFailures => failures;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<PersonalWorkspace>().Any(entry => entry.State == EntityState.Added))
            {
                if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Exception.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
                Interlocked.Increment(ref failures);
            return Task.CompletedTask;
        }
    }
}

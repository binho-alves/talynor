using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Api.Contracts;
using Talynor.Application.KnowledgeMaps;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class KnowledgeMapDiscoveryTests
{
    private const string Password = "synthetic discovery test passphrase";

    [Fact]
    public async Task Anonymous_discovery_is_private_no_store_and_does_not_create_database()
    {
        using var factory = new RegistrationTestFactory();
        using var client = factory.NewClient();
        using var response = await client.GetAsync("/api/maps");
        await ProblemAsync(response, HttpStatusCode.Unauthorized, "authentication_required");
        using var scope = factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.GetConnectionString();
        Assert.False(File.Exists(new SqliteConnectionStringBuilder(connection).DataSource));
    }

    [Fact]
    public async Task Empty_workspace_returns_empty_envelope_without_map_seed_or_csrf_requirement()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await LoginAsync(client, "empty@example.invalid");
        using (var before = factory.Services.CreateScope())
            Assert.Empty(await before.ServiceProvider.GetRequiredService<TalynorDbContext>().PersonalWorkspaces.ToArrayAsync());

        Assert.Empty((await DiscoverAsync(client)).Maps);
        Assert.Empty((await DiscoverAsync(client)).Maps);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        Assert.Single(await db.PersonalWorkspaces.ToArrayAsync());
        Assert.Empty(await db.KnowledgeMaps.ToArrayAsync());
        using var anonymous = factory.NewClient();
        foreach (var demo in new[] { "software", "csharp" })
        {
            using var response = await anonymous.GetAsync("/api/knowledge-maps/" + demo);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Create_then_discover_returns_only_id_and_title_and_preserves_private_read()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await LoginAsync(client, "author@example.invalid");
        var created = await CreateMapAsync(client, " My persisted map ");

        var map = Assert.Single((await DiscoverAsync(client)).Maps);
        Assert.Equal(created.Id, map.Id);
        Assert.Equal("My persisted map", map.Title);
        using var read = await client.GetAsync($"/api/maps/{map.Id:D}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True(read.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Discovery_is_workspace_scoped_despite_query_overrides_and_order_is_title_then_id()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var a = factory.NewClient();
        using var b = factory.NewClient();
        using var empty = factory.NewClient();
        await LoginAsync(a, "a@example.invalid");
        await LoginAsync(b, "b@example.invalid");
        await LoginAsync(empty, "empty@example.invalid");
        var alphaLow = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var alphaHigh = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var beta = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var lowercaseAlpha = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var own = new[] { (Id: beta, Title: "Beta"), (Id: alphaHigh, Title: "Alpha"),
            (Id: lowercaseAlpha, Title: "alpha"), (Id: alphaLow, Title: "Alpha") };
        var workspace = (await a.GetFromJsonAsync<PersonalWorkspaceResponse>("/api/workspace"))!;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            var createdAt = DateTimeOffset.UnixEpoch;
            // Explicit fixture IDs: insert the equal-title pair high-before-low, never in expected ID order.
            foreach (var map in own)
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO KnowledgeMaps (Id, WorkspaceId, Title, Description, CreatedAtUtc, UpdatedAtUtc) VALUES ({map.Id}, {workspace.Id}, {map.Title}, NULL, {createdAt}, {createdAt})");
        }
        var foreign = await CreateMapAsync(b, "Private B");
        var otherWorkspace = (await b.GetFromJsonAsync<PersonalWorkspaceResponse>("/api/workspace"))!;
        var otherAccount = (await b.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me"))!;
        var query = $"/api/maps?workspaceId={otherWorkspace.Id:D}&ownerUserId={Uri.EscapeDataString(otherAccount.Id)}";
        var expected = new[] { alphaLow, alphaHigh, beta, lowercaseAlpha };

        for (var repeat = 0; repeat < 2; repeat++)
            Assert.Equal(expected, (await DiscoverAsync(a, query)).Maps.Select(map => map.Id));
        Assert.Equal(foreign.Id, Assert.Single((await DiscoverAsync(b)).Maps).Id);
        Assert.Empty((await DiscoverAsync(empty, query)).Maps);
    }

    [Fact]
    public async Task Discovery_projects_untracked_summaries_without_loading_node_or_edge_tables()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var fixture = await KnowledgeGraphFixture.CreateAsync(db);
        db.ChangeTracker.Clear();
        // This isolated disposable database makes accidental graph loading observable.
        await db.Database.ExecuteSqlRawAsync("DROP TABLE KnowledgeEdges");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE KnowledgeNodes");
        var maps = await scope.ServiceProvider.GetRequiredService<IPersistedKnowledgeMapReader>()
            .ListAsync(fixture.Workspace.Id, CancellationToken.None);
        Assert.Equal(new[] { fixture.Child.Id, fixture.Map.Id }, maps.Select(map => map.Id));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Removed_account_returns_401_and_does_not_expose_orphan_maps()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await LoginAsync(client, "removed@example.invalid");
        await CreateMapAsync(client, "Private orphan");
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True((await users.DeleteAsync((await users.FindByEmailAsync("removed@example.invalid"))!)).Succeeded);
        }
        using var response = await client.GetAsync("/api/maps");
        await ProblemAsync(response, HttpStatusCode.Unauthorized, "authentication_required");
        using var final = factory.Services.CreateScope();
        Assert.Single(await final.ServiceProvider.GetRequiredService<TalynorDbContext>().KnowledgeMaps.ToArrayAsync());
    }

    [Theory]
    [InlineData("account")]
    [InlineData("maps")]
    [InlineData("stamp")]
    public async Task Operational_failure_is_sanitized_500_not_an_empty_collection(string operation)
    {
        var clock = new Clock();
        using var factory = new RegistrationTestFactory(configureServices: services =>
        {
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = clock);
            services.PostConfigure<SecurityStampValidatorOptions>(options => options.TimeProvider = clock);
        });
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await LoginAsync(client, "failure@example.invalid");
        using (var workspace = await client.GetAsync("/api/workspace")) Assert.Equal(HttpStatusCode.OK, workspace.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            await db.Database.ExecuteSqlRawAsync(operation == "maps" ? "DROP TABLE KnowledgeMaps" : "DROP TABLE AspNetUsers");
        }
        if (operation == "stamp") clock.Advance();
        using var response = await client.GetAsync("/API/MAPS/");
        await ProblemAsync(response, HttpStatusCode.InternalServerError, "unexpected_error");
    }

    [Fact]
    public async Task Discovery_envelope_is_in_development_openapi_alongside_authoring_and_private_read()
    {
        using var factory = new RegistrationTestFactory();
        using var client = factory.NewClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = json.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/maps").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/maps").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/maps/{mapId}").TryGetProperty("get", out _));
    }

    private static async Task LoginAsync(HttpClient client, string email)
    {
        using var registration = await RegistrationTestFactory.RegisterAsync(client, new { email, password = Password },
            await RegistrationTestFactory.CsrfAsync(client));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        { Content = JsonContent.Create(new { email, password = Password }) };
        request.Headers.Add("X-CSRF-TOKEN", await RegistrationTestFactory.CsrfAsync(client));
        using var login = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    private static async Task<KnowledgeMapAuthoringResponse> CreateMapAsync(HttpClient client, string title)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/maps")
        { Content = JsonContent.Create(new { title, description = "Not part of discovery" }) };
        request.Headers.Add("X-CSRF-TOKEN", await RegistrationTestFactory.CsrfAsync(client));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<KnowledgeMapAuthoringResponse>())!;
    }

    private static async Task<KnowledgeMapListResponse> DiscoverAsync(HttpClient client, string path = "/api/maps")
    {
        using var response = await client.GetAsync(path); // No CSRF header on this read.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.Location);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Properties(json.RootElement, "maps");
        foreach (var map in json.RootElement.GetProperty("maps").EnumerateArray()) Properties(map, "id", "title");
        return json.RootElement.Deserialize<KnowledgeMapListResponse>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Properties(json.RootElement, "status", "title", "code");
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        foreach (var marker in new[] { "SQLite", "Exception", "KnowledgeMaps", "AspNetUsers", "WorkspaceId", "OwnerUserId", "failure@example.invalid", Password })
            Assert.DoesNotContain(marker, body);
    }

    private static void Properties(JsonElement value, params string[] names) =>
        Assert.Equal(names.Order(), value.EnumerateObject().Select(property => property.Name).Order());

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance() => now += TimeSpan.FromMinutes(31);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Api.Contracts;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class PersistedKnowledgeMapsTests
{
    private const string Password = "synthetic persistent map test passphrase";

    [Fact]
    public async Task Anonymous_private_read_is_401_no_store_without_creating_database()
    {
        using var factory = new RegistrationTestFactory();
        using var client = factory.NewClient();
        using var response = await client.GetAsync($"/api/maps/{Guid.NewGuid()}");
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "authentication_required");
        using var scope = factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.GetConnectionString();
        Assert.False(File.Exists(new SqliteConnectionStringBuilder(connection).DataSource));
    }

    [Fact]
    public async Task Empty_guid_reaches_private_flow_and_returns_sanitized_no_store_not_found()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await LoginAsync(client, "empty-guid@example.invalid");
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().PersonalWorkspaces.CountAsync());

        using var response = await client.GetAsync($"/api/maps/{Guid.Empty:D}");
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "map_not_found");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Properties(json.RootElement, "status", "title", "code");
        Assert.Equal("Mapa não encontrado.", json.RootElement.GetProperty("title").GetString());

        // Lazy workspace resolution proves this was the private use case, not a routing 404.
        using var finalScope = factory.Services.CreateScope();
        var db = finalScope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        Assert.Equal(1, await db.PersonalWorkspaces.CountAsync());
        Assert.Equal(0, await db.KnowledgeMaps.CountAsync());
    }

    [Fact]
    public async Task Malformed_guid_is_routing_not_found_without_private_response_or_database_access()
    {
        using var factory = new RegistrationTestFactory(); // No migration/account is needed for route rejection.
        using var client = factory.NewClient();
        using var response = await client.GetAsync("/api/maps/not-a-guid");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Null(response.Headers.CacheControl);
        Assert.Null(response.Headers.Location);
        using var scope = factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.GetConnectionString();
        Assert.False(File.Exists(new SqliteConnectionStringBuilder(connection).DataSource));
    }

    [Fact]
    public async Task Authenticated_response_is_library_independent_private_no_store_and_includes_persisted_child_positions()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        var workspace = await AuthenticateAsync(client, "read@example.invalid");
        KnowledgeGraphFixture fixture;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            fixture = await KnowledgeGraphFixture.CreateAsync(db, await db.PersonalWorkspaces.SingleAsync(workspaceRow => workspaceRow.Id == workspace.Id));
        }
        using var response = await client.GetAsync($"/api/maps/{fixture.Map.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Properties(json.RootElement, "id", "title", "description", "nodes", "edges");
        Assert.Equal(fixture.Map.Id, json.RootElement.GetProperty("id").GetGuid());
        var node = Assert.Single(json.RootElement.GetProperty("nodes").EnumerateArray(),
            node => node.GetProperty("id").GetGuid() == fixture.First.Id);
        Properties(node, "id", "title", "description", "position", "childMapId");
        Properties(node.GetProperty("position"), "x", "y");
        Assert.Equal(125.5, node.GetProperty("position").GetProperty("x").GetDouble());
        Assert.Equal(-32.25, node.GetProperty("position").GetProperty("y").GetDouble());
        Assert.Equal(fixture.Child.Id, node.GetProperty("childMapId").GetGuid());
        var edge = Assert.Single(json.RootElement.GetProperty("edges").EnumerateArray());
        Properties(edge, "id", "sourceNodeId", "targetNodeId");
        Assert.Equal(fixture.First.Id, edge.GetProperty("sourceNodeId").GetGuid());
        Assert.Equal(fixture.Second.Id, edge.GetProperty("targetNodeId").GetGuid());
        using var child = await client.GetAsync($"/api/maps/{fixture.Child.Id}");
        Assert.Equal(HttpStatusCode.OK, child.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        foreach (var marker in new[] { "workspaceId", "ownerUserId", "progress", "studyMinutes", "type", "handles", "style" })
            Assert.DoesNotContain($"\"{marker}\"", body);
    }

    [Fact]
    public async Task Two_real_sessions_are_isolated_even_with_client_workspace_override_and_parallel_reads()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var a = factory.NewClient();
        using var b = factory.NewClient();
        var workspaceA = await AuthenticateAsync(a, "a@example.invalid");
        var workspaceB = await AuthenticateAsync(b, "b@example.invalid");
        KnowledgeGraphFixture fixture;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            fixture = await KnowledgeGraphFixture.CreateAsync(db,
                await db.PersonalWorkspaces.SingleAsync(row => row.Id == workspaceA.Id),
                await db.PersonalWorkspaces.SingleAsync(row => row.Id == workspaceB.Id));
        }
        using var missing = await a.GetAsync($"/api/maps/{Guid.NewGuid()}");
        using var foreign = await a.GetAsync($"/api/maps/{fixture.OtherMap.Id}?workspaceId={workspaceB.Id}");
        using var opposite = await b.GetAsync($"/api/maps/{fixture.Map.Id}?workspaceId={workspaceA.Id}");
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "map_not_found");
        await AssertProblemAsync(foreign, HttpStatusCode.NotFound, "map_not_found");
        await AssertProblemAsync(opposite, HttpStatusCode.NotFound, "map_not_found");
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await foreign.Content.ReadAsStringAsync());
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await opposite.Content.ReadAsStringAsync());
        var requests = Enumerable.Range(0, 8).Select(index => index % 2 == 0
            ? a.GetAsync($"/api/maps/{fixture.Map.Id}") : b.GetAsync($"/api/maps/{fixture.OtherMap.Id}"));
        var responses = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            for (var index = 0; index < responses.Length; index++)
            {
                Assert.Equal(HttpStatusCode.OK, responses[index].StatusCode);
                Assert.True(responses[index].Headers.CacheControl?.NoStore);
                var map = await responses[index].Content.ReadFromJsonAsync<PersistedKnowledgeMapResponse>();
                Assert.Equal(index % 2 == 0 ? fixture.Map.Id : fixture.OtherMap.Id, map!.Id);
            }
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task Deleted_account_cannot_read_orphan_workspace_map_before_next_stamp_interval()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        var workspace = await AuthenticateAsync(client, "removed@example.invalid");
        KnowledgeGraphFixture fixture;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            fixture = await KnowledgeGraphFixture.CreateAsync(db, await db.PersonalWorkspaces.SingleAsync(row => row.Id == workspace.Id));
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True((await users.DeleteAsync((await users.FindByEmailAsync("removed@example.invalid"))!)).Succeeded);
        }
        using var response = await client.GetAsync($"/api/maps/{fixture.Map.Id}");
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "authentication_required");
        using var finalScope = factory.Services.CreateScope();
        Assert.Equal(3, await finalScope.ServiceProvider.GetRequiredService<TalynorDbContext>().KnowledgeMaps.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Database_failure_in_reader_or_native_stamp_validation_is_sanitized(bool stampFailure)
    {
        var clock = new Clock();
        using var factory = new RegistrationTestFactory(configureServices: services =>
        {
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = clock);
            services.PostConfigure<SecurityStampValidatorOptions>(options => options.TimeProvider = clock);
        });
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        var workspace = await AuthenticateAsync(client, "failure@example.invalid");
        KnowledgeGraphFixture fixture;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            fixture = await KnowledgeGraphFixture.CreateAsync(db, await db.PersonalWorkspaces.SingleAsync(row => row.Id == workspace.Id));
            await db.Database.ExecuteSqlRawAsync(stampFailure ? "DROP TABLE AspNetUsers" : "DROP TABLE KnowledgeEdges");
        }
        if (stampFailure) clock.Advance(TimeSpan.FromMinutes(31));
        using var response = await client.GetAsync($"/API/MAPS/{fixture.Map.Id}/");
        await AssertProblemAsync(response, HttpStatusCode.InternalServerError, "unexpected_error");
        var body = await response.Content.ReadAsStringAsync();
        foreach (var marker in new[] { "SQLite", "AspNetUsers", "KnowledgeEdges", "WorkspaceId", "SELECT", "Exception", "failure@example.invalid", Password })
            Assert.DoesNotContain(marker, body);
    }

    [Fact]
    public async Task Private_read_resolves_empty_workspace_without_map_seed_and_preserves_anonymous_demo_routes()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await LoginAsync(client, "empty@example.invalid");
        using var missing = await client.GetAsync($"/api/maps/{Guid.NewGuid()}");
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "map_not_found");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        Assert.Equal(1, await db.PersonalWorkspaces.CountAsync());
        Assert.Equal(0, await db.KnowledgeMaps.CountAsync());
        using var anonymous = factory.NewClient();
        foreach (var map in new[] { "software", "csharp" })
        {
            using var demo = await anonymous.GetAsync("/api/knowledge-maps/" + map);
            Assert.Equal(HttpStatusCode.OK, demo.StatusCode);
        }
        using var oldRoute = await anonymous.GetAsync($"/api/knowledge-maps/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, oldRoute.StatusCode);
    }

    [Fact]
    public async Task Persisted_contract_is_in_development_openapi_without_workspace_or_xyflow_fields()
    {
        using var factory = new RegistrationTestFactory();
        using var client = factory.NewClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("paths").GetProperty("/api/maps/{mapId}").TryGetProperty("get", out _));
    }

    private static async Task<PersonalWorkspaceResponse> AuthenticateAsync(HttpClient client, string email)
    {
        await LoginAsync(client, email);
        return (await client.GetFromJsonAsync<PersonalWorkspaceResponse>("/api/workspace"))!;
    }

    private static async Task LoginAsync(HttpClient client, string email)
    {
        using var registration = await RegistrationTestFactory.RegisterAsync(client, new { email, password = Password },
            await RegistrationTestFactory.CsrfAsync(client));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            { Content = JsonContent.Create(new { email, password = Password }) };
        request.Headers.Add("X-CSRF-TOKEN", await RegistrationTestFactory.CsrfAsync(client));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static void Properties(JsonElement value, params string[] names) =>
        Assert.Equal(names.Order(), value.EnumerateObject().Select(property => property.Name).Order());

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.False(json.RootElement.TryGetProperty("detail", out _));
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan time) => now += time;
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Api.Contracts;
using Talynor.Application.KnowledgeContents;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class KnowledgeContentHttpTests
{
    private const string Password = "synthetic content test passphrase";
    private static string List(Guid map, Guid node) => $"/api/maps/{map:D}/nodes/{node:D}/contents";
    private static string Detail(Guid map, Guid node, Guid content) => $"{List(map, node)}/{content:D}";

    [Fact]
    public async Task Anonymous_reads_are_no_store_401_and_startup_does_not_create_database()
    {
        using var factory = new RegistrationTestFactory(); using var client = factory.NewClient();
        foreach (var path in new[] { List(Guid.NewGuid(), Guid.NewGuid()), Detail(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) })
        {
            using var response = await client.GetAsync(path);
            await Problem(response, HttpStatusCode.Unauthorized, "authentication_required");
        }
        using var scope = factory.Services.CreateScope();
        var cs = scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.GetConnectionString();
        Assert.False(File.Exists(new SqliteConnectionStringBuilder(cs).DataSource));
    }

    [Fact]
    public async Task Owner_reads_exact_discriminated_shapes_in_order_without_csrf_or_map_dto_expansion()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var client = factory.NewClient(); var workspace = await Login(client, "owner@example.invalid");
        KnowledgeContentFixture f;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            f = await KnowledgeContentFixture.CreateAsync(db, await db.PersonalWorkspaces.SingleAsync(w => w.Id == workspace.Id));
        }
        Assert.False(client.DefaultRequestHeaders.Contains("X-CSRF-TOKEN"));
        var list = await Get(client, List(f.Graph.Map.Id, f.Graph.First.Id));
        Properties(list, "contents"); var items = list.GetProperty("contents").EnumerateArray().ToArray();
        Assert.Equal(new[] { f.Early.Id, f.Link.Id, f.Note.Id }, items.Select(item => item.GetProperty("id").GetGuid()));
        foreach (var note in items.Where(item => item.GetProperty("type").GetString() == "note"))
        { Properties(note, "id", "type", "title", "createdAtUtc", "updatedAtUtc"); Assert.False(note.TryGetProperty("body", out _)); }
        var linkSummary = items[1];
        Properties(linkSummary, "id", "type", "title", "createdAtUtc", "updatedAtUtc", "url");
        Assert.Equal(JsonValueKind.Null, linkSummary.GetProperty("title").ValueKind);
        Assert.Equal(f.Link.Url, linkSummary.GetProperty("url").GetString());
        var noteDetail = await Get(client, Detail(f.Graph.Map.Id, f.Graph.First.Id, f.Note.Id));
        Properties(noteDetail, "id", "type", "title", "createdAtUtc", "updatedAtUtc", "body");
        Assert.Equal("note", noteDetail.GetProperty("type").GetString()); Assert.Equal(f.Note.Body, noteDetail.GetProperty("body").GetString());
        Assert.Equal(KnowledgeContentFixture.Instant, noteDetail.GetProperty("createdAtUtc").GetDateTimeOffset());
        Assert.Equal(TimeSpan.Zero, noteDetail.GetProperty("updatedAtUtc").GetDateTimeOffset().Offset);
        var link = await Get(client, Detail(f.Graph.Map.Id, f.Graph.First.Id, f.Link.Id));
        Properties(link, "id", "type", "title", "createdAtUtc", "updatedAtUtc", "url");
        Assert.Equal("link", link.GetProperty("type").GetString()); Assert.Equal(f.Link.Url, link.GetProperty("url").GetString());
        Assert.Empty((await Get(client, List(f.Graph.Map.Id, f.Graph.Second.Id))).GetProperty("contents").EnumerateArray());
        var map = await Get(client, $"/api/maps/{f.Graph.Map.Id:D}");
        Properties(map, "id", "title", "description", "nodes", "edges");
        foreach (var node in map.GetProperty("nodes").EnumerateArray())
            Properties(node, "id", "title", "description", "position", "childMapId");
    }

    [Fact]
    public async Task Foreign_missing_and_mixed_parent_paths_are_indistinguishable_despite_query_overrides()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var a = factory.NewClient(); using var b = factory.NewClient();
        var wa = await Login(a, "a@example.invalid"); var wb = await Login(b, "b@example.invalid");
        KnowledgeContentFixture f;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            f = await KnowledgeContentFixture.CreateAsync(db, await db.PersonalWorkspaces.SingleAsync(w => w.Id == wa.Id),
                await db.PersonalWorkspaces.SingleAsync(w => w.Id == wb.Id));
        }
        var query = $"?WorkspaceId={wb.Id:D}&OwnerUserId=ignored";
        Assert.Equal(3, (await Get(a, List(f.Graph.Map.Id, f.Graph.First.Id) + query)).GetProperty("contents").GetArrayLength());
        Assert.Equal(f.ForeignContent.Id, (await Get(b, Detail(f.Graph.OtherMap.Id, f.ForeignNode.Id, f.ForeignContent.Id))).GetProperty("id").GetGuid());
        var paths = new[]
        {
            List(f.Graph.OtherMap.Id, f.ForeignNode.Id), List(f.Graph.OtherMap.Id, f.Graph.First.Id),
            List(f.Graph.Map.Id, f.ForeignNode.Id), List(Guid.NewGuid(), f.Graph.First.Id),
            List(f.Graph.Map.Id, Guid.NewGuid()), List(Guid.Empty, f.Graph.First.Id),
            Detail(f.Graph.OtherMap.Id, f.ForeignNode.Id, f.ForeignContent.Id),
            Detail(f.Graph.Map.Id, f.Graph.First.Id, f.ForeignContent.Id),
            Detail(f.Graph.OtherMap.Id, f.Graph.First.Id, f.Note.Id),
            Detail(f.Graph.Map.Id, f.Graph.Second.Id, f.Note.Id),
            Detail(f.Graph.Child.Id, f.Graph.ChildNode.Id, f.Note.Id),
            Detail(f.Graph.Map.Id, f.Graph.First.Id, Guid.NewGuid()),
            Detail(f.Graph.Map.Id, f.Graph.First.Id, Guid.Empty)
        };
        string? baseline = null;
        foreach (var path in paths)
        {
            using var response = await a.GetAsync(path + query);
            var body = await Problem(response, HttpStatusCode.NotFound, "content_not_found");
            baseline ??= body; Assert.Equal(baseline, body);
        }
        using var reverse = await b.GetAsync(Detail(f.Graph.Map.Id, f.Graph.First.Id, f.Note.Id));
        Assert.Equal(baseline, await Problem(reverse, HttpStatusCode.NotFound, "content_not_found"));
    }

    [Fact]
    public async Task Malformed_guids_follow_existing_guid_route_constraints()
    {
        using var factory = new RegistrationTestFactory(); using var client = factory.NewClient();
        foreach (var path in new[] { "/api/maps/not-a-guid", $"/api/maps/not-a-guid/nodes/{Guid.NewGuid()}/contents",
            $"/api/maps/{Guid.NewGuid()}/nodes/not-a-guid/contents", $"{List(Guid.NewGuid(), Guid.NewGuid())}/not-a-guid" })
        { using var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); }
    }

    [Fact]
    public async Task Removed_account_cannot_read_orphan_content_and_receives_local_sign_out()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var client = factory.NewClient(); var workspace = await Login(client, "removed-content@example.invalid"); KnowledgeContentFixture f;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            f = await KnowledgeContentFixture.CreateAsync(db, await db.PersonalWorkspaces.SingleAsync(w => w.Id == workspace.Id));
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True((await users.DeleteAsync((await users.FindByEmailAsync("removed-content@example.invalid"))!)).Succeeded);
        }
        using var response = await client.GetAsync(List(f.Graph.Map.Id, f.Graph.First.Id));
        await Problem(response, HttpStatusCode.Unauthorized, "authentication_required");
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("__Host-Talynor.Auth=;", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("reader")] [InlineData("account")] [InlineData("stamp")]
    public async Task Operational_errors_are_sanitized_including_native_cookie_validation(string operation)
    {
        var clock = new Clock();
        using var factory = new RegistrationTestFactory(configureServices: services =>
        {
            if (operation == "reader") services.AddScoped<IKnowledgeContentReader, FailedReader>();
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = clock);
            services.PostConfigure<SecurityStampValidatorOptions>(options => options.TimeProvider = clock);
        });
        await factory.InitializeAsync(); using var client = factory.NewClient(); await Login(client, "content-failure@example.invalid");
        if (operation != "reader")
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.ExecuteSqlRawAsync("DROP TABLE AspNetUsers");
        }
        if (operation == "stamp") clock.Advance();
        foreach (var path in new[] { List(Guid.NewGuid(), Guid.NewGuid()), Detail(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) })
        { using var response = await client.GetAsync(path); await Problem(response, HttpStatusCode.InternalServerError, "unexpected_error"); }
    }

    [Fact]
    public async Task Only_two_gets_are_exposed_in_openapi_and_content_mutations_are_absent()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        await Login(client, "routes@example.invalid");
        var openapi = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var routes = openapi.GetProperty("paths").EnumerateObject().Where(path => path.Name.Contains("/contents", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, routes.Length); Assert.All(routes, route => Properties(route.Value, "get"));
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Patch, HttpMethod.Delete })
        foreach (var path in new[] { List(Guid.NewGuid(), Guid.NewGuid()), Detail(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) })
        {
            using var request = new HttpRequestMessage(method, path); using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
    }

    private static async Task<PersonalWorkspaceResponse> Login(HttpClient client, string email)
    {
        using var register = await RegistrationTestFactory.RegisterAsync(client, new { email, password = Password }, await RegistrationTestFactory.CsrfAsync(client));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { email, password = Password }) };
        request.Headers.Add("X-CSRF-TOKEN", await RegistrationTestFactory.CsrfAsync(client));
        using var login = await client.SendAsync(request); Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return (await client.GetFromJsonAsync<PersonalWorkspaceResponse>("/api/workspace"))!;
    }
    private static async Task<JsonElement> Get(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore); Assert.Null(response.Headers.Location);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return json.RootElement.Clone();
    }
    private static async Task<string> Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore); Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(); using var json = JsonDocument.Parse(body);
        Properties(json.RootElement, "status", "title", "code"); Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        foreach (var marker in new[] { "SQLite", "Exception", "KnowledgeContents", "AspNetUsers", "WorkspaceId", "OwnerUserId", Password, "private literal", "synthetic failure" })
            Assert.DoesNotContain(marker, body);
        return body;
    }
    private static void Properties(JsonElement value, params string[] names) =>
        Assert.Equal(names.Order(), value.EnumerateObject().Select(property => property.Name).Order());
    private sealed class FailedReader : IKnowledgeContentReader
    {
        public Task<IReadOnlyList<KnowledgeContentSummary>?> ListForNodeAsync(Guid workspace, Guid map, Guid node, CancellationToken ct) =>
            throw new InvalidOperationException("Private synthetic failure.");
        public Task<KnowledgeContentDetail?> GetForNodeAsync(Guid workspace, Guid map, Guid node, Guid content, CancellationToken ct) =>
            throw new InvalidOperationException("Private synthetic failure.");
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance() => now += TimeSpan.FromMinutes(31);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Api.Contracts;
using Talynor.Application.StudySessions;
using Talynor.Domain.StudySessions;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class StudySessionHttpTests
{
    private const string Password = "synthetic study test passphrase";
    private static string List(Guid map, Guid node) => $"/api/maps/{map:D}/nodes/{node:D}/study-sessions";
    private static string Detail(Guid map, Guid node, Guid session) => $"{List(map, node)}/{session:D}";

    [Fact]
    public async Task Anonymous_reads_are_no_store_401_without_startup_database_creation()
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
    public async Task Reads_have_exact_shapes_completed_only_and_active_detail_null_duration_without_csrf()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var client = factory.NewClient(); var workspace = await Login(client, "study@example.invalid");
        var f = await Seed(factory, workspace);
        Assert.False(client.DefaultRequestHeaders.Contains("X-CSRF-TOKEN"));
        var list = await Get(client, List(f.Graph.Map.Id, f.Graph.First.Id));
        Properties(list, "sessions", "page", "pageSize", "hasMore");
        Assert.Equal(1, list.GetProperty("page").GetInt32()); Assert.Equal(20, list.GetProperty("pageSize").GetInt32());
        Assert.False(list.GetProperty("hasMore").GetBoolean());
        var items = list.GetProperty("sessions").EnumerateArray().ToArray();
        Assert.Equal(new[] { f.High.Id, f.Low.Id, f.Early.Id }, items.Select(s => s.GetProperty("id").GetGuid()));
        foreach (var item in items)
        {
            Properties(item, "id", "knowledgeNodeId", "startedAtUtc", "endedAtUtc", "durationSeconds");
            Assert.Equal(f.Graph.First.Id, item.GetProperty("knowledgeNodeId").GetGuid());
            Assert.Equal(TimeSpan.Zero, item.GetProperty("startedAtUtc").GetDateTimeOffset().Offset);
            Assert.Equal(JsonValueKind.Number, item.GetProperty("durationSeconds").ValueKind);
        }
        Assert.Equal(0, items[0].GetProperty("durationSeconds").GetDouble());
        Assert.Equal(0.1234567, items[1].GetProperty("durationSeconds").GetDouble());
        var detail = await Get(client, Detail(f.Graph.Map.Id, f.Graph.First.Id, f.Low.Id));
        Assert.Equal(items[1].GetRawText(), detail.GetRawText());
        var active = await Get(client, Detail(f.Graph.Map.Id, f.Graph.First.Id, f.Active.Id));
        Properties(active, "id", "knowledgeNodeId", "startedAtUtc", "endedAtUtc", "durationSeconds");
        Assert.Equal(JsonValueKind.Null, active.GetProperty("durationSeconds").ValueKind);
        Assert.Equal(JsonValueKind.Null, active.GetProperty("endedAtUtc").ValueKind);
        var empty = await Get(client, List(f.Graph.Map.Id, f.Graph.Second.Id));
        Assert.Empty(empty.GetProperty("sessions").EnumerateArray()); Assert.False(empty.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Page_defaults_maximum_and_hasMore_are_observable_at_boundaries()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var client = factory.NewClient(); var f = await Seed(factory, await Login(client, "pages@example.invalid"));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            for (var i = 0; i < 98; i++)
            {
                var time = StudySessionFixture.Instant.AddHours(i + 1);
                db.StudySessions.Add(new(Guid.NewGuid(), f.Graph.First.Id, time, time));
            }
            await db.SaveChangesAsync();
        }
        foreach (var (query, page, size, count, more) in new[]
        {
            ("", 1, 20, 20, true), ("?page=2", 2, 20, 20, true), ("?page=6", 6, 20, 1, false),
            ("?pageSize=100", 1, 100, 100, true), ("?page=2&pageSize=100", 2, 100, 1, false),
            ("?page=3&pageSize=100", 3, 100, 0, false), ("?pageSize=1", 1, 1, 1, true)
        })
        {
            var body = await Get(client, List(f.Graph.Map.Id, f.Graph.First.Id) + query);
            Assert.Equal(page, body.GetProperty("page").GetInt32()); Assert.Equal(size, body.GetProperty("pageSize").GetInt32());
            Assert.Equal(count, body.GetProperty("sessions").GetArrayLength()); Assert.Equal(more, body.GetProperty("hasMore").GetBoolean());
        }
    }

    [Theory]
    [InlineData("page=0")] [InlineData("page=-1")] [InlineData("pageSize=0")] [InlineData("pageSize=-1")]
    [InlineData("pageSize=101")] [InlineData("page=2147483647&pageSize=100")]
    [InlineData("page=2147483648")] [InlineData("pageSize=2147483648")] [InlineData("page=bad")]
    [InlineData("pageSize=bad")] [InlineData("page=")]
    public async Task Invalid_pagination_is_sanitized_validation_failed_not_clamped(string query)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var client = factory.NewClient(); await Login(client, "invalid-page@example.invalid");
        using var response = await client.GetAsync(List(Guid.NewGuid(), Guid.NewGuid()) + "?" + query);
        await Problem(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Foreign_missing_and_mismatched_paths_are_identical_despite_ownership_query_overrides()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var a = factory.NewClient(); using var b = factory.NewClient();
        var wa = await Login(a, "study-a@example.invalid"); var wb = await Login(b, "study-b@example.invalid");
        var f = await Seed(factory, wa, wb); var query = $"?WorkspaceId={wb.Id:D}&OwnerUserId=ignored";
        Assert.Equal(3, (await Get(a, List(f.Graph.Map.Id, f.Graph.First.Id) + query)).GetProperty("sessions").GetArrayLength());
        Assert.Equal(f.Foreign.Id, (await Get(b, Detail(f.Graph.OtherMap.Id, f.Content.ForeignNode.Id, f.Foreign.Id))).GetProperty("id").GetGuid());
        string? baseline = null;
        foreach (var path in new[]
        {
            List(f.Graph.OtherMap.Id, f.Content.ForeignNode.Id), List(f.Graph.Map.Id, f.Content.ForeignNode.Id),
            List(f.Graph.OtherMap.Id, f.Graph.First.Id), List(Guid.NewGuid(), f.Graph.First.Id),
            List(f.Graph.Map.Id, Guid.NewGuid()), List(Guid.Empty, f.Graph.First.Id),
            Detail(f.Graph.OtherMap.Id, f.Content.ForeignNode.Id, f.Foreign.Id),
            Detail(f.Graph.Map.Id, f.Graph.First.Id, f.Foreign.Id), Detail(f.Graph.Map.Id, f.Graph.Second.Id, f.High.Id),
            Detail(f.Graph.Child.Id, f.Graph.ChildNode.Id, f.High.Id), Detail(f.Graph.OtherMap.Id, f.Graph.First.Id, f.High.Id),
            Detail(f.Graph.Map.Id, f.Graph.First.Id, Guid.NewGuid()), Detail(f.Graph.Map.Id, f.Graph.First.Id, Guid.Empty)
        })
        {
            using var response = await a.GetAsync(path + query);
            var body = await Problem(response, HttpStatusCode.NotFound, "study_session_not_found");
            baseline ??= body; Assert.Equal(baseline, body);
        }
        using var reverse = await b.GetAsync(Detail(f.Graph.Map.Id, f.Graph.First.Id, f.High.Id));
        Assert.Equal(baseline, await Problem(reverse, HttpStatusCode.NotFound, "study_session_not_found"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Removed_account_cannot_read_and_receives_local_signout(bool detail)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var client = factory.NewClient(); var f = await Seed(factory, await Login(client, "removed-study@example.invalid"));
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True((await users.DeleteAsync((await users.FindByEmailAsync("removed-study@example.invalid"))!)).Succeeded);
        }
        using var response = await client.GetAsync(detail ? Detail(f.Graph.Map.Id, f.Graph.First.Id, f.High.Id) : List(f.Graph.Map.Id, f.Graph.First.Id));
        await Problem(response, HttpStatusCode.Unauthorized, "authentication_required");
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), h => h.StartsWith("__Host-Talynor.Auth=;", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("reader")] [InlineData("account")] [InlineData("workspace")] [InlineData("stamp")]
    public async Task Operational_failures_are_sanitized_including_before_controller(string operation)
    {
        var clock = new Clock();
        using var factory = new RegistrationTestFactory(configureServices: services =>
        {
            if (operation == "reader") services.AddScoped<IStudySessionReader, FailedReader>();
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = clock);
            services.PostConfigure<SecurityStampValidatorOptions>(options => options.TimeProvider = clock);
        });
        await factory.InitializeAsync(); using var client = factory.NewClient(); await Login(client, "study-failure@example.invalid");
        if (operation != "reader")
        {
            using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            if (operation == "workspace") await db.Database.ExecuteSqlRawAsync("DROP TABLE PersonalWorkspaces");
            else await db.Database.ExecuteSqlRawAsync("DROP TABLE AspNetUsers");
        }
        if (operation == "stamp") clock.Advance();
        foreach (var path in new[] { List(Guid.NewGuid(), Guid.NewGuid()), Detail(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) })
        { using var response = await client.GetAsync(path); await Problem(response, HttpStatusCode.InternalServerError, "unexpected_error"); }
    }

    [Theory]
    [InlineData(false, "active")] [InlineData(false, "completed")] [InlineData(false, "none")]
    [InlineData(true, "active")] [InlineData(true, "completed")] [InlineData(true, "none")]
    public async Task Graph_delete_protects_any_session_and_preserves_existing_content_cascade_when_none(bool map, string state)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync();
        using var client = factory.NewClient(); var workspace = await Login(client, "delete-study@example.invalid"); KnowledgeContentFixture f;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            f = await KnowledgeContentFixture.CreateAsync(db, await db.PersonalWorkspaces.SingleAsync(w => w.Id == workspace.Id));
            await db.KnowledgeEdges.ExecuteDeleteAsync();
            if (state != "none") db.StudySessions.Add(new(Guid.NewGuid(), f.Graph.First.Id, StudySessionFixture.Instant,
                state == "active" ? null : StudySessionFixture.Instant));
            await db.SaveChangesAsync();
        }
        var path = $"/api/maps/{f.Graph.Map.Id:D}" + (map ? "" : $"/nodes/{f.Graph.First.Id:D}");
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Add("X-CSRF-TOKEN", await RegistrationTestFactory.CsrfAsync(client));
        using var response = await client.SendAsync(request);
        if (state == "none") Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        else await Problem(response, HttpStatusCode.Conflict, map ? "map_in_use" : "node_in_use");
        using var check = factory.Services.CreateScope(); var verify = check.ServiceProvider.GetRequiredService<TalynorDbContext>();
        Assert.Equal(state != "none", await verify.KnowledgeNodes.AnyAsync(n => n.Id == f.Graph.First.Id));
        Assert.Equal(state == "none" ? 0 : 3, await verify.KnowledgeContents.CountAsync(c => c.KnowledgeNodeId == f.Graph.First.Id));
        Assert.Equal(state == "none" ? 0 : 1, await verify.StudySessions.CountAsync());
        Assert.Equal(!map || state != "none", await verify.KnowledgeNodes.AnyAsync(n => n.Id == f.Graph.Second.Id));
        Assert.True(await verify.KnowledgeContents.AnyAsync(c => c.Id == f.ForeignContent.Id));
    }

    [Fact]
    public async Task Openapi_exposes_only_two_nested_gets_without_active_or_write_endpoints()
    {
        using var factory = new RegistrationTestFactory(); using var client = factory.NewClient();
        var openapi = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var routes = openapi.GetProperty("paths").EnumerateObject().Where(p => p.Name.Contains("study-sessions", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, routes.Length); Assert.All(routes, r => Properties(r.Value, "get"));
        using var active = await client.GetAsync("/api/study-sessions/active"); Assert.Equal(HttpStatusCode.NotFound, active.StatusCode);
        using var malformed = await client.GetAsync($"{List(Guid.NewGuid(), Guid.NewGuid())}/not-a-guid");
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
    }

    private static async Task<StudySessionFixture> Seed(RegistrationTestFactory factory, PersonalWorkspaceResponse owner, PersonalWorkspaceResponse? other = null)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        return await StudySessionFixture.CreateAsync(db, await db.PersonalWorkspaces.SingleAsync(w => w.Id == owner.Id),
            other is null ? null : await db.PersonalWorkspaces.SingleAsync(w => w.Id == other.Id));
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
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        foreach (var marker in new[] { "SQLite", "Exception", "StudySessions", "AspNetUsers", "WorkspaceId", "OwnerUserId", Password, "Private synthetic failure" })
            Assert.DoesNotContain(marker, body);
        return body;
    }
    private static void Properties(JsonElement value, params string[] names) =>
        Assert.Equal(names.Order(), value.EnumerateObject().Select(p => p.Name).Order());
    private sealed class FailedReader : IStudySessionReader
    {
        public Task<StudySessionPage?> ListCompletedForNodeAsync(Guid workspace, Guid map, Guid node, int offset, int size, CancellationToken ct) =>
            throw new InvalidOperationException("Private synthetic failure.");
        public Task<StudySessionReadModel?> GetForNodeAsync(Guid workspace, Guid map, Guid node, Guid session, CancellationToken ct) =>
            throw new InvalidOperationException("Private synthetic failure.");
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance() => now += TimeSpan.FromMinutes(31);
    }
}

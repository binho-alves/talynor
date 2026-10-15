using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Api.Contracts;
using Talynor.Api.Controllers;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class KnowledgeAuthoringHttpTests
{
    [Fact]
    public void Every_authoring_action_has_native_32_KiB_request_size_configuration()
    {
        using var factory = new RegistrationTestFactory();
        var actions = factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
            .Where(action => action.ControllerTypeInfo.AsType() == typeof(KnowledgeAuthoringController)).ToArray();
        Assert.Equal(8, actions.Length);
        foreach (var action in actions)
        {
            var limit = Assert.Single(action.FilterDescriptors.Select(descriptor => descriptor.Filter)
                .OfType<IRequestSizeLimitMetadata>());
            Assert.Equal(32 * 1024L, limit.MaxRequestBodySize);
        }
        // MVC configuration only; TestServer does not prove Kestrel's oversized-body rejection.
    }

    private const string Password = "synthetic knowledge authoring passphrase";
    private const string MapPath = "/api/maps/10000000-0000-0000-0000-000000000001";
    private const string ResourceId = "20000000-0000-0000-0000-000000000002";
    public static IEnumerable<object[]> Routes()
    {
        yield return new object[] { "POST", "/api/maps", new { title = "Map" } };
        yield return new object[] { "PATCH", MapPath, new { title = "Updated" } };
        yield return new object[] { "DELETE", MapPath, new { } };
        yield return new object[] { "POST", MapPath + "/nodes", new { title = "Node", position = new { x = 1, y = 2 } } };
        yield return new object[] { "PATCH", MapPath + "/nodes/" + ResourceId, new { title = "Updated" } };
        yield return new object[] { "DELETE", MapPath + "/nodes/" + ResourceId, new { } };
        yield return new object[] { "POST", MapPath + "/edges", new { sourceNodeId = Guid.NewGuid(), targetNodeId = Guid.NewGuid() } };
        yield return new object[] { "DELETE", MapPath + "/edges/" + ResourceId, new { } };
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Every_authoring_endpoint_requires_authentication_without_database_creation(string method, string path, object body)
    {
        using var factory = new RegistrationTestFactory(); using var client = factory.NewClient();
        using var response = await SendAsync(client, method, path, body, null);
        await ProblemAsync(response, HttpStatusCode.Unauthorized, "authentication_required");
        using var scope = factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.GetConnectionString();
        Assert.False(File.Exists(new SqliteConnectionStringBuilder(connection).DataSource));
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Every_unsafe_endpoint_rejects_missing_and_invalid_csrf_then_accepts_native_pair(string method, string path, object body)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        await LoginAsync(client, "csrf@example.invalid");
        foreach (var token in new string?[] { null, "not-a-valid-token" })
        {
            using var rejected = await SendAsync(client, method, path, body, token);
            await ProblemAsync(rejected, HttpStatusCode.BadRequest, "csrf_invalid");
        }
        using var accepted = await SendAsync(client, method, path, body, await RegistrationTestFactory.CsrfAsync(client));
        Assert.Equal(path == "/api/maps" ? HttpStatusCode.Created : HttpStatusCode.NotFound, accepted.StatusCode);
        Assert.True(accepted.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Real_authoring_roundtrip_controls_timestamps_patch_semantics_conflicts_and_ordered_deletes()
    {
        var clock = new Clock();
        using var factory = new RegistrationTestFactory(configureServices: services => services.AddSingleton<TimeProvider>(clock));
        await factory.InitializeAsync(); using var client = factory.NewClient(); await LoginAsync(client, "author@example.invalid");
        var map = await CreateMapAsync(client, "Root", "Initial"); var child = await CreateMapAsync(client, "Child");
        Assert.Equal(clock.GetUtcNow(), map.CreatedAtUtc); Assert.Equal(map.CreatedAtUtc, map.UpdatedAtUtc);
        var node = await CreateNodeAsync(client, map.Id, "First", child.Id);
        var second = await CreateNodeAsync(client, map.Id, "Second");
        var edge = await CreateEdgeAsync(client, map.Id, node.Id, second.Id);
        using var childInUse = await WriteAsync(client, "DELETE", $"/api/maps/{child.Id}", new { });
        await ProblemAsync(childInUse, HttpStatusCode.Conflict, "map_in_use");
        using var nodeInUse = await WriteAsync(client, "DELETE", $"/api/maps/{map.Id}/nodes/{node.Id}", new { });
        await ProblemAsync(nodeInUse, HttpStatusCode.Conflict, "node_in_use");
        clock.Advance();
        await NoContentAsync(client, "PATCH", $"/api/maps/{map.Id}", new { title = " Renamed ", clearDescription = true });
        await NoContentAsync(client, "PATCH", $"/api/maps/{map.Id}/nodes/{node.Id}", new { title = "New node", description = "New description", position = new { x = -12.25, y = 99.5 } });
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            var savedMap = await db.KnowledgeMaps.SingleAsync(row => row.Id == map.Id);
            var savedNode = await db.KnowledgeNodes.SingleAsync(row => row.Id == node.Id);
            Assert.Equal("Renamed", savedMap.Title); Assert.Null(savedMap.Description);
            Assert.Equal(map.CreatedAtUtc, savedMap.CreatedAtUtc); Assert.Equal(clock.GetUtcNow(), savedMap.UpdatedAtUtc);
            Assert.Equal(node.CreatedAtUtc, savedNode.CreatedAtUtc); Assert.Equal(clock.GetUtcNow(), savedNode.UpdatedAtUtc);
            Assert.Equal(child.Id, savedNode.ChildMapId); // Omission leaves the link unchanged.
            Assert.Equal(-12.25, savedNode.PositionX); Assert.Equal(99.5, savedNode.PositionY);
        }
        clock.Advance();
        await NoContentAsync(client, "PATCH", $"/api/maps/{map.Id}/nodes/{node.Id}", new { clearChildMap = true, clearDescription = true });
        using (var scope = factory.Services.CreateScope())
        {
            var saved = await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().KnowledgeNodes.SingleAsync(row => row.Id == node.Id);
            Assert.Null(saved.ChildMapId); Assert.Null(saved.Description); Assert.Equal(clock.GetUtcNow(), saved.UpdatedAtUtc);
        }
        await NoContentAsync(client, "PATCH", $"/api/maps/{map.Id}/nodes/{node.Id}", new { childMapId = child.Id });
        await NoContentAsync(client, "PATCH", $"/api/maps/{map.Id}/nodes/{node.Id}", new { clearChildMap = true });
        await NoContentAsync(client, "DELETE", $"/api/maps/{child.Id}", new { });
        await NoContentAsync(client, "DELETE", $"/api/maps/{map.Id}/edges/{edge.Id}", new { });
        await NoContentAsync(client, "DELETE", $"/api/maps/{map.Id}/nodes/{node.Id}", new { });
        await CreateEdgeAsync(client, map.Id, second.Id, second.Id); // Self-loops and exact duplicates stay allowed.
        await CreateEdgeAsync(client, map.Id, second.Id, second.Id);
        await NoContentAsync(client, "DELETE", $"/api/maps/{map.Id}", new { });
        using var finalScope = factory.Services.CreateScope(); var finalDb = finalScope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        Assert.Empty(await finalDb.KnowledgeMaps.ToListAsync()); Assert.Empty(await finalDb.KnowledgeNodes.ToListAsync()); Assert.Empty(await finalDb.KnowledgeEdges.ToListAsync());
        using var anonymous = factory.NewClient();
        foreach (var demo in new[] { "software", "csharp" }) using (var response = await anonymous.GetAsync("/api/knowledge-maps/" + demo)) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("PATCH", "map")]
    [InlineData("DELETE", "map")]
    [InlineData("POST", "nodes")]
    [InlineData("PATCH", "node")]
    [InlineData("DELETE", "node")]
    [InlineData("POST", "edges")]
    [InlineData("DELETE", "edge")]
    public async Task Every_targeted_write_is_scoped_and_foreign_is_indistinguishable_from_missing(string method, string resource)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var a = factory.NewClient(); using var b = factory.NewClient();
        await LoginAsync(a, "a@example.invalid"); await LoginAsync(b, "b@example.invalid");
        var own = await CreateMapAsync(a, "Own"); var foreign = await CreateMapAsync(b, "Foreign");
        var foreignNode = await CreateNodeAsync(b, foreign.Id, "Private node");
        var foreignEdge = await CreateEdgeAsync(b, foreign.Id, foreignNode.Id, foreignNode.Id);
        string Path(Guid map) => resource switch { "map" => $"/api/maps/{map}", "nodes" => $"/api/maps/{map}/nodes", "node" => $"/api/maps/{map}/nodes/{foreignNode.Id}", "edges" => $"/api/maps/{map}/edges", _ => $"/api/maps/{map}/edges/{foreignEdge.Id}" };
        object body = resource switch { "map" => new { title = "Changed" }, "nodes" => new { title = "N", position = new { x = 0, y = 0 } }, "node" => new { title = "Changed" }, _ => new { sourceNodeId = foreignNode.Id, targetNodeId = foreignNode.Id } };
        using var rejected = await WriteAsync(a, method, Path(foreign.Id), body);
        using var missing = await WriteAsync(a, method, Path(Guid.NewGuid()), body);
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode); Assert.True(rejected.Headers.CacheControl?.NoStore);
        Assert.Equal(await rejected.Content.ReadAsStringAsync(), await missing.Content.ReadAsStringAsync());
        if (resource is "node" or "edge")
        {
            using var wrongParent = await WriteAsync(a, method, Path(own.Id), body);
            Assert.Equal(HttpStatusCode.NotFound, wrongParent.StatusCode);
        }
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        Assert.Equal("Foreign", (await db.KnowledgeMaps.SingleAsync(row => row.Id == foreign.Id)).Title);
        Assert.Equal("Private node", (await db.KnowledgeNodes.SingleAsync(row => row.Id == foreignNode.Id)).Title);
        Assert.NotNull(await db.KnowledgeEdges.FindAsync(foreignEdge.Id));
    }

    [Fact]
    public async Task Invalid_child_and_edge_relations_do_not_disclose_foreign_existence_or_save()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var a = factory.NewClient(); using var b = factory.NewClient();
        await LoginAsync(a, "a@example.invalid"); await LoginAsync(b, "b@example.invalid");
        var map = await CreateMapAsync(a, "A"); var otherOwn = await CreateMapAsync(a, "Other own"); var foreign = await CreateMapAsync(b, "B");
        var node = await CreateNodeAsync(a, map.Id, "A"); var cross = await CreateNodeAsync(a, otherOwn.Id, "Cross"); var foreignNode = await CreateNodeAsync(b, foreign.Id, "B");
        foreach (var child in new[] { foreign.Id, Guid.NewGuid() })
        {
            using var create = await WriteAsync(a, "POST", $"/api/maps/{map.Id}/nodes", new { title = "Invalid", position = new { x = 0, y = 0 }, childMapId = child });
            using var update = await WriteAsync(a, "PATCH", $"/api/maps/{map.Id}/nodes/{node.Id}", new { childMapId = child });
            await ProblemAsync(create, HttpStatusCode.BadRequest, "invalid_child_map"); await ProblemAsync(update, HttpStatusCode.BadRequest, "invalid_child_map");
        }
        foreach (var target in new[] { cross.Id, foreignNode.Id, Guid.NewGuid() })
        {
            using var response = await WriteAsync(a, "POST", $"/api/maps/{map.Id}/edges", new { sourceNodeId = node.Id, targetNodeId = target });
            await ProblemAsync(response, HttpStatusCode.BadRequest, "invalid_edge_nodes");
        }
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        Assert.Equal(3, await db.KnowledgeNodes.CountAsync()); Assert.Empty(await db.KnowledgeEdges.ToListAsync());
    }

    [Fact]
    public async Task Client_cannot_assign_ownership_ids_or_timestamps_and_invalid_payloads_are_sanitized()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient(); await LoginAsync(client, "validation@example.invalid");
        foreach (var extra in new[] { "workspaceId", "ownerUserId", "createdAtUtc", "id" })
        {
            using var response = await WriteAsync(client, "POST", "/api/maps", new Dictionary<string, object> { ["title"] = "Map", [extra] = "internal-injected-value" });
            await ProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
            Assert.DoesNotContain("internal-injected-value", await response.Content.ReadAsStringAsync());
        }
        var map = await CreateMapAsync(client, "Valid"); var node = await CreateNodeAsync(client, map.Id, "Valid");
        foreach (var body in new object[] { new { title = " " }, new { title = new string('a', 201) }, new { description = new string('a', 4001) }, new { description = "Ambiguous", clearDescription = true }, new { } })
        {
            using var response = await WriteAsync(client, "PATCH", $"/api/maps/{map.Id}", body);
            await ProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        }
        foreach (var body in new object[] { new { childMapId = Guid.Empty }, new { childMapId = map.Id, clearChildMap = true }, new { position = new { x = 1 } } })
        {
            using var response = await WriteAsync(client, "PATCH", $"/api/maps/{map.Id}/nodes/{node.Id}", body);
            await ProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        }
        using var edge = await WriteAsync(client, "POST", $"/api/maps/{map.Id}/edges", new { sourceNodeId = Guid.Empty, targetNodeId = node.Id });
        await ProblemAsync(edge, HttpStatusCode.BadRequest, "validation_failed");
        using var list = await client.GetAsync("/api/maps"); Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var discovered = (await list.Content.ReadFromJsonAsync<KnowledgeMapListResponse>())!;
        Assert.Equal(map.Id, Assert.Single(discovered.Maps).Id);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(19, 1555)]
    [InlineData(19, 787)]
    public async Task Operational_or_unclassified_constraint_failure_is_500_not_expected_business_error(int code, int extended)
    {
        using var factory = new RegistrationTestFactory(new Fault(code, extended)); await factory.InitializeAsync(); using var client = factory.NewClient(); await LoginAsync(client, "fault@example.invalid");
        using var response = await WriteAsync(client, "POST", "/api/maps", new { title = "Private title" });
        await ProblemAsync(response, HttpStatusCode.InternalServerError, "unexpected_error");
        var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("SQLite", body); Assert.DoesNotContain("Private title", body);
    }

    private static async Task LoginAsync(HttpClient client, string email)
    {
        using var register = await RegistrationTestFactory.RegisterAsync(client, new { email, password = Password }, await RegistrationTestFactory.CsrfAsync(client)); Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        using var login = await SendAsync(client, "POST", "/api/auth/login", new { email, password = Password }, await RegistrationTestFactory.CsrfAsync(client)); Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path, object body, string? token)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method != "DELETE") request.Content = JsonContent.Create(body);
        if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }
    private static async Task<HttpResponseMessage> WriteAsync(HttpClient client, string method, string path, object body) =>
        await SendAsync(client, method, path, body, await RegistrationTestFactory.CsrfAsync(client));
    private static async Task NoContentAsync(HttpClient client, string method, string path, object body)
    { using var response = await WriteAsync(client, method, path, body); Assert.Equal(HttpStatusCode.NoContent, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore); Assert.Empty(await response.Content.ReadAsByteArrayAsync()); }
    private static async Task<KnowledgeMapAuthoringResponse> CreateMapAsync(HttpClient client, string title, string? description = null)
    {
        using var response = await WriteAsync(client, "POST", "/api/maps", new { title, description }); await CreatedAsync(response);
        var map = (await response.Content.ReadFromJsonAsync<KnowledgeMapAuthoringResponse>())!;
        Assert.Equal($"/api/maps/{map.Id:D}", response.Headers.Location?.ToString()); return map;
    }
    private static async Task<KnowledgeNodeAuthoringResponse> CreateNodeAsync(HttpClient client, Guid map, string title, Guid? child = null)
    { using var response = await WriteAsync(client, "POST", $"/api/maps/{map}/nodes", new { title, position = new { x = 1.5, y = 2.25 }, childMapId = child }); await CreatedAsync(response); return (await response.Content.ReadFromJsonAsync<KnowledgeNodeAuthoringResponse>())!; }
    private static async Task<KnowledgeEdgeAuthoringResponse> CreateEdgeAsync(HttpClient client, Guid map, Guid source, Guid target)
    { using var response = await WriteAsync(client, "POST", $"/api/maps/{map}/edges", new { sourceNodeId = source, targetNodeId = target }); await CreatedAsync(response); return (await response.Content.ReadFromJsonAsync<KnowledgeEdgeAuthoringResponse>())!; }
    private static async Task CreatedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = json.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("workspaceId", names); Assert.DoesNotContain("ownerUserId", names);
    }
    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore); Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        foreach (var marker in new[] { "SQLite", "stack", "WorkspaceId", "OwnerUserId", "FOREIGN KEY", "Child map must belong", "Exception" }) Assert.DoesNotContain(marker, await response.Content.ReadAsStringAsync());
    }
    private sealed class Clock : TimeProvider { private DateTimeOffset now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => now; public void Advance() => now += TimeSpan.FromMinutes(1); }
    private sealed class Fault(int code, int extended) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default) =>
            data.Context!.ChangeTracker.Entries<Talynor.Domain.KnowledgeMaps.KnowledgeMap>().Any(entry => entry.State == EntityState.Added)
                ? throw new DbUpdateException("Synthetic fault.", new SqliteException("SQLite provider failure.", code, extended)) : ValueTask.FromResult(result);
    }
}

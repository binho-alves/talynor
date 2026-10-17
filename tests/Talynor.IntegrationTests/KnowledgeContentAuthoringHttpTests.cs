using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Api.Contracts;
using Talynor.Api.Controllers;
using Talynor.Application.KnowledgeContents;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class KnowledgeContentAuthoringHttpTests
{
    private const string Password = "synthetic content authoring passphrase";
    private static string List(Guid map, Guid node) => $"/api/maps/{map:D}/nodes/{node:D}/contents";
    private static string Detail(Guid map, Guid node, Guid content) => $"{List(map, node)}/{content:D}";

    [Fact]
    public void All_three_unsafe_actions_have_32768_byte_metadata_without_claiming_kestrel_enforcement()
    {
        using var factory = new RegistrationTestFactory();
        var actions = factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>().Where(a => a.ControllerTypeInfo.AsType() == typeof(KnowledgeContentAuthoringController)).ToArray();
        Assert.Equal(3, actions.Length);
        foreach (var action in actions)
            Assert.Equal(32768L, Assert.Single(action.FilterDescriptors.Select(f => f.Filter).OfType<IRequestSizeLimitMetadata>()).MaxRequestBodySize);
    }

    [Theory]
    [InlineData("POST")] [InlineData("PATCH")] [InlineData("DELETE")]
    public async Task Every_write_requires_authentication_and_does_not_create_database(string method)
    {
        using var factory = new RegistrationTestFactory(); using var client = factory.NewClient();
        using var response = await Send(client, method, PathFor(method), "{}", null);
        await Problem(response, HttpStatusCode.Unauthorized, "authentication_required");
        using var scope = factory.Services.CreateScope(); var cs = scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.GetConnectionString();
        Assert.False(File.Exists(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(cs).DataSource));
    }

    [Theory]
    [InlineData("POST")] [InlineData("PATCH")] [InlineData("DELETE")]
    public async Task Every_write_rejects_missing_invalid_and_other_client_csrf_then_accepts_native_pair(string method)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        await Login(client, "csrf-content@example.invalid"); using var other = factory.NewClient();
        foreach (var token in new string?[] { null, "invalid", await RegistrationTestFactory.CsrfAsync(other) })
        {
            using var rejected = await Send(client, method, PathFor(method), "{}", token);
            await Problem(rejected, HttpStatusCode.BadRequest, "csrf_invalid");
        }
        using var admitted = await Send(client, method, PathFor(method), method == "POST" ? "{\"type\":\"note\",\"body\":\"body\"}" : "{\"title\":\"Changed\"}", await RegistrationTestFactory.CsrfAsync(client));
        await Problem(admitted, HttpStatusCode.NotFound, "content_not_found");
    }

    [Fact]
    public async Task Note_and_link_create_patch_get_and_delete_preserve_contracts_timestamps_and_no_store()
    {
        var clock = new Clock(); using var factory = new RegistrationTestFactory(configureServices: s => s.AddSingleton<TimeProvider>(clock));
        await factory.InitializeAsync(); using var client = factory.NewClient(); var f = await Prepare(factory, client, "roundtrip-content@example.invalid");
        var path = List(f.Graph.Map.Id, f.Graph.Second.Id);
        var note = await Create(client, path, new { type = "note", title = " Title ", body = "  <b>literal</b>\n# plain text" });
        var link = await Create(client, path, new { type = "link", url = " http://intranet/docs?q=1#part " });
        Shape(note, "id", "type", "title", "createdAtUtc", "updatedAtUtc", "body");
        Shape(link, "id", "type", "title", "createdAtUtc", "updatedAtUtc", "url");
        Assert.Equal("Title", note.GetProperty("title").GetString()); Assert.Equal(JsonValueKind.Null, link.GetProperty("title").ValueKind);
        Assert.Equal("http://intranet/docs?q=1#part", link.GetProperty("url").GetString());
        foreach (var created in new[] { note, link })
        {
            Assert.Equal(clock.GetUtcNow(), created.GetProperty("createdAtUtc").GetDateTimeOffset());
            Assert.Equal(created.GetProperty("createdAtUtc").GetDateTimeOffset(), created.GetProperty("updatedAtUtc").GetDateTimeOffset());
        }
        var notePath = path + "/" + note.GetProperty("id").GetGuid(); var linkPath = path + "/" + link.GetProperty("id").GetGuid();
        clock.Advance();
        await NoContent(client, "PATCH", notePath, new { body = "Changed body", title = (string?)null });
        var changed = await Get(client, notePath);
        Assert.Equal("Title", changed.GetProperty("title").GetString()); Assert.Equal("Changed body", changed.GetProperty("body").GetString());
        Assert.Equal(note.GetProperty("createdAtUtc").GetDateTimeOffset(), changed.GetProperty("createdAtUtc").GetDateTimeOffset());
        Assert.Equal(clock.GetUtcNow(), changed.GetProperty("updatedAtUtc").GetDateTimeOffset());
        await NoContent(client, "PATCH", linkPath, new { title = "Link title", url = (string?)null });
        Assert.Equal("http://intranet/docs?q=1#part", (await Get(client, linkPath)).GetProperty("url").GetString());
        clock.Advance(); await NoContent(client, "PATCH", linkPath, new { url = "https://localhost/new" });
        await NoContent(client, "PATCH", notePath, new { clearTitle = true });
        await NoContent(client, "PATCH", linkPath, new { clearTitle = true });
        Assert.Equal(JsonValueKind.Null, (await Get(client, notePath)).GetProperty("title").ValueKind);
        Assert.Equal("https://localhost/new", (await Get(client, linkPath)).GetProperty("url").GetString());
        var before = await Get(client, notePath); clock.Advance();
        foreach (var payload in new object[] { new { }, new { title = (string?)null }, new { clearTitle = true },
            new { body = "Changed body" }, new { title = "Candidate", body = " " }, new { clearTitle = true, title = "Conflict" },
            new { title = "Candidate", url = (string?)null } })
        {
            using var rejected = await Write(client, "PATCH", notePath, payload);
            await Problem(rejected, HttpStatusCode.BadRequest, "validation_failed");
            Assert.Equal(before.GetRawText(), (await Get(client, notePath)).GetRawText());
        }
        foreach (var payload in new object[] { new { body = (string?)null }, new { title = "Candidate", url = "javascript:alert(1)" } })
        { using var rejected = await Write(client, "PATCH", linkPath, payload); await Problem(rejected, HttpStatusCode.BadRequest, "validation_failed"); }
        var list = await Get(client, path); Shape(list, "contents");
        Assert.Equal(2, list.GetProperty("contents").GetArrayLength());
        Assert.All(list.GetProperty("contents").EnumerateArray(), c => Assert.False(c.TryGetProperty("body", out _)));
        await NoContent(client, "DELETE", notePath, null); await NoContent(client, "DELETE", linkPath, null);
        using var secondDelete = await Write(client, "DELETE", notePath, null); await Problem(secondDelete, HttpStatusCode.NotFound, "content_not_found");
        Assert.Empty((await Get(client, path)).GetProperty("contents").EnumerateArray());
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        Assert.False(await db.KnowledgeContents.AnyAsync(c => c.KnowledgeNodeId == f.Graph.Second.Id));
        Assert.True(await db.KnowledgeContents.AnyAsync(c => c.Id == f.ForeignContent.Id));
    }

    public static IEnumerable<object[]> InvalidCreates()
    {
        yield return ["{}"]; yield return ["{\"type\":null}"]; yield return ["{\"type\":0}"];
        foreach (var type in new[] { "Note", "0", "video", "" }) yield return [JsonSerializer.Serialize(new { type, body = "body" })];
        foreach (var body in new string?[] { null, " ", new('b', 4001) }) yield return [JsonSerializer.Serialize(new { type = "note", body })];
        yield return ["{\"type\":\"note\",\"body\":\"body\",\"url\":null}"];
        yield return ["{\"type\":\"note\",\"body\":\"body\",\"url\":\"http://intranet\"}"];
        foreach (var url in new string?[] { null, "javascript:alert(1)", "http://user:pass@example.invalid", "http://example.invalid\\evil", "http://intranet/" + new string('x', 2048) })
            yield return [JsonSerializer.Serialize(new { type = "link", url })];
        yield return ["{\"type\":\"link\",\"url\":\"http://intranet\",\"body\":null}"];
        yield return ["{\"type\":\"link\",\"url\":\"http://intranet\",\"body\":\"body\"}"];
        yield return [JsonSerializer.Serialize(new { type = "note", title = new string('t', 201), body = "body" })];
    }

    [Theory]
    [MemberData(nameof(InvalidCreates))]
    public async Task Invalid_create_is_sanitized_and_does_not_persist(string json)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var f = await Prepare(factory, client, "invalid-content@example.invalid");
        using var response = await Send(client, "POST", List(f.Graph.Map.Id, f.Graph.Second.Id), json, await RegistrationTestFactory.CsrfAsync(client));
        await Problem(response, HttpStatusCode.BadRequest, "validation_failed");
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().KnowledgeContents.AnyAsync(c => c.KnowledgeNodeId == f.Graph.Second.Id));
    }

    [Fact]
    public async Task Unknown_members_identity_type_and_internal_presence_flags_are_rejected_on_create_and_patch()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var f = await Prepare(factory, client, "unknown-content@example.invalid");
        foreach (var field in new[] { "id", "nodeId", "mapId", "workspaceId", "ownerUserId", "knowledgeNodeId", "createdAtUtc", "updatedAtUtc", "bodySpecified", "unexpected" })
        foreach (var method in new[] { "POST", "PATCH" })
        {
            var values = method == "POST" ? new Dictionary<string, object?> { ["type"] = "note", ["body"] = "body" } : new() { ["title"] = "Changed" };
            values[field] = "not-client-controlled";
            var path = method == "POST" ? List(f.Graph.Map.Id, f.Graph.Second.Id) : Detail(f.Graph.Map.Id, f.Graph.First.Id, f.Note.Id);
            using var response = await Write(client, method, path, values);
            await Problem(response, HttpStatusCode.BadRequest, "validation_failed");
        }
        using var typeAttempt = await Write(client, "PATCH", Detail(f.Graph.Map.Id, f.Graph.First.Id, f.Note.Id), new { type = "link" });
        await Problem(typeAttempt, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(f.Note.Body, (await Get(client, Detail(f.Graph.Map.Id, f.Graph.First.Id, f.Note.Id))).GetProperty("body").GetString());
    }

    [Fact]
    public async Task All_writes_share_not_found_for_foreign_missing_and_mixed_paths_despite_ownership_query_overrides()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var a = factory.NewClient(); using var b = factory.NewClient();
        var f = await Prepare(factory, a, "owner-a-content@example.invalid"); await Login(b, "owner-b-content@example.invalid");
        var lists = new[] { List(f.Graph.OtherMap.Id, f.ForeignNode.Id), List(f.Graph.Map.Id, f.ForeignNode.Id),
            List(f.Graph.OtherMap.Id, f.Graph.First.Id), List(Guid.NewGuid(), f.Graph.First.Id), List(f.Graph.Map.Id, Guid.NewGuid()) };
        var details = new[] { Detail(f.Graph.OtherMap.Id, f.ForeignNode.Id, f.ForeignContent.Id), Detail(f.Graph.Map.Id, f.Graph.Second.Id, f.Note.Id),
            Detail(f.Graph.OtherMap.Id, f.Graph.First.Id, f.Note.Id), Detail(f.Graph.Map.Id, f.Graph.First.Id, f.ForeignContent.Id),
            Detail(f.Graph.Map.Id, f.Graph.First.Id, Guid.NewGuid()), Detail(Guid.Empty, f.Graph.First.Id, f.Note.Id) };
        string? baseline = null;
        foreach (var path in lists)
        {
            using var response = await Write(a, "POST", path + "?workspaceId=" + f.Graph.OtherWorkspace.Id, new { type = "note", body = "body" });
            var text = await Problem(response, HttpStatusCode.NotFound, "content_not_found"); baseline ??= text; Assert.Equal(baseline, text);
        }
        foreach (var path in details)
        foreach (var method in new[] { "PATCH", "DELETE" })
        { using var response = await Write(a, method, path, new { title = "Changed" }); Assert.Equal(baseline, await Problem(response, HttpStatusCode.NotFound, "content_not_found")); }
        foreach (var method in new[] { "POST", "PATCH", "DELETE" })
        {
            var path = method == "POST" ? List(f.Graph.Map.Id, f.Graph.First.Id) : Detail(f.Graph.Map.Id, f.Graph.First.Id, f.Note.Id);
            using var response = await Write(b, method, path, method == "POST"
                ? new { type = "note", body = "body" } : (object)new { title = "Changed" });
            Assert.Equal(baseline, await Problem(response, HttpStatusCode.NotFound, "content_not_found"));
        }
        Assert.Equal(f.Note.Body, (await Get(a, Detail(f.Graph.Map.Id, f.Graph.First.Id, f.Note.Id))).GetProperty("body").GetString());
    }

    [Theory]
    [InlineData("POST")] [InlineData("PATCH")] [InlineData("DELETE")]
    public async Task Provider_failure_is_sanitized_and_removed_account_is_signed_out(string method)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var f = await Prepare(factory, client, "failed-content@example.invalid");
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.ExecuteSqlRawAsync("DROP TABLE KnowledgeContents");
        using var failed = await Write(client, method, method == "POST" ? List(f.Graph.Map.Id, f.Graph.First.Id) : Detail(f.Graph.Map.Id, f.Graph.First.Id, f.Note.Id),
            method == "POST" ? new { type = "note", body = "body" } : (object)new { title = "Changed" });
        await Problem(failed, HttpStatusCode.InternalServerError, "unexpected_error");
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Users.ExecuteDeleteAsync();
        using var removed = await Write(client, method, method == "POST" ? List(f.Graph.Map.Id, f.Graph.First.Id) : Detail(f.Graph.Map.Id, f.Graph.First.Id, f.Note.Id),
            method == "POST" ? new { type = "note", body = "body" } : (object)new { title = "Changed" });
        await Problem(removed, HttpStatusCode.Unauthorized, "authentication_required");
        Assert.Contains(removed.Headers.GetValues("Set-Cookie"), v => v.StartsWith("__Host-Talynor.Auth=;", StringComparison.Ordinal));
    }

    private static string PathFor(string method) => method == "POST" ? List(Guid.NewGuid(), Guid.NewGuid()) : Detail(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private static async Task Login(HttpClient client, string email)
    {
        using var register = await RegistrationTestFactory.RegisterAsync(client, new { email, password = Password }, await RegistrationTestFactory.CsrfAsync(client));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        using var login = await Write(client, "POST", "/api/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }
    private static async Task<KnowledgeContentFixture> Prepare(RegistrationTestFactory factory, HttpClient client, string email)
    {
        await Login(client, email); var workspace = (await client.GetFromJsonAsync<PersonalWorkspaceResponse>("/api/workspace"))!;
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        return await KnowledgeContentFixture.CreateAsync(db, await db.PersonalWorkspaces.SingleAsync(w => w.Id == workspace.Id));
    }
    private static Task<HttpResponseMessage> Write(HttpClient client, string method, string path, object? payload) => SendWithCsrf(client, method, path, payload);
    private static async Task<HttpResponseMessage> SendWithCsrf(HttpClient client, string method, string path, object? payload) =>
        await Send(client, method, path, payload is null ? null : JsonSerializer.Serialize(payload), await RegistrationTestFactory.CsrfAsync(client));
    private static async Task<HttpResponseMessage> Send(HttpClient client, string method, string path, string? json, string? token)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }
    private static async Task<JsonElement> Create(HttpClient client, string path, object payload)
    {
        using var response = await Write(client, "POST", path, payload); Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        var content = (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone(); var location = path + "/" + content.GetProperty("id").GetGuid();
        Assert.Equal(location, response.Headers.Location?.OriginalString); Assert.Equal(content.GetRawText(), (await Get(client, location)).GetRawText());
        return content;
    }
    private static async Task<JsonElement> Get(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }
    private static async Task NoContent(HttpClient client, string method, string path, object? payload)
    { using var response = await Write(client, method, path, payload); Assert.Equal(HttpStatusCode.NoContent, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore); Assert.Equal("", await response.Content.ReadAsStringAsync()); }
    private static void Shape(JsonElement value, params string[] fields) => Assert.Equal(fields.Order(), value.EnumerateObject().Select(p => p.Name).Order());
    private static async Task<string> Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore); Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var raw = await response.Content.ReadAsStringAsync(); using var json = JsonDocument.Parse(raw);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        foreach (var forbidden in new[] { "SQLite", "Exception", "KnowledgeContents", "WorkspaceId", "OwnerUserId", "private literal", Password }) Assert.DoesNotContain(forbidden, raw);
        return raw;
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance() => now = now.AddHours(1);
    }
}

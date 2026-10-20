using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Api.Contracts;
using Talynor.Api.Controllers;
using Talynor.Domain.Flashcards;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class FlashcardHttpTests
{
    private const string Password = "synthetic flashcard test passphrase";
    private static string Collection(Guid map, Guid node) => $"/api/maps/{map:D}/nodes/{node:D}/flashcards";
    private static string Detail(Guid map, Guid node, Guid card) => Collection(map, node) + "/" + card.ToString("D");

    [Theory]
    [InlineData("GET")] [InlineData("DETAIL")] [InlineData("POST")] [InlineData("PATCH")] [InlineData("DELETE")]
    public async Task Every_endpoint_requires_auth_without_creating_a_database(string method)
    {
        using var factory = new RegistrationTestFactory(); using var client = factory.NewClient();
        using var response = await Send(client, method == "DETAIL" ? "GET" : method,
            method is "GET" or "POST" ? Collection(Guid.NewGuid(), Guid.NewGuid()) : Detail(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), "{}", null);
        await Problem(response, HttpStatusCode.Unauthorized, "authentication_required");
        using var scope = factory.Services.CreateScope(); var cs = scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.GetConnectionString();
        Assert.False(File.Exists(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(cs).DataSource));
    }

    [Theory]
    [InlineData("POST")] [InlineData("PATCH")] [InlineData("DELETE")]
    public async Task Unsafe_endpoints_reject_missing_invalid_and_other_cookie_csrf(string method)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        await Login(client); using var other = factory.NewClient();
        var path = method == "POST" ? Collection(Guid.NewGuid(), Guid.NewGuid()) : Detail(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        foreach (var token in new string?[] { null, "invalid", await RegistrationTestFactory.CsrfAsync(other) })
        { using var response = await Send(client, method, path, "{}", token); await Problem(response, HttpStatusCode.BadRequest, "csrf_invalid"); }
        using var admitted = await Write(client, method, path, "{\"front\":\"f\",\"back\":\"b\"}");
        await Problem(admitted, HttpStatusCode.NotFound, "flashcard_not_found");
    }

    [Fact]
    public async Task Crud_preserves_plain_text_partial_updates_revisions_noops_and_response_contracts()
    {
        var clock = new Clock(); using var factory = new RegistrationTestFactory(configureServices: s => s.AddSingleton<TimeProvider>(clock));
        await factory.InitializeAsync(); using var client = factory.NewClient(); var graph = await Prepare(factory, client);
        var path = Collection(graph.Map.Id, graph.Second.Id);
        var empty = await Get(client, path); Shape(empty, "flashcards", "page", "pageSize", "hasMore");
        Assert.Empty(empty.GetProperty("flashcards").EnumerateArray()); Assert.Equal(1, empty.GetProperty("page").GetInt32());
        Assert.Equal(20, empty.GetProperty("pageSize").GetInt32()); Assert.False(empty.GetProperty("hasMore").GetBoolean());
        var created = await Create(client, path, "  # <b>front</b>\r\n", " back\n ");
        Shape(created, "id", "knowledgeNodeId", "front", "back", "contentRevision", "createdAtUtc", "updatedAtUtc");
        Assert.Equal(graph.Second.Id, created.GetProperty("knowledgeNodeId").GetGuid()); Assert.Equal(1, created.GetProperty("contentRevision").GetInt32());
        Assert.Equal("  # <b>front</b>\r\n", created.GetProperty("front").GetString()); Assert.Equal(" back\n ", created.GetProperty("back").GetString());
        Assert.Equal(clock.GetUtcNow(), created.GetProperty("createdAtUtc").GetDateTimeOffset());
        var detail = path + "/" + created.GetProperty("id").GetGuid(); clock.Advance();
        await NoContent(client, "PATCH", detail, "{\"front\":\"changed\"}");
        var changed = await Get(client, detail); Assert.Equal(2, changed.GetProperty("contentRevision").GetInt32());
        Assert.Equal(created.GetProperty("back").GetString(), changed.GetProperty("back").GetString());
        Assert.Equal(created.GetProperty("createdAtUtc").GetString(), changed.GetProperty("createdAtUtc").GetString());
        Assert.Equal(clock.GetUtcNow(), changed.GetProperty("updatedAtUtc").GetDateTimeOffset());
        clock.Advance();
        foreach (var json in new[] { "{}", "{\"front\":\"changed\"}", "{\"back\":null}", "{\"front\":\"candidate\",\"back\":\" \"}" })
        { using var response = await Write(client, "PATCH", detail, json); await Problem(response, HttpStatusCode.BadRequest, "validation_failed");
            Assert.Equal(changed.GetRawText(), (await Get(client, detail)).GetRawText()); }
        await NoContent(client, "PATCH", detail, "{\"back\":\"new back\"}");
        Assert.Equal(3, (await Get(client, detail)).GetProperty("contentRevision").GetInt32());
        await NoContent(client, "PATCH", detail, "{\"front\":\"both\",\"back\":\"both\"}");
        Assert.Equal(4, (await Get(client, detail)).GetProperty("contentRevision").GetInt32());
        await NoContent(client, "DELETE", detail, null);
        using var repeated = await Write(client, "DELETE", detail, null); await Problem(repeated, HttpStatusCode.NotFound, "flashcard_not_found");
        using var missing = await client.GetAsync(detail); await Problem(missing, HttpStatusCode.NotFound, "flashcard_not_found");
        Assert.Empty((await Get(client, path)).GetProperty("flashcards").EnumerateArray());
    }

    public static IEnumerable<object[]> InvalidJson()
    {
        foreach (var json in new[] { "null", "[]", "42", "{", "{\"front\":1,\"back\":\"b\"}", "{\"front\":null,\"back\":\"b\"}",
            "{\"front\":\"f\",\"back\":null}", "{\"front\":\" \n\",\"back\":\"b\"}", "{\"front\":\"f\",\"back\":\"\"}" }) yield return [json];
        yield return [JsonSerializer.Serialize(new { front = new string('f', 2001), back = "b" })];
        yield return [JsonSerializer.Serialize(new { front = "f", back = new string('b', 4001) })];
        yield return [JsonSerializer.Serialize(new { front = " \r\n\t", back = "back" })];
        yield return [JsonSerializer.Serialize(new { front = "front", back = " \r\n\t" })];
    }

    [Fact]
    public async Task Create_requires_both_members_and_never_persists_incomplete_payloads()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var graph = await Prepare(factory, client); var path = Collection(graph.Map.Id, graph.Second.Id);
        foreach (var payload in new[] { "{}", "{\"front\":\"f\"}", "{\"back\":\"b\"}" })
        { using var response = await Write(client, "POST", path, payload); await Problem(response, HttpStatusCode.BadRequest, "validation_failed"); }
        Assert.Empty((await Get(client, path)).GetProperty("flashcards").EnumerateArray());
    }

    [Fact]
    public async Task Overflow_is_sanitized_without_persisted_content_time_or_revision_change()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var graph = await Prepare(factory, client); var path = Collection(graph.Map.Id, graph.Second.Id);
        var card = await Create(client, path, "front", "back"); var id = card.GetProperty("id").GetGuid(); var detail = path + "/" + id;
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Flashcards SET ContentRevision = {int.MaxValue} WHERE Id = {id}");
        var before = await Get(client, detail);
        using var failed = await Write(client, "PATCH", detail, "{\"front\":\"candidate\"}");
        await Problem(failed, HttpStatusCode.InternalServerError, "unexpected_error");
        Assert.Equal(before.GetRawText(), (await Get(client, detail)).GetRawText());
    }

    [Theory]
    [MemberData(nameof(InvalidJson))]
    public async Task Invalid_json_shape_or_fields_are_sanitized_for_create_and_patch(string json)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var graph = await Prepare(factory, client); var path = Collection(graph.Map.Id, graph.Second.Id);
        var created = await Create(client, path, "front", "back"); var detail = path + "/" + created.GetProperty("id").GetGuid();
        foreach (var method in new[] { "POST", "PATCH" })
        { using var response = await Write(client, method, method == "POST" ? path : detail, json); await Problem(response, HttpStatusCode.BadRequest, "validation_failed"); }
        Assert.Equal(created.GetRawText(), (await Get(client, detail)).GetRawText());
        Assert.Single((await Get(client, path)).GetProperty("flashcards").EnumerateArray());
    }

    [Theory]
    [InlineData("id")] [InlineData("knowledgeNodeId")] [InlineData("mapId")] [InlineData("workspaceId")]
    [InlineData("ownerUserId")] [InlineData("contentRevision")] [InlineData("createdAtUtc")] [InlineData("updatedAtUtc")]
    [InlineData("frontSpecified")] [InlineData("type")] [InlineData("unexpected")]
    public async Task Unknown_and_server_owned_members_are_rejected(string field)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var graph = await Prepare(factory, client); var path = Collection(graph.Map.Id, graph.Second.Id);
        var card = await Create(client, path, "front", "back");
        var json = JsonSerializer.Serialize(new Dictionary<string, object?> { ["front"] = "changed", ["back"] = "b", [field] = "client-controlled" });
        foreach (var method in new[] { "POST", "PATCH" })
        { using var response = await Write(client, method, method == "POST" ? path : path + "/" + card.GetProperty("id").GetGuid(), json);
            await Problem(response, HttpStatusCode.BadRequest, "validation_failed"); }
    }

    [Theory]
    [InlineData("page=0")] [InlineData("page=-1")] [InlineData("page=2147483648")]
    [InlineData("page=2147483647&pageSize=100")] [InlineData("page=oops")] [InlineData("pageSize=0")]
    [InlineData("pageSize=-1")] [InlineData("pageSize=101")] [InlineData("pageSize=2147483648")]
    public async Task Invalid_pagination_is_rejected_without_clamping(string query)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var graph = await Prepare(factory, client);
        using var response = await client.GetAsync(Collection(graph.Map.Id, graph.Second.Id) + "?" + query);
        await Problem(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Collection_uses_default_twenty_bounded_pages_and_only_own_cards()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var graph = await Prepare(factory, client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            db.Flashcards.AddRange(Enumerable.Range(0, 21).Select(i => Flashcard.Create(graph.Second.Id, "f" + i, "b", DateTimeOffset.UnixEpoch.AddSeconds(i))));
            await db.SaveChangesAsync();
        }
        var path = Collection(graph.Map.Id, graph.Second.Id); var page = await Get(client, path);
        Assert.Equal(20, page.GetProperty("flashcards").GetArrayLength()); Assert.True(page.GetProperty("hasMore").GetBoolean());
        Assert.Equal("f0", page.GetProperty("flashcards")[0].GetProperty("front").GetString());
        var second = await Get(client, path + "?page=2"); Assert.Single(second.GetProperty("flashcards").EnumerateArray());
        Assert.False(second.GetProperty("hasMore").GetBoolean());
        var all = await Get(client, path + "?pageSize=100"); Assert.Equal(21, all.GetProperty("flashcards").GetArrayLength());
    }

    [Fact]
    public async Task Foreign_missing_and_mixed_paths_share_generic_not_found_for_every_operation()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var g = await Prepare(factory, client); var own = await Create(client, Collection(g.Map.Id, g.Second.Id), "front", "back");
        var card = own.GetProperty("id").GetGuid();
        var lists = new[] { Collection(g.OtherMap.Id, g.Second.Id), Collection(g.Child.Id, g.Second.Id),
            Collection(g.Map.Id, g.ChildNode.Id), Collection(Guid.NewGuid(), g.Second.Id), Collection(g.Map.Id, Guid.NewGuid()), Collection(g.Map.Id, Guid.Empty) };
        string? baseline = null;
        foreach (var path in lists)
        foreach (var method in new[] { "GET", "POST" })
        { using var response = method == "GET" ? await client.GetAsync(path) : await Write(client, method, path, "{\"front\":\"f\",\"back\":\"b\"}");
            var text = await Problem(response, HttpStatusCode.NotFound, "flashcard_not_found"); baseline ??= text; Assert.Equal(baseline, text); }
        var details = lists.Select(p => p + "/" + card).Append(Detail(g.Map.Id, g.First.Id, card)).Append(Detail(g.Map.Id, g.Second.Id, Guid.NewGuid()));
        foreach (var path in details)
        foreach (var method in new[] { "GET", "PATCH", "DELETE" })
        { using var response = method == "GET" ? await client.GetAsync(path + "?workspaceId=" + g.Workspace.Id) : await Write(client, method, path, "{\"front\":\"changed\"}");
            Assert.Equal(baseline, await Problem(response, HttpStatusCode.NotFound, "flashcard_not_found")); }
        using var foreign = factory.NewClient(); await Login(foreign);
        foreach (var method in new[] { "GET", "PATCH", "DELETE" })
        { var path = Detail(g.Map.Id, g.Second.Id, card); using var response = method == "GET" ? await foreign.GetAsync(path) : await Write(foreign, method, path, "{\"front\":\"changed\"}");
            Assert.Equal(baseline, await Problem(response, HttpStatusCode.NotFound, "flashcard_not_found")); }
        Assert.Equal(own.GetRawText(), (await Get(client, Detail(g.Map.Id, g.Second.Id, card))).GetRawText());
    }

    [Fact]
    public async Task Native_metadata_covers_only_post_and_patch_at_65536_bytes()
    {
        using var factory = new RegistrationTestFactory();
        var actions = factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>().Where(a => a.ControllerTypeInfo.AsType() == typeof(FlashcardsController)).ToArray();
        Assert.Equal(5, actions.Length);
        foreach (var action in actions)
        {
            var limits = action.FilterDescriptors.Select(f => f.Filter).OfType<IRequestSizeLimitMetadata>().ToArray();
            if (action.ActionName is "Create" or "Update") Assert.Equal(65536L, Assert.Single(limits).MaxRequestBodySize);
            else Assert.Empty(limits);
        }
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Body_cap_checks_actual_stream_bytes_before_parsing_including_unknown_length(bool unknownLength)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var graph = await Prepare(factory, client); await AssertBodyBounds(client, Collection(graph.Map.Id, graph.Second.Id), unknownLength);
    }

    [Fact]
    public async Task Real_kestrel_rejects_oversize_fixed_and_chunked_bodies_with_pinned_ephemeral_https()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        // Reimport for Windows Schannel: it cannot use every ephemeral RSA key handle.
        // No PersistKeySet or certificate-store install; disposal releases the temporary key.
        using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pkcs12), null);
        using var factory = new RegistrationTestFactory();
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listener => listener.UseHttps(certificate)));
        await factory.InitializeAsync();
        var address = Assert.Single(factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
        // Test-only certificate pinned to this in-memory host; no trust-store or production TLS change.
        using var handler = new HttpClientHandler { AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert?.GetCertHashString() == certificate.GetCertHashString() };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(address) };
        var graph = await Prepare(factory, client);
        await AssertBodyBounds(client, Collection(graph.Map.Id, graph.Second.Id), false, nativeFraming: true);
        await AssertBodyBounds(client, Collection(graph.Map.Id, graph.Second.Id), true, nativeFraming: true);
    }

    [Fact]
    public async Task Removed_account_and_provider_failure_keep_existing_sanitized_security_boundaries()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var graph = await Prepare(factory, client); var path = Collection(graph.Map.Id, graph.Second.Id);
        var created = await Create(client, path, "front", "back"); var detail = path + "/" + created.GetProperty("id").GetGuid();
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.ExecuteSqlRawAsync("DROP TABLE Flashcards");
        foreach (var method in new[] { "GET", "POST", "PATCH", "DELETE" })
        { using var failed = method == "GET" ? await client.GetAsync(path) : await Write(client, method, method == "POST" ? path : detail, "{\"front\":\"f\",\"back\":\"b\"}");
            await Problem(failed, HttpStatusCode.InternalServerError, "unexpected_error"); }
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Users.ExecuteDeleteAsync();
        using var removed = await client.GetAsync(path); await Problem(removed, HttpStatusCode.Unauthorized, "authentication_required");
        Assert.Contains(removed.Headers.GetValues("Set-Cookie"), value => value.StartsWith("__Host-Talynor.Auth=;", StringComparison.Ordinal));
    }

    private static async Task AssertBodyBounds(HttpClient client, string path, bool unknownLength, bool nativeFraming = false)
    {
        var maximum = "{\"front\":\"" + string.Concat(Enumerable.Repeat("\\u0066", 2000)) + "\",\"back\":\"" + string.Concat(Enumerable.Repeat("\\u0062", 4000)) + "\"}";
        // Kestrel also counts HTTP/1 chunk framing against its native limit. Keep
        // framing headroom for chunked acceptance, without raising the configured cap.
        var acceptedBytes = nativeFraming && unknownLength ? 65500 : 65536;
        var atLimit = maximum.PadRight(acceptedBytes);
        using var accepted = await Send(client, "POST", path, atLimit, await RegistrationTestFactory.CsrfAsync(client), unknownLength);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var card = await accepted.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(2000, card.GetProperty("front").GetString()!.Length);
        Assert.Equal(4000, card.GetProperty("back").GetString()!.Length);
        var detail = path + "/" + card.GetProperty("id").GetGuid();
        foreach (var method in new[] { "POST", "PATCH" })
        {
            using var oversized = await Send(client, method, method == "POST" ? path : detail, maximum.PadRight(65537), await RegistrationTestFactory.CsrfAsync(client), unknownLength);
            await Problem(oversized, HttpStatusCode.RequestEntityTooLarge, "validation_failed");
        }
        using var validPatch = await Send(client, "PATCH", detail, "{\"front\":\"changed\"}".PadRight(acceptedBytes), await RegistrationTestFactory.CsrfAsync(client), unknownLength);
        Assert.Equal(HttpStatusCode.NoContent, validPatch.StatusCode);
        using var invalidField = await Send(client, "PATCH", detail, JsonSerializer.Serialize(new { front = new string('f', 2001) }), await RegistrationTestFactory.CsrfAsync(client), unknownLength);
        await Problem(invalidField, HttpStatusCode.BadRequest, "validation_failed");
    }

    private static async Task<KnowledgeGraphFixture> Prepare(RegistrationTestFactory factory, HttpClient client)
    {
        await Login(client); var workspace = (await client.GetFromJsonAsync<PersonalWorkspaceResponse>("/api/workspace"))!;
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        return await KnowledgeGraphFixture.CreateAsync(db, await db.PersonalWorkspaces.SingleAsync(w => w.Id == workspace.Id));
    }
    private static async Task Login(HttpClient client)
    {
        var email = $"flashcard-{Guid.NewGuid():N}@example.invalid";
        using var registered = await RegistrationTestFactory.RegisterAsync(client, new { email, password = Password }, await RegistrationTestFactory.CsrfAsync(client));
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        using var login = await Write(client, "POST", "/api/auth/login", JsonSerializer.Serialize(new { email, password = Password }));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }
    private static async Task<JsonElement> Create(HttpClient client, string path, string front, string back)
    {
        using var response = await Write(client, "POST", path, JsonSerializer.Serialize(new { front, back }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        var card = await response.Content.ReadFromJsonAsync<JsonElement>();
        var detail = path + "/" + card.GetProperty("id").GetGuid(); Assert.Equal(detail, response.Headers.Location?.OriginalString);
        Assert.Equal(card.GetRawText(), (await Get(client, detail)).GetRawText()); return card;
    }
    private static async Task<JsonElement> Get(HttpClient client, string path)
    { using var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    private static async Task NoContent(HttpClient client, string method, string path, string? json)
    { using var response = await Write(client, method, path, json); Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore); Assert.Equal("", await response.Content.ReadAsStringAsync()); }
    private static async Task<HttpResponseMessage> Write(HttpClient client, string method, string path, string? json) =>
        await Send(client, method, path, json, await RegistrationTestFactory.CsrfAsync(client));
    private static async Task<HttpResponseMessage> Send(HttpClient client, string method, string path, string? json, string? token, bool unknownLength = false)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (json is not null) request.Content = unknownLength ? new UnknownLengthContent(json) : new StringContent(json, Encoding.UTF8, "application/json");
        if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }
    private static void Shape(JsonElement value, params string[] fields) => Assert.Equal(fields.Order(), value.EnumerateObject().Select(p => p.Name).Order());
    private static async Task<string> Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync(); using var json = JsonDocument.Parse(text);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString()); Assert.Null(response.Headers.Location);
        foreach (var secret in new[] { "SQLite", "Exception", "WorkspaceId", "OwnerUserId", Password }) Assert.DoesNotContain(secret, text);
        return text;
    }
    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] bytes;
        public UnknownLengthContent(string text) { bytes = Encoding.UTF8.GetBytes(text); Headers.ContentType = new("application/json"); }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UnixEpoch.AddDays(100);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance() => now = now.AddMinutes(1);
    }
}

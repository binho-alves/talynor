using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Api.Contracts;
using Talynor.Api.Controllers;
using Talynor.Application.Quizzes;
using Talynor.Domain.Quizzes;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class QuizHttpTests
{
    private const string Password = "synthetic quiz test passphrase";
    private const int Limit = 262144;
    private static string Collection(Guid map, Guid node) => $"/api/maps/{map:D}/nodes/{node:D}/quizzes";
    private static string Body(string title = "title", int? revision = null) => revision is null
        ? JsonSerializer.Serialize(new { title, questions = new[] { new { text = " question\r\n", options = new[] { " first ", "second" }, correctOptionIndex = 1 } } })
        : JsonSerializer.Serialize(new { expectedRevision = revision, title, questions = new[] { new { text = " question\r\n", options = new[] { " first ", "second" }, correctOptionIndex = 1 } } });

    [Theory]
    [InlineData("LIST")] [InlineData("GET")] [InlineData("TAKE")] [InlineData("POST")] [InlineData("PUT")] [InlineData("DELETE")]
    public async Task Every_endpoint_requires_authentication_without_database_creation(string operation)
    {
        using var factory = new RegistrationTestFactory(); using var client = factory.NewClient();
        var path = Collection(Guid.NewGuid(), Guid.NewGuid());
        using var response = await Send(client, operation is "LIST" or "GET" or "TAKE" ? "GET" : operation,
            operation is "LIST" or "POST" ? path : path + "/" + Guid.NewGuid() + (operation == "TAKE" ? "/take" : ""), null, null);
        await Problem(response, HttpStatusCode.Unauthorized, "authentication_required");
        using var scope = factory.Services.CreateScope();
        var cs = scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.GetConnectionString();
        Assert.False(File.Exists(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(cs).DataSource));
    }

    [Theory]
    [InlineData("POST")] [InlineData("PUT")] [InlineData("DELETE")]
    public async Task Every_write_requires_valid_session_bound_csrf(string method)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        await Login(client); using var other = factory.NewClient();
        var path = Collection(Guid.NewGuid(), Guid.NewGuid()) + (method == "POST" ? "" : "/" + Guid.NewGuid());
        foreach (var token in new string?[] { null, "invalid", await RegistrationTestFactory.CsrfAsync(other) })
        { using var response = await Send(client, method, path, method == "DELETE" ? null : Body(revision: method == "PUT" ? 1 : null), token);
            await Problem(response, HttpStatusCode.BadRequest, "csrf_invalid"); }
        using var admitted = await Write(client, method, path, method == "DELETE" ? null : Body(revision: method == "PUT" ? 1 : null));
        await Problem(admitted, HttpStatusCode.NotFound, "quiz_not_found");
    }

    [Fact]
    public async Task Crud_full_replace_noop_stale_and_take_have_exact_owned_contracts()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var g = await Prepare(factory, client); var path = Collection(g.Map.Id, g.Second.Id);
        var empty = await Get(client, path); Shape(empty, "quizzes", "page", "pageSize", "hasMore");
        Assert.Empty(empty.GetProperty("quizzes").EnumerateArray()); Assert.Equal(1, empty.GetProperty("page").GetInt32()); Assert.Equal(20, empty.GetProperty("pageSize").GetInt32());
        var created = await Create(client, path, Body(" title ")); var id = created.GetProperty("id").GetGuid(); var detail = path + "/" + id;
        Shape(created, "id", "knowledgeNodeId", "title", "contentRevision", "createdAtUtc", "updatedAtUtc", "questions");
        Assert.Equal("title", created.GetProperty("title").GetString()); Assert.Equal(g.Second.Id, created.GetProperty("knowledgeNodeId").GetGuid());
        var question = created.GetProperty("questions")[0]; Shape(question, "id", "text", "position", "options", "correctOptionIndex");
        Assert.Equal(" question\r\n", question.GetProperty("text").GetString()); Assert.Equal(1, question.GetProperty("correctOptionIndex").GetInt32());
        Shape(question.GetProperty("options")[0], "id", "text", "position");
        var summary = (await Get(client, path)).GetProperty("quizzes")[0]; Shape(summary, "id", "title", "contentRevision", "createdAtUtc", "updatedAtUtc");
        var take = await Get(client, detail + "/take"); AssertTakeShape(take); Assert.Equal(id, take.GetProperty("quizId").GetGuid());
        Assert.Equal(question.GetProperty("id").GetGuid(), take.GetProperty("questions")[0].GetProperty("id").GetGuid());
        using (var noop = await Write(client, "PUT", detail, Body(" title ", 1))) await Problem(noop, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(created.GetRawText(), (await Get(client, detail)).GetRawText());
        await NoContent(client, "PUT", detail, Body("changed", 1)); var changed = await Get(client, detail);
        Assert.Equal(2, changed.GetProperty("contentRevision").GetInt32()); Assert.Equal("changed", changed.GetProperty("title").GetString());
        Assert.Equal(created.GetProperty("createdAtUtc").GetString(), changed.GetProperty("createdAtUtc").GetString());
        Assert.NotEqual(question.GetProperty("id").GetGuid(), changed.GetProperty("questions")[0].GetProperty("id").GetGuid());
        using (var stale = await Write(client, "PUT", detail, Body("losing", 1))) await Problem(stale, HttpStatusCode.Conflict, "quiz_changed");
        Assert.Equal(changed.GetRawText(), (await Get(client, detail)).GetRawText()); AssertTakeShape(await Get(client, detail + "/take"));
        await NoContent(client, "DELETE", detail, null);
        foreach (var suffix in new[] { "", "/take" })
        { using var missing = await client.GetAsync(detail + suffix); await Problem(missing, HttpStatusCode.NotFound, "quiz_not_found"); }
        using var repeat = await Write(client, "DELETE", detail, null); await Problem(repeat, HttpStatusCode.NotFound, "quiz_not_found");
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        Assert.Empty(await db.QuizQuestions.ToArrayAsync()); Assert.Empty(await db.QuizOptions.ToArrayAsync());
    }

    public static IEnumerable<object[]> InvalidJson()
    {
        foreach (var raw in new[] { "null", "[]", "42", "{", "{}", "{\"title\":\"t\"}", "{\"title\":null,\"questions\":[]}",
            "{\"title\":\"t\",\"questions\":null}", "{\"title\":\"t\",\"questions\":[null]}" }) yield return [raw];
        foreach (var questions in new object?[] { Array.Empty<object>(), new object?[] { new { text = "q", options = new[] { "a", "b" } } },
            new[] { new { text = "q", options = new[] { "a", "b" }, correctOptionIndex = (int?)null } },
            new[] { new { text = "q", options = new[] { "a", "b" }, correctOptionIndex = -1 } },
            new[] { new { text = "q", options = new[] { "a", "b" }, correctOptionIndex = 2 } },
            new[] { new { text = "q", options = new string?[] { null, "b" }, correctOptionIndex = 0 } },
            new[] { new { text = "q", options = (string[]?)null, correctOptionIndex = 0 } },
            new[] { new { text = "q", options = new[] { " a\n", "a" }, correctOptionIndex = 0 } },
            new[] { new { text = " ", options = new[] { "a", "b" }, correctOptionIndex = 0 } },
            new[] { new { text = "q", options = new[] { "a" }, correctOptionIndex = 0 } },
            new[] { new { text = "q", options = new[] { "a", "b", "c", "d", "e" }, correctOptionIndex = 0 } } })
            yield return [JsonSerializer.Serialize(new { title = "t", questions })];
        yield return [JsonSerializer.Serialize(new { title = new string('t', 201), questions = new[] { new { text = "q", options = new[] { "a", "b" }, correctOptionIndex = 0 } } })];
        yield return [JsonSerializer.Serialize(new { title = "t", questions = new[] { new { text = new string('q', 1001), options = new[] { "a", "b" }, correctOptionIndex = 0 } } })];
        yield return [JsonSerializer.Serialize(new { title = "t", questions = new[] { new { text = "q", options = new[] { new string('a', 501), "b" }, correctOptionIndex = 0 } } })];
        yield return [JsonSerializer.Serialize(new { title = "t", questions = Enumerable.Range(0, 11).Select(i => new { text = "q" + i, options = new[] { "a", "b" }, correctOptionIndex = 0 }) })];
    }

    [Theory]
    [MemberData(nameof(InvalidJson))]
    public async Task Invalid_json_or_definition_is_sanitized_and_cannot_partially_write(string json)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var g = await Prepare(factory, client); var path = Collection(g.Map.Id, g.Second.Id); var quiz = await Create(client, path, Body());
        var detail = path + "/" + quiz.GetProperty("id").GetGuid();
        using var create = await Write(client, "POST", path, json); await Problem(create, HttpStatusCode.BadRequest, "validation_failed");
        var replacement = json.StartsWith('{') && json.EndsWith('}') ? "{\"expectedRevision\":1," + json[1..] : json;
        using var replace = await Write(client, "PUT", detail, replacement); await Problem(replace, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(quiz.GetRawText(), (await Get(client, detail)).GetRawText());
        Assert.Single((await Get(client, path)).GetProperty("quizzes").EnumerateArray());
    }

    [Theory]
    [InlineData("ownerUserId")] [InlineData("workspaceId")] [InlineData("knowledgeNodeId")] [InlineData("contentRevision")]
    [InlineData("id")] [InlineData("createdAtUtc")] [InlineData("isCorrect")] [InlineData("unexpected")]
    public async Task Server_owned_and_unknown_fields_are_rejected_at_every_object_level(string field)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var g = await Prepare(factory, client); var path = Collection(g.Map.Id, g.Second.Id); var quiz = await Create(client, path, Body());
        var detail = path + "/" + quiz.GetProperty("id").GetGuid();
        foreach (var nested in new[] { false, true })
        {
            var payload = nested ? Body().Replace("\"text\":", $"\"{field}\":\"private\",\"text\":") : "{\"" + field + "\":\"private\"," + Body()[1..];
            using var create = await Write(client, "POST", path, payload); await Problem(create, HttpStatusCode.BadRequest, "validation_failed");
            using var replace = await Write(client, "PUT", detail, "{\"expectedRevision\":1," + payload[1..]); await Problem(replace, HttpStatusCode.BadRequest, "validation_failed");
        }
    }

    [Theory]
    [InlineData("missing")] [InlineData("null")] [InlineData("0")] [InlineData("-1")]
    [InlineData("2147483648")] [InlineData("\"1\"")] [InlineData("1.5")]
    public async Task Put_requires_positive_strict_int32_expected_revision(string revision)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var g = await Prepare(factory, client); var path = Collection(g.Map.Id, g.Second.Id); var quiz = await Create(client, path, Body());
        var json = revision == "missing" ? Body("new") : "{\"expectedRevision\":" + revision + "," + Body("new")[1..];
        using var response = await Write(client, "PUT", path + "/" + quiz.GetProperty("id").GetGuid(), json);
        await Problem(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Theory]
    [InlineData("page=0")] [InlineData("page=-1")] [InlineData("page=2147483648")] [InlineData("page=2147483647&pageSize=100")]
    [InlineData("page=oops")] [InlineData("pageSize=0")] [InlineData("pageSize=101")] [InlineData("pageSize=2147483648")]
    [InlineData("page=1&page=2")] [InlineData("pageSize=20&pageSize=20")] [InlineData("page=")]
    public async Task Pagination_rejects_invalid_duplicate_and_overflow_without_clamping(string query)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var g = await Prepare(factory, client); using var response = await client.GetAsync(Collection(g.Map.Id, g.Second.Id) + "?" + query);
        await Problem(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Pagination_defaults_has_more_and_non_creating_empty_reads()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var g = await Prepare(factory, client); var path = Collection(g.Map.Id, g.Second.Id);
        Assert.Empty((await Get(client, path)).GetProperty("quizzes").EnumerateArray());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); Assert.Empty(await db.Quizzes.ToArrayAsync());
            var input = QuizPersistenceTests.Definition();
            db.Quizzes.AddRange(Enumerable.Range(0, 21).Select(i => Quiz.Create(g.Second.Id, "q" + i, input.Questions, DateTimeOffset.UnixEpoch.AddSeconds(i))));
            await db.SaveChangesAsync();
        }
        var page = await Get(client, path); Assert.Equal(20, page.GetProperty("quizzes").GetArrayLength()); Assert.True(page.GetProperty("hasMore").GetBoolean());
        Assert.Equal("q0", page.GetProperty("quizzes")[0].GetProperty("title").GetString());
        var next = await Get(client, path + "?page=2"); Assert.Single(next.GetProperty("quizzes").EnumerateArray()); Assert.False(next.GetProperty("hasMore").GetBoolean());
        Assert.Equal(21, (await Get(client, path + "?pageSize=100")).GetProperty("quizzes").GetArrayLength());
    }

    [Fact]
    public async Task Foreign_missing_and_mixed_paths_are_indistinguishable_for_all_six_endpoints()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var g = await Prepare(factory, client); var ownPath = Collection(g.Map.Id, g.Second.Id); var quiz = await Create(client, ownPath, Body());
        var id = quiz.GetProperty("id").GetGuid(); string? baseline = null;
        var paths = new[] { Collection(g.OtherMap.Id, g.Second.Id), Collection(g.Map.Id, g.ChildNode.Id), Collection(g.Child.Id, g.Second.Id),
            Collection(Guid.NewGuid(), g.Second.Id), Collection(g.Map.Id, Guid.NewGuid()), Collection(g.Map.Id, Guid.Empty) };
        foreach (var path in paths)
        {
            using var list = await client.GetAsync(path);
            var problem = await Problem(list, HttpStatusCode.NotFound, "quiz_not_found");
            baseline ??= problem; Assert.Equal(baseline, problem);
            using var create = await Write(client, "POST", path, Body()); Assert.Equal(baseline, await Problem(create, HttpStatusCode.NotFound, "quiz_not_found"));
        }
        foreach (var path in paths.Select(p => p + "/" + id).Append(Collection(g.Map.Id, g.First.Id) + "/" + id)
            .Append(ownPath + "/" + Guid.NewGuid()).Append(ownPath + "/" + Guid.Empty))
            await MissingAll(client, path, baseline!);
        using var foreign = factory.NewClient(); await Login(foreign);
        using (var list = await foreign.GetAsync(ownPath)) Assert.Equal(baseline, await Problem(list, HttpStatusCode.NotFound, "quiz_not_found"));
        using (var create = await Write(foreign, "POST", ownPath, Body())) Assert.Equal(baseline, await Problem(create, HttpStatusCode.NotFound, "quiz_not_found"));
        await MissingAll(foreign, ownPath + "/" + id, baseline!);
        Assert.Equal(quiz.GetRawText(), (await Get(client, ownPath + "/" + id)).GetRawText());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Delete_rejects_actual_body_including_unknown_length(bool unknownLength)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var g = await Prepare(factory, client); var path = Collection(g.Map.Id, g.Second.Id); var quiz = await Create(client, path, Body());
        using var response = await Send(client, "DELETE", path + "/" + quiz.GetProperty("id").GetGuid(), " ", await RegistrationTestFactory.CsrfAsync(client), unknownLength);
        await Problem(response, HttpStatusCode.BadRequest, "validation_failed"); Assert.Single((await Get(client, path)).GetProperty("quizzes").EnumerateArray());
    }

    [Fact]
    public async Task Take_types_and_json_whitelist_exclude_all_answer_key_properties()
    {
        foreach (var type in new[] { typeof(QuizTakeDefinition), typeof(QuizTakeQuestion), typeof(QuizTakeOption),
            typeof(QuizTakeResponse), typeof(QuizTakeQuestionResponse), typeof(QuizTakeOptionResponse) })
        {
            var names = type.GetProperties().Select(p => p.Name.ToLowerInvariant()).ToArray();
            Assert.DoesNotContain("iscorrect", names); Assert.DoesNotContain("correctoptionindex", names); Assert.DoesNotContain("correctoptionid", names);
        }
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var g = await Prepare(factory, client); var path = Collection(g.Map.Id, g.Second.Id); var created = await Create(client, path, Body());
        AssertTakeShape(await Get(client, path + "/" + created.GetProperty("id").GetGuid() + "/take"));
        var actions = factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
            .Where(a => a.ControllerTypeInfo.AsType() == typeof(QuizzesController)).ToArray(); Assert.Equal(6, actions.Length);
        foreach (var action in actions)
        {
            var limits = action.FilterDescriptors.Select(f => f.Filter).OfType<IRequestSizeLimitMetadata>().ToArray();
            if (action.ActionName is "Create" or "Replace") Assert.Equal((long)Limit, Assert.Single(limits).MaxRequestBodySize); else Assert.Empty(limits);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TestServer_bounds_actual_stream_before_json_even_without_content_length(bool unknownLength)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var g = await Prepare(factory, client); await Bounds(client, Collection(g.Map.Id, g.Second.Id), unknownLength, false);
    }

    [Fact]
    public async Task Real_kestrel_pinned_https_accepts_max_definition_and_rejects_fixed_and_chunked_oversize()
    {
        using var key = RSA.Create(2048); var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback); request.CertificateExtensions.Add(names.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pkcs12), null);
        using var factory = new RegistrationTestFactory(); factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listener => listener.UseHttps(certificate)));
        await factory.InitializeAsync(); var address = Assert.Single(factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert?.GetCertHashString() == certificate.GetCertHashString() };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(address) }; var g = await Prepare(factory, client);
        await Bounds(client, Collection(g.Map.Id, g.Second.Id), false, true); await Bounds(client, Collection(g.Map.Id, g.Second.Id), true, true);
    }

    [Fact]
    public async Task Overflow_removed_account_and_provider_failures_are_sanitized_not_missing()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var g = await Prepare(factory, client); var path = Collection(g.Map.Id, g.Second.Id); var quiz = await Create(client, path, Body());
        var id = quiz.GetProperty("id").GetGuid(); var detail = path + "/" + id;
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.ExecuteSqlInterpolatedAsync($"UPDATE Quizzes SET ContentRevision={int.MaxValue} WHERE Id={id}");
        var before = await Get(client, detail);
        using (var overflow = await Write(client, "PUT", detail, Body("overflow", int.MaxValue))) await Problem(overflow, HttpStatusCode.InternalServerError, "unexpected_error");
        Assert.Equal(before.GetRawText(), (await Get(client, detail)).GetRawText());
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.ExecuteSqlRawAsync("DROP TABLE Quizzes");
        foreach (var method in new[] { "GET", "TAKE", "POST", "PUT", "DELETE" })
        { using var failed = method is "GET" or "TAKE" ? await client.GetAsync(method == "GET" ? path : detail + "/take") : await Write(client, method, method == "POST" ? path : detail, method == "DELETE" ? null : Body(revision: method == "PUT" ? 1 : null));
            await Problem(failed, HttpStatusCode.InternalServerError, "unexpected_error"); }
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Users.ExecuteDeleteAsync();
        using var removed = await client.GetAsync(path); await Problem(removed, HttpStatusCode.Unauthorized, "authentication_required");
    }

    private static async Task Bounds(HttpClient client, string path, bool unknownLength, bool nativeFraming)
    {
        string Maximum(int? revision = null) => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["title"] = new string('漢', 200),
            ["questions"] = Enumerable.Range(0, 10).Select(_ => new { text = new string('漢', 1000),
                options = Enumerable.Range(0, 4).Select(i => new string('漢', 499) + (char)('a' + i)).ToArray(), correctOptionIndex = 3 }).ToArray()
        }.Concat(revision is null ? [] : new Dictionary<string, object?> { ["expectedRevision"] = revision })
            .ToDictionary(p => p.Key, p => p.Value));
        var acceptedBytes = nativeFraming && unknownLength ? Limit - 8192 : Limit;
        var max = Maximum(); Assert.True(Encoding.UTF8.GetByteCount(max) < Limit);
        using var create = await Send(client, "POST", path, max.PadRight(acceptedBytes), await RegistrationTestFactory.CsrfAsync(client), unknownLength);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode); var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10, created.GetProperty("questions").GetArrayLength()); var detail = path + "/" + created.GetProperty("id").GetGuid();
        foreach (var method in new[] { "POST", "PUT" })
        { using var over = await Send(client, method, method == "POST" ? path : detail, (method == "POST" ? max : Maximum(1)).PadRight(Limit + 1), await RegistrationTestFactory.CsrfAsync(client), unknownLength);
            await Problem(over, HttpStatusCode.RequestEntityTooLarge, "validation_failed"); }
        var replacement = Maximum(1);
        // The changed correct choice is material without exceeding any maximum text bound.
        replacement = replacement.Replace("\"correctOptionIndex\":3", "\"correctOptionIndex\":2");
        using var put = await Send(client, "PUT", detail, replacement.PadRight(acceptedBytes), await RegistrationTestFactory.CsrfAsync(client), unknownLength);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode); AssertTakeShape(await Get(client, detail + "/take"));
    }

    private static async Task MissingAll(HttpClient client, string path, string baseline)
    {
        foreach (var method in new[] { "GET", "TAKE", "PUT", "DELETE" })
        { using var response = method is "GET" or "TAKE" ? await client.GetAsync(path + (method == "TAKE" ? "/take" : ""))
                : await Write(client, method, path, method == "DELETE" ? null : Body("candidate", 1));
            Assert.Equal(baseline, await Problem(response, HttpStatusCode.NotFound, "quiz_not_found")); }
    }
    private static void AssertTakeShape(JsonElement take)
    {
        Shape(take, "quizId", "title", "contentRevision", "questions");
        foreach (var q in take.GetProperty("questions").EnumerateArray())
        { Shape(q, "id", "text", "position", "options"); foreach (var option in q.GetProperty("options").EnumerateArray()) Shape(option, "id", "text", "position"); }
    }
    private static void Shape(JsonElement value, params string[] fields) => Assert.Equal(fields.Order(), value.EnumerateObject().Select(p => p.Name).Order());
    private static async Task<JsonElement> Create(HttpClient client, string path, string json)
    {
        using var response = await Write(client, "POST", path, json); Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        var value = await response.Content.ReadFromJsonAsync<JsonElement>(); var detail = path + "/" + value.GetProperty("id").GetGuid();
        Assert.Equal(detail, response.Headers.Location?.OriginalString); Assert.Equal(value.GetRawText(), (await Get(client, detail)).GetRawText()); return value;
    }
    private static async Task<JsonElement> Get(HttpClient client, string path)
    { using var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    private static async Task NoContent(HttpClient client, string method, string path, string? json)
    { using var response = await Write(client, method, path, json); Assert.Equal(HttpStatusCode.NoContent, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore); }
    private static async Task<KnowledgeGraphFixture> Prepare(RegistrationTestFactory factory, HttpClient client)
    {
        await Login(client); var workspace = (await client.GetFromJsonAsync<PersonalWorkspaceResponse>("/api/workspace"))!;
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        return await KnowledgeGraphFixture.CreateAsync(db, await db.PersonalWorkspaces.SingleAsync(w => w.Id == workspace.Id));
    }
    private static async Task Login(HttpClient client)
    {
        var email = $"quiz-{Guid.NewGuid():N}@example.invalid";
        using var registered = await RegistrationTestFactory.RegisterAsync(client, new { email, password = Password }, await RegistrationTestFactory.CsrfAsync(client));
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        using var login = await Write(client, "POST", "/api/auth/login", JsonSerializer.Serialize(new { email, password = Password })); Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }
    private static async Task<HttpResponseMessage> Write(HttpClient client, string method, string path, string? json) => await Send(client, method, path, json, await RegistrationTestFactory.CsrfAsync(client));
    private static async Task<HttpResponseMessage> Send(HttpClient client, string method, string path, string? json, string? token, bool unknownLength = false)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (json is not null) request.Content = unknownLength ? new UnknownLengthContent(json) : new StringContent(json, Encoding.UTF8, "application/json");
        if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }
    private static async Task<string> Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync(); using var json = JsonDocument.Parse(text); Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.Null(response.Headers.Location);
        foreach (var secret in new[] { "SQLite", "Exception", "WorkspaceId", "OwnerUserId", "correctOptionIndex", Password }) Assert.DoesNotContain(secret, text);
        return text;
    }
    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] bytes;
        public UnknownLengthContent(string text) { bytes = Encoding.UTF8.GetBytes(text); Headers.ContentType = new("application/json"); }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }
}

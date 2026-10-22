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
using Talynor.Domain.Quizzes;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class QuizAttemptHttpTests
{
    private const string Password = "synthetic quiz attempt test passphrase";
    private const int Limit = 16384;
    private static string Root(Guid map, Guid node, Guid quiz) => $"/api/maps/{map:D}/nodes/{node:D}/quizzes/{quiz:D}";
    private static string Body(Quiz q, int revision = 1, QuizAnswerInput[]? answers = null) => JsonSerializer.Serialize(new
    { contentRevision = revision, answers = (answers ?? QuizAttemptPersistenceTests.Answers(q)).Select(a => new { questionId = a.QuestionId, selectedOptionId = a.SelectedOptionId }) });

    [Theory]
    [InlineData("LIST")] [InlineData("GET")] [InlineData("PUT")]
    public async Task Every_attempt_endpoint_requires_session_without_creating_database(string method)
    {
        using var factory = new RegistrationTestFactory(); using var client = factory.NewClient(); var path = Root(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) + "/attempts";
        using var response = await Send(client, method == "PUT" ? "PUT" : "GET", path + (method == "LIST" ? "" : "/" + Guid.NewGuid()), null, null);
        await Problem(response, HttpStatusCode.Unauthorized, "authentication_required");
        using var scope = factory.Services.CreateScope(); var cs = scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.GetConnectionString();
        Assert.False(File.Exists(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(cs).DataSource));
    }

    [Fact]
    public async Task Submit_requires_valid_session_bound_csrf_while_reads_do_not()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient(); var (g, q) = await Prepare(factory, client);
        var path = Root(g.Map.Id, g.Second.Id, q.Id) + "/attempts"; using var other = factory.NewClient();
        foreach (var token in new string?[] { null, "invalid", await RegistrationTestFactory.CsrfAsync(other) })
        { using var denied = await Send(client, "PUT", path + "/" + Guid.NewGuid(), Body(q), token); await Problem(denied, HttpStatusCode.BadRequest, "csrf_invalid"); }
        var id = Guid.NewGuid(); var accepted = await Put(client, path + "/" + id, Body(q));
        Assert.Equal(id, accepted.GetProperty("attemptId").GetGuid()); await Get(client, path); await Get(client, path + "/" + id);
    }

    [Fact]
    public async Task Submit_replay_edit_lookup_history_and_delete_preserve_exact_historical_contract()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient(); var (g, q) = await Prepare(factory, client);
        var root = Root(g.Map.Id, g.Second.Id, q.Id); var path = root + "/attempts"; var id = Guid.NewGuid(); var detail = path + "/" + id;
        var empty = await Get(client, path); Shape(empty, "attempts", "page", "pageSize", "hasMore"); Assert.Empty(empty.GetProperty("attempts").EnumerateArray());
        Assert.Equal(1, empty.GetProperty("page").GetInt32()); Assert.Equal(20, empty.GetProperty("pageSize").GetInt32());
        var original = await Put(client, detail, Body(q)); AssertResult(original); Assert.Equal(66.67m, original.GetProperty("accuracyPercentage").GetDecimal());
        Assert.Equal(2, original.GetProperty("correctCount").GetInt32()); Assert.Equal(3, original.GetProperty("questionCount").GetInt32());
        Assert.Equal("original title", original.GetProperty("quizTitle").GetString()); Assert.Equal(q.Id, original.GetProperty("quizId").GetGuid());
        Assert.Equal(original.GetRawText(), (await Put(client, detail, Body(q, answers: QuizAttemptPersistenceTests.Answers(q).Reverse().ToArray()))).GetRawText());
        using (var conflict = await Write(client, detail, Body(q, answers: QuizAttemptPersistenceTests.Answers(q, 3)))) await Problem(conflict, HttpStatusCode.Conflict, "attempt_id_conflict");
        using (var conflict = await Write(client, detail, Body(q, 2))) await Problem(conflict, HttpStatusCode.Conflict, "attempt_id_conflict");
        var editBody = JsonSerializer.Serialize(new { expectedRevision = 1, title = "edited title", questions = new[] { new { text = "edited Q", options = new[] { "edited A", "edited B" }, correctOptionIndex = 1 } } });
        using (var edit = await Write(client, root, editBody)) Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
        Assert.Equal(original.GetRawText(), (await Put(client, detail, Body(q))).GetRawText());
        Assert.Equal(original.GetRawText(), (await Get(client, detail)).GetRawText());
        using (var stale = await Write(client, path + "/" + Guid.NewGuid(), Body(q))) await Problem(stale, HttpStatusCode.Conflict, "quiz_changed");
        var history = await Get(client, path); var summary = Assert.Single(history.GetProperty("attempts").EnumerateArray());
        Shape(summary, "attemptId", "quizId", "quizTitle", "contentRevision", "submittedAtUtc", "questionCount", "correctCount", "accuracyPercentage");
        Assert.Equal("original title", summary.GetProperty("quizTitle").GetString()); Assert.Equal(1, summary.GetProperty("contentRevision").GetInt32());
        using (var delete = new HttpRequestMessage(HttpMethod.Delete, root))
        { delete.Headers.Add("X-CSRF-TOKEN", await RegistrationTestFactory.CsrfAsync(client)); using var removed = await client.SendAsync(delete); Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode); }
        using (var missing = await client.GetAsync(detail)) await Problem(missing, HttpStatusCode.NotFound, "quiz_not_found");
        using (var noResurrection = await Write(client, detail, Body(q))) await Problem(noResurrection, HttpStatusCode.NotFound, "quiz_not_found");
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        Assert.Empty(await db.QuizAttempts.ToArrayAsync()); Assert.Empty(await db.QuizAttemptAnswers.ToArrayAsync()); Assert.Empty(await db.QuizAttemptAnswerOptions.ToArrayAsync());
    }

    [Theory]
    [InlineData("null")] [InlineData("[]")] [InlineData("42")] [InlineData("{")] [InlineData("{}")]
    [InlineData("{\"contentRevision\":1}")] [InlineData("{\"contentRevision\":1,\"answers\":null}")]
    [InlineData("{\"contentRevision\":1,\"answers\":[]}")] [InlineData("{\"contentRevision\":1,\"answers\":[null]}")]
    [InlineData("{\"contentRevision\":1,\"answers\":[{}]}")]
    [InlineData("{\"contentRevision\":1,\"answers\":[{\"questionId\":\"bad\",\"selectedOptionId\":null}]}")]
    public async Task Malformed_missing_or_null_submission_is_sanitized_without_rows(string raw)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient(); var (g, q) = await Prepare(factory, client);
        using var response = await Write(client, Root(g.Map.Id, g.Second.Id, q.Id) + "/attempts/" + Guid.NewGuid(), raw);
        await Problem(response, HttpStatusCode.BadRequest, "validation_failed"); await Empty(factory);
    }

    [Theory]
    [InlineData("null")] [InlineData("0")] [InlineData("-1")] [InlineData("2147483648")] [InlineData("\"1\"")] [InlineData("1.5")]
    public async Task Revision_must_be_positive_strict_int32(string revision)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient(); var (g, q) = await Prepare(factory, client);
        using var response = await Write(client, Root(g.Map.Id, g.Second.Id, q.Id) + "/attempts/" + Guid.NewGuid(), Body(q).Replace("\"contentRevision\":1", "\"contentRevision\":" + revision));
        await Problem(response, HttpStatusCode.BadRequest, "validation_failed"); await Empty(factory);
    }

    [Theory]
    [InlineData("score")] [InlineData("correctCount")] [InlineData("questionCount")] [InlineData("accuracyPercentage")]
    [InlineData("correctOptionId")] [InlineData("isCorrect")] [InlineData("submittedAtUtc")] [InlineData("attemptId")]
    [InlineData("quizId")] [InlineData("mapId")] [InlineData("knowledgeNodeId")] [InlineData("workspaceId")]
    [InlineData("ownerUserId")] [InlineData("studySessionId")] [InlineData("unexpected")]
    public async Task Client_score_identity_time_and_unknown_members_are_rejected_at_both_levels(string field)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient(); var (g, q) = await Prepare(factory, client);
        var path = Root(g.Map.Id, g.Second.Id, q.Id) + "/attempts/" + Guid.NewGuid();
        foreach (var raw in new[] { "{\"" + field + "\":\"private\"," + Body(q)[1..], Body(q).Replace("\"questionId\":", "\"" + field + "\":\"private\",\"questionId\":") })
        { using var response = await Write(client, path, raw); await Problem(response, HttpStatusCode.BadRequest, "validation_failed"); }
        await Empty(factory);
    }

    [Theory]
    [InlineData("missing")] [InlineData("extra")] [InlineData("duplicate")] [InlineData("wrong-option")]
    [InlineData("unknown-option")] [InlineData("empty-question")] [InlineData("empty-option")] [InlineData("empty-id")]
    public async Task Complete_sheet_validation_rejects_invalid_semantic_answers(string kind)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient(); var (g, q) = await Prepare(factory, client);
        var answers = QuizAttemptPersistenceTests.Answers(q); var id = Guid.NewGuid();
        switch (kind)
        {
            case "missing": answers = answers[..2]; break;
            case "extra": answers = [..answers, new(Guid.NewGuid(), Guid.NewGuid())]; break;
            case "duplicate": answers[1] = answers[0]; break;
            case "wrong-option": answers[0] = answers[0] with { SelectedOptionId = q.Questions[1].Options[0].Id }; break;
            case "unknown-option": answers[0] = answers[0] with { SelectedOptionId = Guid.NewGuid() }; break;
            case "empty-question": answers[0] = answers[0] with { QuestionId = Guid.Empty }; break;
            case "empty-option": answers[0] = answers[0] with { SelectedOptionId = Guid.Empty }; break;
            case "empty-id": id = Guid.Empty; break;
        }
        using var response = await Write(client, Root(g.Map.Id, g.Second.Id, q.Id) + "/attempts/" + id, Body(q, answers: answers));
        await Problem(response, HttpStatusCode.BadRequest, "validation_failed"); await Empty(factory);
    }

    [Theory]
    [InlineData("page=0")] [InlineData("page=-1")] [InlineData("page=2147483648")] [InlineData("page=2147483647&pageSize=100")]
    [InlineData("pageSize=0")] [InlineData("pageSize=101")] [InlineData("page=1&page=2")] [InlineData("pageSize=20&pageSize=20")]
    [InlineData("page=")] [InlineData("page=bad")] [InlineData("workspaceId=foreign")]
    public async Task History_rejects_invalid_duplicate_overflow_and_unknown_pagination(string query)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient(); var (g, q) = await Prepare(factory, client);
        using var response = await client.GetAsync(Root(g.Map.Id, g.Second.Id, q.Id) + "/attempts?" + query); await Problem(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task History_pages_have_historical_summaries_and_no_individual_attempt_mutation_route()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient(); var (g, q) = await Prepare(factory, client); var path = Root(g.Map.Id, g.Second.Id, q.Id) + "/attempts";
        using (var scope = factory.Services.CreateScope())
        { var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); db.QuizAttempts.AddRange(Enumerable.Range(0, 21).Select(i => QuizAttempt.Grade(Guid.NewGuid(), q, 1, QuizAttemptPersistenceTests.Answers(q), QuizAttemptPersistenceTests.Now.AddSeconds(i)))); await db.SaveChangesAsync(); }
        var page = await Get(client, path); Assert.Equal(20, page.GetProperty("attempts").GetArrayLength()); Assert.True(page.GetProperty("hasMore").GetBoolean());
        var next = await Get(client, path + "?page=2"); Assert.Single(next.GetProperty("attempts").EnumerateArray()); Assert.False(next.GetProperty("hasMore").GetBoolean());
        Assert.Equal(21, (await Get(client, path + "?pageSize=100")).GetProperty("attempts").GetArrayLength());
        var detail = path + "/" + page.GetProperty("attempts")[0].GetProperty("attemptId").GetGuid();
        foreach (var method in new[] { "PATCH", "DELETE" })
        { using var denied = await Send(client, method, detail, null, await RegistrationTestFactory.CsrfAsync(client)); Assert.Equal(HttpStatusCode.MethodNotAllowed, denied.StatusCode); }
        Assert.Equal(21, (await Get(client, path + "?pageSize=100")).GetProperty("attempts").GetArrayLength());
    }

    [Fact]
    public async Task Foreign_missing_mixed_paths_and_global_id_collisions_never_disclose_results()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient(); var (g, q) = await Prepare(factory, client);
        var own = Root(g.Map.Id, g.Second.Id, q.Id) + "/attempts"; var id = Guid.NewGuid(); var original = await Put(client, own + "/" + id, Body(q));
        var paths = new[] { Root(g.OtherMap.Id, g.Second.Id, q.Id), Root(g.Map.Id, g.ChildNode.Id, q.Id), Root(g.Child.Id, g.Second.Id, q.Id),
            Root(Guid.NewGuid(), g.Second.Id, q.Id), Root(g.Map.Id, Guid.NewGuid(), q.Id), Root(g.Map.Id, g.Second.Id, Guid.NewGuid()), Root(g.Map.Id, g.Second.Id, Guid.Empty) };
        string? baseline = null;
        foreach (var root in paths)
        {
            foreach (var suffix in new[] { "/attempts", "/attempts/" + id })
            { using var missing = await client.GetAsync(root + suffix); var problem = await Problem(missing, HttpStatusCode.NotFound, "quiz_not_found"); baseline ??= problem; Assert.Equal(baseline, problem); }
            using var denied = await Write(client, root + "/attempts/" + id, Body(q)); Assert.Equal(baseline, await Problem(denied, HttpStatusCode.NotFound, "quiz_not_found"));
        }
        using (var missing = await client.GetAsync(own + "/" + Guid.NewGuid())) Assert.Equal(baseline, await Problem(missing, HttpStatusCode.NotFound, "quiz_not_found"));
        using var foreign = factory.NewClient(); var (fg, fq) = await Prepare(factory, foreign);
        foreach (var suffix in new[] { "", "/" + id }) { using var denied = await foreign.GetAsync(own + suffix); Assert.Equal(baseline, await Problem(denied, HttpStatusCode.NotFound, "quiz_not_found")); }
        using (var denied = await Write(foreign, own + "/" + id, Body(q))) Assert.Equal(baseline, await Problem(denied, HttpStatusCode.NotFound, "quiz_not_found"));
        using (var collision = await Write(foreign, Root(fg.Map.Id, fg.Second.Id, fq.Id) + "/attempts/" + id, Body(fq)))
        { var problem = await Problem(collision, HttpStatusCode.Conflict, "attempt_id_conflict"); Assert.DoesNotContain("original title", problem); }
        Assert.Equal(original.GetRawText(), (await Get(client, own + "/" + id)).GetRawText());
        Assert.Empty((await Get(foreign, Root(fg.Map.Id, fg.Second.Id, fq.Id) + "/attempts")).GetProperty("attempts").EnumerateArray());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TestServer_enforces_actual_submission_stream_limit(bool unknownLength)
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient(); var (g, q) = await Prepare(factory, client);
        await Bounds(client, Root(g.Map.Id, g.Second.Id, q.Id) + "/attempts", q, unknownLength, false);
    }

    [Fact]
    public async Task Real_kestrel_https_checks_fixed_chunked_submission_caps_and_authoring_metadata_unchanged()
    {
        using var key = RSA.Create(2048); var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback); request.CertificateExtensions.Add(names.Build());
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var cert = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pkcs12), null);
        using var factory = new RegistrationTestFactory(); factory.UseKestrel(o => o.Listen(IPAddress.Loopback, 0, listener => listener.UseHttps(cert)));
        await factory.InitializeAsync(); var address = Assert.Single(factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, ServerCertificateCustomValidationCallback = (_, c, _, _) => c?.GetCertHashString() == cert.GetCertHashString() };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(address) }; var (g, q) = await Prepare(factory, client);
        var path = Root(g.Map.Id, g.Second.Id, q.Id) + "/attempts"; await Bounds(client, path, q, false, true); await Bounds(client, path, q, true, true);
        var actions = factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>();
        foreach (var action in actions.Where(a => a.ControllerTypeInfo.AsType() == typeof(QuizAttemptsController)))
        { var limits = action.FilterDescriptors.Select(f => f.Filter).OfType<IRequestSizeLimitMetadata>(); if (action.ActionName == "Submit") Assert.Equal((long)Limit, Assert.Single(limits).MaxRequestBodySize); else Assert.Empty(limits); }
        foreach (var action in actions.Where(a => a.ControllerTypeInfo.AsType() == typeof(QuizzesController) && a.ActionName is "Create" or "Replace"))
            Assert.Equal(262144L, Assert.Single(action.FilterDescriptors.Select(f => f.Filter).OfType<IRequestSizeLimitMetadata>()).MaxRequestBodySize);
    }

    [Fact]
    public async Task Operational_failures_and_removed_account_are_not_disguised_as_missing_history()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient(); var (g, q) = await Prepare(factory, client); var path = Root(g.Map.Id, g.Second.Id, q.Id) + "/attempts";
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.ExecuteSqlRawAsync("DROP TABLE QuizAttempts");
        foreach (var suffix in new[] { "", "/" + Guid.NewGuid() }) { using var failed = await client.GetAsync(path + suffix); await Problem(failed, HttpStatusCode.InternalServerError, "unexpected_error"); }
        using (var failed = await Write(client, path + "/" + Guid.NewGuid(), Body(q))) await Problem(failed, HttpStatusCode.InternalServerError, "unexpected_error");
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Users.ExecuteDeleteAsync();
        using var removed = await client.GetAsync(path); await Problem(removed, HttpStatusCode.Unauthorized, "authentication_required");
    }

    private static async Task Bounds(HttpClient client, string path, Quiz q, bool unknownLength, bool native)
    {
        var id = Guid.NewGuid(); var body = Body(q); var within = native && unknownLength ? Limit - 1024 : Limit;
        using var accepted = await Send(client, "PUT", path + "/" + id, body.PadRight(within), await RegistrationTestFactory.CsrfAsync(client), unknownLength);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode); Assert.True(accepted.Headers.CacheControl?.NoStore); AssertResult(await accepted.Content.ReadFromJsonAsync<JsonElement>());
        var overId = Guid.NewGuid(); using var over = await Send(client, "PUT", path + "/" + overId, body.PadRight(Limit + 1), await RegistrationTestFactory.CsrfAsync(client), unknownLength);
        await Problem(over, HttpStatusCode.RequestEntityTooLarge, "validation_failed"); using var missing = await client.GetAsync(path + "/" + overId); await Problem(missing, HttpStatusCode.NotFound, "quiz_not_found");
    }
    private static async Task<(KnowledgeGraphFixture Graph, Quiz Quiz)> Prepare(RegistrationTestFactory factory, HttpClient client)
    {
        var email = $"attempt-{Guid.NewGuid():N}@example.invalid";
        using var register = await RegistrationTestFactory.RegisterAsync(client, new { email, password = Password }, await RegistrationTestFactory.CsrfAsync(client)); Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        using var login = await Send(client, "POST", "/api/auth/login", JsonSerializer.Serialize(new { email, password = Password }), await RegistrationTestFactory.CsrfAsync(client)); Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var workspace = (await client.GetFromJsonAsync<PersonalWorkspaceResponse>("/api/workspace"))!;
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var graph = await KnowledgeGraphFixture.CreateAsync(db, await db.PersonalWorkspaces.SingleAsync(w => w.Id == workspace.Id),
            Talynor.Domain.Workspaces.PersonalWorkspace.Create("fixture-other-" + Guid.NewGuid(), TimeProvider.System.GetUtcNow()));
        var quiz = QuizAttemptPersistenceTests.NewQuiz(graph.Second.Id);
        db.Quizzes.Add(quiz); await db.SaveChangesAsync(); return (graph, quiz);
    }
    private static async Task Empty(RegistrationTestFactory factory)
    { using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>(); Assert.Empty(await db.QuizAttempts.ToArrayAsync()); Assert.Empty(await db.QuizAttemptAnswers.ToArrayAsync()); Assert.Empty(await db.QuizAttemptAnswerOptions.ToArrayAsync()); }
    private static void AssertResult(JsonElement value)
    {
        Shape(value, "attemptId", "quizId", "quizTitle", "contentRevision", "submittedAtUtc", "questionCount", "correctCount", "accuracyPercentage", "answers");
        foreach (var a in value.GetProperty("answers").EnumerateArray())
        { Shape(a, "questionId", "position", "text", "options", "selectedOptionId", "correctOptionId", "isCorrect"); foreach (var o in a.GetProperty("options").EnumerateArray()) Shape(o, "id", "position", "text"); }
    }
    private static void Shape(JsonElement value, params string[] fields) => Assert.Equal(fields.Order(), value.EnumerateObject().Select(p => p.Name).Order());
    private static async Task<JsonElement> Get(HttpClient client, string path)
    { using var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    private static async Task<JsonElement> Put(HttpClient client, string path, string json)
    { using var response = await Write(client, path, json); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    private static async Task<HttpResponseMessage> Write(HttpClient client, string path, string json) => await Send(client, "PUT", path, json, await RegistrationTestFactory.CsrfAsync(client));
    private static async Task<HttpResponseMessage> Send(HttpClient client, string method, string path, string? json, string? token, bool unknown = false)
    { using var request = new HttpRequestMessage(new HttpMethod(method), path); if (json is not null) request.Content = unknown ? new UnknownLengthContent(json) : new StringContent(json, Encoding.UTF8, "application/json"); if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token); return await client.SendAsync(request); }
    private static async Task<string> Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore); Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType); var text = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text); Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        foreach (var secret in new[] { "SQLite", "Exception", "correctOptionId", "selectedOptionId", "WorkspaceId", "original title", Password }) Assert.DoesNotContain(secret, text);
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

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Api.Contracts;
using Talynor.Application.Progress;
using Talynor.Domain.Flashcards;
using Talynor.Domain.StudySessions;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class NodeProgressHttpTests
{
    private const string Password = "synthetic progress test passphrase";
    private const string PrivateFailure = "Private synthetic progress failure: SQLite AspNetUsers";
    private static string Path(Guid map, Guid node) => $"/api/maps/{map:D}/nodes/{node:D}/progress";

    [Fact]
    public async Task Anonymous_read_is_no_store_401_without_redirect_or_startup_database_creation()
    {
        using var factory = new RegistrationTestFactory(); using var client = factory.NewClient();
        using var response = await client.GetAsync(Path(Guid.NewGuid(), Guid.NewGuid()));
        await Problem(response, HttpStatusCode.Unauthorized, "authentication_required");
        using var scope = factory.Services.CreateScope();
        var cs = scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.GetConnectionString();
        Assert.False(File.Exists(new SqliteConnectionStringBuilder(cs).DataSource));
    }

    [Theory]
    [InlineData("")] [InlineData("?days=7")] [InlineData("?days=30")]
    [InlineData("?days=90")] [InlineData("?days=030")]
    public async Task Empty_node_has_exact_minimal_DTO_UTC_period_and_numeric_nulls_without_csrf(string query)
    {
        var clock = new Clock(); using var factory = Factory(clock); await factory.InitializeAsync();
        using var client = factory.NewClient(); var graph = await Seed(factory, await Login(client));
        Assert.False(client.DefaultRequestHeaders.Contains("X-CSRF-TOKEN"));
        var before = clock.Calls;
        // An irrelevant invalid CSRF header must not turn this safe GET into a CSRF check.
        using var request = new HttpRequestMessage(HttpMethod.Get, Path(graph.Map.Id, graph.First.Id) + query);
        request.Headers.Add("X-CSRF-TOKEN", "invalid-and-unneeded-for-GET");
        using var response = await client.SendAsync(request); var json = await Success(response);
        Assert.Equal(before + 1, clock.Calls);
        Shape(json);
        var days = query == "?days=7" ? 7 : query == "?days=90" ? 90 : 30;
        var period = json.GetProperty("period");
        Assert.Equal(days, period.GetProperty("days").GetInt32());
        Assert.Equal(clock.Now, json.GetProperty("calculatedAtUtc").GetDateTimeOffset());
        Assert.Equal(clock.Now, period.GetProperty("toUtc").GetDateTimeOffset());
        Assert.Equal(clock.Now.AddDays(-days), period.GetProperty("fromUtc").GetDateTimeOffset());
        foreach (var key in new[] { "fromUtc", "toUtc" })
            Assert.Equal(TimeSpan.Zero, period.GetProperty(key).GetDateTimeOffset().Offset);
        AssertStudy(json, 0, 0); AssertReviews(json, 0, 0, null); AssertQuizzes(json, 0, 0, 0, null);
    }

    [Fact]
    public async Task Populated_node_returns_full_end_attribution_historical_ratios_and_weighted_accuracy()
    {
        var clock = new Clock(); using var factory = Factory(clock); await factory.InitializeAsync();
        using var client = factory.NewClient(); var graph = await Seed(factory, await Login(client));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            var node = graph.First.Id; var at = clock.Now.AddMinutes(-1); var from = clock.Now.AddDays(-30);
            var card = Flashcard.Create(node, "Private front", "Private back", at);
            var one = NodeProgressFixture.QuizFor(node); var ten = NodeProgressFixture.QuizFor(node, 10);
            db.AddRange(card, one, ten);
            db.StudySessions.AddRange(NodeProgressFixture.Session(node, from.AddHours(-1), from.AddHours(1)),
                NodeProgressFixture.Session(node, at, at),
                NodeProgressFixture.Session(node, at.AddTicks(-6_000_000), at),
                NodeProgressFixture.Session(node, at.AddTicks(-6_000_000), at), StudySession.Start(node, at));
            db.FlashcardReviews.AddRange(FlashcardReview.Create(Guid.NewGuid(), card.Id, 1, FlashcardAnswerResult.Correct, at),
                FlashcardReview.Create(Guid.NewGuid(), card.Id, 1, FlashcardAnswerResult.Incorrect, at),
                FlashcardReview.Create(Guid.NewGuid(), card.Id, 1, FlashcardAnswerResult.Correct, at));
            db.QuizAttempts.AddRange(NodeProgressFixture.Attempt(one, 1, at), NodeProgressFixture.Attempt(ten, 5, at));
            await db.SaveChangesAsync();
        }
        var json = await Get(client, Path(graph.Map.Id, graph.First.Id)); Shape(json);
        AssertStudy(json, 4, 7201); AssertReviews(json, 3, 2, 66.67m); AssertQuizzes(json, 2, 11, 6, 54.55m);
        Assert.False(client.DefaultRequestHeaders.Contains("X-CSRF-TOKEN"));
    }

    [Theory]
    [InlineData(7, 0)] [InlineData(30, 1)] [InlineData(90, 1)]
    public async Task Days_preset_reaches_real_reader_for_all_three_sources(int days, long count)
    {
        var clock = new Clock(); using var factory = Factory(clock); await factory.InitializeAsync();
        using var client = factory.NewClient(); var graph = await Seed(factory, await Login(client));
        using (var scope = factory.Services.CreateScope())
            await NodeProgressFixture.AddActivity(scope.ServiceProvider.GetRequiredService<TalynorDbContext>(),
                graph.First.Id, clock.Now.AddDays(-10));
        var json = await Get(client, Path(graph.Map.Id, graph.First.Id) + $"?days={days}");
        AssertStudy(json, count, count); AssertReviews(json, count, count, count == 0 ? null : 100m);
        AssertQuizzes(json, count, count, count, count == 0 ? null : 100m);
    }

    [Theory]
    [InlineData("foo=1")] [InlineData("WorkspaceId=private")] [InlineData("OwnerUserId=private")]
    [InlineData("days=30&foo=1")] [InlineData("days=30&days=90")] [InlineData("days=30&days=30")]
    [InlineData("days=30&Days=30")] [InlineData("days=")] [InlineData("days")]
    [InlineData("days=abc")] [InlineData("days=private%3Cscript%3E")] [InlineData("days=31")]
    [InlineData("days=0")] [InlineData("days=-1")] [InlineData("days=91")]
    [InlineData("days=2147483648")] [InlineData("days=-2147483649")]
    [InlineData("days=30.0")] [InlineData("days=3e1")] [InlineData("Days=30")]
    [InlineData("=30")] [InlineData("days[]=30")] [InlineData("days=7,30")]
    public async Task Invalid_query_is_sanitized_no_store_400_before_reader_or_clock(string query)
    {
        var clock = new Clock(); var reader = new FailedReader();
        using var factory = Factory(clock, services => services.AddSingleton<INodeProgressReader>(reader));
        await factory.InitializeAsync(); using var client = factory.NewClient(); await Login(client);
        var before = clock.Calls;
        using var response = await client.GetAsync(Path(Guid.NewGuid(), Guid.NewGuid()) + "?" + query);
        var body = await Problem(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.DoesNotContain("script", body); Assert.DoesNotContain("private", body);
        Assert.Equal(0, reader.Calls); Assert.Equal(before, clock.Calls);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Empty_Guid_inputs_are_validation_failed(bool map)
    {
        using var factory = Factory(new Clock()); await factory.InitializeAsync();
        using var client = factory.NewClient(); await Login(client);
        using var response = await client.GetAsync(Path(map ? Guid.Empty : Guid.NewGuid(), map ? Guid.NewGuid() : Guid.Empty));
        await Problem(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Missing_foreign_and_mismatched_paths_are_indistinguishable_and_cannot_override_ownership()
    {
        var clock = new Clock(); using var factory = Factory(clock); await factory.InitializeAsync();
        using var a = factory.NewClient(); using var b = factory.NewClient();
        var wa = await Login(a, "a@example.invalid"); var wb = await Login(b, "b@example.invalid");
        var graph = await Seed(factory, wa, wb); Guid foreign;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            var node = Talynor.Domain.KnowledgeMaps.KnowledgeNode.Create(graph.OtherMap.Id, "Foreign", null, 0, 0, null, clock.Now);
            db.Add(node); await db.SaveChangesAsync(); foreign = node.Id;
            await NodeProgressFixture.AddActivity(db, foreign, clock.Now.AddSeconds(-1));
        }
        string? baseline = null;
        foreach (var path in new[] { Path(graph.OtherMap.Id, foreign), Path(graph.Map.Id, foreign),
            Path(graph.OtherMap.Id, graph.First.Id), Path(graph.Map.Id, graph.ChildNode.Id),
            Path(Guid.NewGuid(), graph.First.Id), Path(graph.Map.Id, Guid.NewGuid()) })
        {
            using var response = await a.GetAsync(path);
            var body = await Problem(response, HttpStatusCode.NotFound, "progress_not_found");
            baseline ??= body; Assert.Equal(baseline, body);
        }
        using (var response = await b.GetAsync(Path(graph.Map.Id, graph.First.Id)))
            Assert.Equal(baseline, await Problem(response, HttpStatusCode.NotFound, "progress_not_found"));
        var own = await Get(b, Path(graph.OtherMap.Id, foreign)); AssertStudy(own, 1, 1);
        foreach (var query in new[] { $"WorkspaceId={wb.Id:D}", "OwnerUserId=ignored" })
        {
            using var response = await a.GetAsync(Path(graph.OtherMap.Id, foreign) + "?" + query);
            await Problem(response, HttpStatusCode.BadRequest, "validation_failed");
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Deleted_or_stamp_invalidated_account_gets_no_store_401_and_local_cookie_deletion(bool stamp)
    {
        var authClock = new AuthClock(); using var factory = Factory(new Clock(), services =>
        {
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, o => o.TimeProvider = authClock);
            services.PostConfigure<SecurityStampValidatorOptions>(o => o.TimeProvider = authClock);
        });
        await factory.InitializeAsync(); using var client = factory.NewClient(); var graph = await Seed(factory, await Login(client));
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = (await users.FindByEmailAsync("progress@example.invalid"))!;
            Assert.True((stamp ? await users.UpdateSecurityStampAsync(user) : await users.DeleteAsync(user)).Succeeded);
        }
        if (stamp) authClock.Now += TimeSpan.FromMinutes(31);
        using var response = await client.GetAsync(Path(graph.Map.Id, graph.First.Id));
        await Problem(response, HttpStatusCode.Unauthorized, "authentication_required");
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), h => h.StartsWith("__Host-Talynor.Auth=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Existing_forbid_handler_returns_no_store_403_without_HTML_redirect()
    {
        using var factory = Factory(new Clock(), services => services.AddAuthorization(options =>
            options.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim("test-only-denied").Build()));
        await factory.InitializeAsync(); using var client = factory.NewClient(); await Login(client, workspace: false);
        using var response = await client.GetAsync(Path(Guid.NewGuid(), Guid.NewGuid()));
        await Problem(response, HttpStatusCode.Forbidden, "access_denied");
    }

    [Theory]
    [InlineData("reader")] [InlineData("account")] [InlineData("workspace")] [InlineData("stamp")]
    [InlineData("source")] [InlineData("clock")] [InlineData("overflow")]
    public async Task Operational_clock_corrupt_and_pre_controller_failures_are_sanitized_no_store_500(string source)
    {
        var clock = new Clock(); var authClock = new AuthClock(); var failed = new FailedReader(source == "overflow");
        using var factory = Factory(clock, services =>
        {
            if (source is "reader" or "overflow") services.AddSingleton<INodeProgressReader>(failed);
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, o => o.TimeProvider = authClock);
            services.PostConfigure<SecurityStampValidatorOptions>(o => o.TimeProvider = authClock);
        });
        await factory.InitializeAsync(); using var client = factory.NewClient(); var graph = await Seed(factory, await Login(client));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            if (source is "account" or "stamp") await db.Database.ExecuteSqlRawAsync("DROP TABLE AspNetUsers");
            if (source == "workspace") await db.Database.ExecuteSqlRawAsync("ALTER TABLE PersonalWorkspaces RENAME COLUMN OwnerUserId TO BrokenOwner");
            if (source == "source") await db.Database.ExecuteSqlRawAsync("DROP TABLE FlashcardReviews");
        }
        if (source == "stamp") authClock.Now += TimeSpan.FromMinutes(31);
        if (source == "clock") clock.Now = DateTimeOffset.MinValue;
        using var response = await client.GetAsync(Path(graph.Map.Id, graph.First.Id));
        await Problem(response, HttpStatusCode.InternalServerError, "unexpected_error");
        if (source is "reader" or "overflow") Assert.Equal(1, failed.Calls);
    }

    [Fact]
    public async Task JSON_preserves_safe_Int64_counts_decimal_precision_and_only_the_own_DTO()
    {
        var max = NodeProgressCalculation.MaxSafeInteger;
        using var factory = Factory(new Clock(), s => s.AddSingleton<INodeProgressReader>(new MaxReader()));
        await factory.InitializeAsync(); using var client = factory.NewClient(); await Login(client);
        var json = await Get(client, Path(Guid.NewGuid(), Guid.NewGuid())); Shape(json);
        AssertStudy(json, max, max); AssertReviews(json, 32, 1, 3.13m); AssertQuizzes(json, 4, 32, 1, 3.13m);
    }

    [Fact]
    public async Task Lazy_workspace_resolution_is_preserved_without_creating_map_activity_or_progress()
    {
        using var factory = Factory(new Clock()); await factory.InitializeAsync(); using var client = factory.NewClient();
        await Login(client, workspace: false);
        using var response = await client.GetAsync(Path(Guid.NewGuid(), Guid.NewGuid()));
        await Problem(response, HttpStatusCode.NotFound, "progress_not_found");
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        Assert.Single(await db.PersonalWorkspaces.ToListAsync()); Assert.Empty(await db.KnowledgeMaps.ToListAsync());
        Assert.Empty(await db.StudySessions.ToListAsync()); Assert.Empty(await db.FlashcardReviews.ToListAsync());
        Assert.Empty(await db.QuizAttempts.ToListAsync());
    }

    [Fact]
    public async Task Openapi_exposes_one_GET_only_and_malformed_Guids_keep_route_level_404()
    {
        using var factory = Factory(new Clock()); using var client = factory.NewClient();
        var api = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = api.GetProperty("paths").EnumerateObject().Where(p => p.Name.EndsWith("/progress", StringComparison.Ordinal)).ToArray();
        var route = Assert.Single(paths); Assert.Equal("/api/maps/{mapId}/nodes/{nodeId}/progress", route.Name);
        Properties(route.Value, "get");
        foreach (var url in new[] { $"/api/maps/bad/nodes/{Guid.NewGuid():D}/progress", $"/api/maps/{Guid.NewGuid():D}/nodes/bad/progress" })
        { using var response = await client.GetAsync(url); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); }
    }

    private static RegistrationTestFactory Factory(Clock clock, Action<IServiceCollection>? configure = null) => new(configureServices: services =>
    {
        services.AddSingleton<TimeProvider>(clock);
        // Keep native cookie/stamp clock calls separate from the summary's one captured reference.
        var authClock = new AuthClock();
        services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, o => o.TimeProvider = authClock);
        services.PostConfigure<SecurityStampValidatorOptions>(o => o.TimeProvider = authClock);
        configure?.Invoke(services);
    });
    private static async Task<KnowledgeGraphFixture> Seed(RegistrationTestFactory factory, PersonalWorkspaceResponse workspace,
        PersonalWorkspaceResponse? other = null)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        return await KnowledgeGraphFixture.CreateAsync(db, await db.PersonalWorkspaces.SingleAsync(w => w.Id == workspace.Id),
            other is null ? null : await db.PersonalWorkspaces.SingleAsync(w => w.Id == other.Id));
    }
    private static async Task<PersonalWorkspaceResponse> Login(HttpClient client, string email = "progress@example.invalid", bool workspace = true)
    {
        using var registration = await RegistrationTestFactory.RegisterAsync(client, new { email, password = Password }, await RegistrationTestFactory.CsrfAsync(client));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { email, password = Password }) };
        request.Headers.Add("X-CSRF-TOKEN", await RegistrationTestFactory.CsrfAsync(client));
        using var login = await client.SendAsync(request); Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return workspace ? (await client.GetFromJsonAsync<PersonalWorkspaceResponse>("/api/workspace"))! : null!;
    }
    private static async Task<JsonElement> Get(HttpClient client, string path)
    { using var response = await client.GetAsync(path); return await Success(response); }
    private static async Task<JsonElement> Success(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Null(response.Headers.Location); Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return document.RootElement.Clone();
    }
    private static async Task<string> Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore); Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(); using var document = JsonDocument.Parse(body);
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
        Assert.Equal((int)status, document.RootElement.GetProperty("status").GetInt32());
        foreach (var marker in new[] { PrivateFailure, "SQLite", "Exception", "AspNetUsers", "PersonalWorkspaces", "FlashcardReviews", "stackTrace", Password })
            Assert.DoesNotContain(marker, body);
        Assert.False(document.RootElement.TryGetProperty("study", out _)); return body;
    }
    private static void Shape(JsonElement json)
    {
        Properties(json, "period", "calculatedAtUtc", "study", "flashcards", "quizzes");
        Properties(json.GetProperty("period"), "days", "fromUtc", "toUtc");
        Properties(json.GetProperty("study"), "completedSessionCount", "totalDurationSeconds");
        Properties(json.GetProperty("flashcards"), "reviewCount", "correctCount", "correctPercentage");
        Properties(json.GetProperty("quizzes"), "attemptCount", "answeredQuestionCount", "correctAnswerCount", "accuracyPercentage");
    }
    private static void Properties(JsonElement value, params string[] names) => Assert.Equal(names.Order(), value.EnumerateObject().Select(p => p.Name).Order());
    private static void AssertStudy(JsonElement root, long count, long seconds)
    { var json = root.GetProperty("study"); Assert.Equal(count, json.GetProperty("completedSessionCount").GetInt64()); Assert.Equal(seconds, json.GetProperty("totalDurationSeconds").GetInt64()); }
    private static void AssertReviews(JsonElement root, long count, long correct, decimal? percent)
    {
        var json = root.GetProperty("flashcards"); Assert.Equal(count, json.GetProperty("reviewCount").GetInt64());
        Assert.Equal(correct, json.GetProperty("correctCount").GetInt64()); AssertPercent(json.GetProperty("correctPercentage"), percent);
    }
    private static void AssertQuizzes(JsonElement root, long count, long questions, long correct, decimal? percent)
    {
        var json = root.GetProperty("quizzes"); Assert.Equal(count, json.GetProperty("attemptCount").GetInt64());
        Assert.Equal(questions, json.GetProperty("answeredQuestionCount").GetInt64());
        Assert.Equal(correct, json.GetProperty("correctAnswerCount").GetInt64()); AssertPercent(json.GetProperty("accuracyPercentage"), percent);
    }
    private static void AssertPercent(JsonElement value, decimal? expected)
    { if (expected is null) Assert.Equal(JsonValueKind.Null, value.ValueKind); else Assert.Equal(expected.Value, value.GetDecimal()); }
    private sealed class Clock : TimeProvider
    { public DateTimeOffset Now = NodeProgressFixture.Now; public int Calls; public override DateTimeOffset GetUtcNow() { Calls++; return Now; } }
    private sealed class AuthClock : TimeProvider
    { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class FailedReader(bool overflow = false) : INodeProgressReader
    {
        public int Calls;
        public Task<NodeProgressSummary?> ReadAsync(Guid workspace, Guid map, Guid node, NodeProgressPeriod period, CancellationToken ct)
        {
            Calls++;
            if (overflow) _ = NodeProgressCalculation.Study(1, (NodeProgressCalculation.MaxSafeInteger + 1m) * TimeSpan.TicksPerSecond);
            throw new InvalidOperationException(PrivateFailure);
        }
    }
    private sealed class MaxReader : INodeProgressReader
    {
        public Task<NodeProgressSummary?> ReadAsync(Guid workspace, Guid map, Guid node, NodeProgressPeriod period, CancellationToken ct) =>
            Task.FromResult<NodeProgressSummary?>(new(period, new(NodeProgressCalculation.MaxSafeInteger, NodeProgressCalculation.MaxSafeInteger),
                NodeProgressCalculation.Flashcards(32, 1), NodeProgressCalculation.Quizzes(4, 32, 1)));
    }
}

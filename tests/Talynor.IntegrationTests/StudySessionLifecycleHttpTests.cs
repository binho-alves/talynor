using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Api.Contracts;
using Talynor.Application.StudySessions;
using Talynor.Domain.StudySessions;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class StudySessionLifecycleHttpTests
{
    private const string Active = "/api/study-sessions/active";
    private static string Collection(KnowledgeGraphFixture f) => $"/api/maps/{f.Map.Id}/nodes/{f.First.Id}/study-sessions";
    private static string Item(KnowledgeGraphFixture f, Guid id) => $"{Collection(f)}/{id}";

    [Fact]
    public async Task Lifecycle_uses_server_clock_real_dto_location_first_completion_and_explicit_deletion()
    {
        var clock = new Clock(); using var factory = Factory(clock); await factory.InitializeAsync();
        using var client = factory.NewClient(); var f = await Seed(factory, await Login(client, "lifecycle@example.invalid"));
        var empty = await Get(client, Active); Properties(empty, "active", "serverTimeUtc");
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("active").ValueKind);
        Assert.Equal(clock.Now, empty.GetProperty("serverTimeUtc").GetDateTimeOffset());
        using var start = await Write(client, HttpMethod.Post, Collection(f));
        Assert.Equal(HttpStatusCode.Created, start.StatusCode); Assert.True(start.Headers.CacheControl?.NoStore);
        var session = await start.Content.ReadFromJsonAsync<JsonElement>();
        Properties(session, "id", "knowledgeNodeId", "startedAtUtc", "endedAtUtc", "durationSeconds");
        var id = session.GetProperty("id").GetGuid(); Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(f.First.Id, session.GetProperty("knowledgeNodeId").GetGuid());
        Assert.Equal(clock.Now, session.GetProperty("startedAtUtc").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, session.GetProperty("endedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, session.GetProperty("durationSeconds").ValueKind);
        Assert.Equal(Item(f, id), start.Headers.Location?.OriginalString);
        Assert.Equal(session.GetRawText(), (await Get(client, Item(f, id))).GetRawText());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
            await db.KnowledgeNodes.Where(n => n.Id == f.First.Id).ExecuteUpdateAsync(u => u.SetProperty(n => n.Title, "Current title"));
        }
        var active = (await Get(client, Active)).GetProperty("active");
        Properties(active, "session", "mapId", "nodeId", "nodeTitle");
        Assert.Equal(id, active.GetProperty("session").GetProperty("id").GetGuid());
        Assert.Equal(f.Map.Id, active.GetProperty("mapId").GetGuid()); Assert.Equal(f.First.Id, active.GetProperty("nodeId").GetGuid());
        Assert.Equal("Current title", active.GetProperty("nodeTitle").GetString());
        clock.Now = clock.Now.AddTicks(1234567); var firstEnd = clock.Now;
        using var complete = await Write(client, HttpMethod.Post, Item(f, id) + "/complete"); NoContent(complete);
        clock.Now = clock.Now.AddMinutes(10);
        using var repeat = await Write(client, HttpMethod.Post, Item(f, id) + "/complete"); NoContent(repeat);
        var detail = await Get(client, Item(f, id));
        Assert.Equal(firstEnd, detail.GetProperty("endedAtUtc").GetDateTimeOffset());
        Assert.Equal(0.1234567, detail.GetProperty("durationSeconds").GetDouble());
        Assert.Equal(JsonValueKind.Null, (await Get(client, Active)).GetProperty("active").ValueKind);
        Assert.Single((await Get(client, Collection(f))).GetProperty("sessions").EnumerateArray());
        using var delete = await Write(client, HttpMethod.Delete, Item(f, id)); NoContent(delete);
        Assert.Empty((await Get(client, Collection(f))).GetProperty("sessions").EnumerateArray());
        using var missing = await Write(client, HttpMethod.Delete, Item(f, id)); await Problem(missing, 404, "study_session_not_found");
    }

    [Fact]
    public async Task Active_conflicts_are_workspace_wide_target_is_authorized_first_and_foreign_active_is_hidden()
    {
        using var factory = Factory(new Clock()); await factory.InitializeAsync();
        using var a = factory.NewClient("192.0.2.1"); using var b = factory.NewClient("192.0.2.2");
        var wa = await Login(a, "owner-a@example.invalid"); var wb = await Login(b, "owner-b@example.invalid");
        var f = await Seed(factory, wa, wb);
        using var first = await Write(a, HttpMethod.Post, Collection(f)); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var id = (await first.Content.ReadFromJsonAsync<StudySessionResponse>())!.Id;
        foreach (var path in new[] { Collection(f), $"/api/maps/{f.Child.Id}/nodes/{f.ChildNode.Id}/study-sessions" })
        { using var conflict = await Write(a, HttpMethod.Post, path); await Problem(conflict, 409, "active_session_exists"); }
        Assert.Equal(JsonValueKind.Null, (await Get(b, Active + $"?WorkspaceId={wa.Id}")).GetProperty("active").ValueKind);
        foreach (var path in new[] { Collection(f), $"/api/maps/{f.Child.Id}/nodes/{f.First.Id}/study-sessions", $"/api/maps/{Guid.NewGuid()}/nodes/{Guid.NewGuid()}/study-sessions" })
        { using var foreign = await Write(b, HttpMethod.Post, path); await Problem(foreign, 404, "study_session_not_found"); }
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Delete })
        {
            using var mismatch = await Write(a, method, $"/api/maps/{f.Map.Id}/nodes/{f.Second.Id}/study-sessions/{id}" +
                (method == HttpMethod.Post ? "/complete" : ""));
            await Problem(mismatch, 404, "study_session_not_found");
            foreach (var path in new[] { Item(f, id), $"/api/maps/{f.Child.Id}/nodes/{f.ChildNode.Id}/study-sessions/{id}", Item(f, Guid.NewGuid()) })
            {
                using var foreign = await Write(b, method, path + (method == HttpMethod.Post ? "/complete" : ""));
                await Problem(foreign, 404, "study_session_not_found");
            }
        }
        using var discard = await Write(a, HttpMethod.Delete, Item(f, id)); NoContent(discard);
        Assert.Equal(JsonValueKind.Null, (await Get(a, Active)).GetProperty("active").ValueKind);
        using var again = await Write(a, HttpMethod.Post, Collection(f)); Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        var node = Talynor.Domain.KnowledgeMaps.KnowledgeNode.Create(f.OtherMap.Id, "Other", null, 0, 0, null, DateTimeOffset.UnixEpoch);
        db.KnowledgeNodes.Add(node); await db.SaveChangesAsync();
        using var otherStart = await Write(b, HttpMethod.Post, $"/api/maps/{f.OtherMap.Id}/nodes/{node.Id}/study-sessions");
        Assert.Equal(HttpStatusCode.Created, otherStart.StatusCode);
    }

    [Fact]
    public async Task Clock_conflict_does_not_mutate_and_zero_duration_is_valid()
    {
        var clock = new Clock(); using var factory = Factory(clock); await factory.InitializeAsync();
        using var client = factory.NewClient(); var f = await Seed(factory, await Login(client, "clock@example.invalid"));
        using var start = await Write(client, HttpMethod.Post, Collection(f));
        var id = (await start.Content.ReadFromJsonAsync<StudySessionResponse>())!.Id; var beginning = clock.Now;
        clock.Now = beginning.AddTicks(-1);
        using var invalid = await Write(client, HttpMethod.Post, Item(f, id) + "/complete"); await Problem(invalid, 409, "study_session_clock_conflict");
        Assert.Equal(JsonValueKind.Null, (await Get(client, Item(f, id))).GetProperty("endedAtUtc").ValueKind);
        clock.Now = beginning;
        using var complete = await Write(client, HttpMethod.Post, Item(f, id) + "/complete"); NoContent(complete);
        Assert.Equal(0, (await Get(client, Item(f, id))).GetProperty("durationSeconds").GetDouble());
    }

    [Theory]
    [InlineData("start")] [InlineData("complete")] [InlineData("delete")] [InlineData("active")]
    public async Task Anonymous_requests_are_no_store_401(string action)
    {
        using var factory = Factory(new Clock()); using var client = factory.NewClient();
        using var response = await client.SendAsync(Request(action, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        await Problem(response, 401, "authentication_required");
    }

    [Theory]
    [InlineData("start")] [InlineData("complete")] [InlineData("delete")] [InlineData("active")]
    public async Task Removed_account_is_signed_out_and_cannot_reach_lifecycle_or_active_data(string action)
    {
        using var factory = Factory(new Clock()); await factory.InitializeAsync(); using var client = factory.NewClient();
        await Login(client, "removed@example.invalid"); var csrf = await RegistrationTestFactory.CsrfAsync(client);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Users.ExecuteDeleteAsync();
        using var request = Request(action, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        if (action != "active") request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.SendAsync(request); await Problem(response, 401, "authentication_required");
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("__Host-Talynor.Auth=;", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("start")] [InlineData("complete")] [InlineData("delete")]
    public async Task Writes_require_csrf_and_reject_any_body_without_changing_state(string action)
    {
        using var factory = Factory(new Clock()); await factory.InitializeAsync(); using var client = factory.NewClient();
        var f = await Seed(factory, await Login(client, "csrf@example.invalid"));
        foreach (var token in new string?[] { null, "invalid-token" })
        {
            using var request = Request(action, f.Map.Id, f.First.Id, Guid.NewGuid());
            if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token);
            using var denied = await client.SendAsync(request); await Problem(denied, 400, "csrf_invalid");
            using var formRequest = Request(action, f.Map.Id, f.First.Id, Guid.NewGuid());
            formRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                { ["__RequestVerificationToken"] = token ?? "" });
            using var deniedForm = await client.SendAsync(formRequest); await Problem(deniedForm, 400, "csrf_invalid");
        }
        foreach (var payload in new[] { "{}", "{\"startedAtUtc\":\"2000-01-01\"}", " ", "null" })
        foreach (var withoutLength in new[] { false, true })
        {
            using var request = Request(action, f.Map.Id, f.First.Id, Guid.NewGuid());
            request.Content = withoutLength ? new UnknownLengthContent(Encoding.UTF8.GetBytes(payload))
                : new StringContent(payload, Encoding.UTF8, "application/json");
            request.Content.Headers.ContentType = new("application/json");
            request.Headers.Add("X-CSRF-TOKEN", await RegistrationTestFactory.CsrfAsync(client));
            using var denied = await client.SendAsync(request); await Problem(denied, 400, "validation_failed");
        }
        using var scope = factory.Services.CreateScope(); Assert.Empty(await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().StudySessions.ToArrayAsync());
    }

    [Theory]
    [InlineData("start", false, false)] [InlineData("start", false, true)]
    [InlineData("start", true, false)] [InlineData("start", true, true)]
    [InlineData("complete", false, false)] [InlineData("complete", false, true)]
    [InlineData("complete", true, false)] [InlineData("complete", true, true)]
    [InlineData("delete", false, false)] [InlineData("delete", false, true)]
    [InlineData("delete", true, false)] [InlineData("delete", true, true)]
    public async Task Writes_reject_forms_consumed_by_native_csrf_without_side_effects(
        string action, bool withoutLength, bool tokenOnly)
    {
        var probe = new BodyProbe();
        using var factory = Factory(new Clock(), services => services.AddSingleton<IStartupFilter>(probe));
        await factory.InitializeAsync(); using var client = factory.NewClient();
        var f = await Seed(factory, await Login(client, "form-body@example.invalid"));
        Guid id = Guid.NewGuid();
        if (action != "start")
        {
            using var start = await Write(client, HttpMethod.Post, Collection(f));
            Assert.Equal(HttpStatusCode.Created, start.StatusCode);
            id = (await start.Content.ReadFromJsonAsync<StudySessionResponse>())!.Id;
        }
        var fields = new Dictionary<string, string>
            { ["__RequestVerificationToken"] = await RegistrationTestFactory.CsrfAsync(client) };
        if (!tokenOnly) fields["startedAtUtc"] = "2000-01-01";
        using var form = new FormUrlEncodedContent(fields);
        using var request = Request(action, f.Map.Id, f.First.Id, id);
        request.Content = withoutLength
            ? new UnknownLengthContent(await form.ReadAsByteArrayAsync())
            : new FormUrlEncodedContent(fields);
        request.Content.Headers.ContentType = new("application/x-www-form-urlencoded");
        using var denied = await client.SendAsync(request);
        await Problem(denied, 400, "validation_failed");
        Assert.False(probe.CanSeek); Assert.False(probe.TokenInHeader);
        if (withoutLength) Assert.Null(probe.ContentLength); else Assert.True(probe.ContentLength > 0);
        Assert.True(probe.FormHasToken); Assert.Equal(0, probe.RemainingBytes);
        using var scope = factory.Services.CreateScope();
        var sessions = await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().StudySessions.AsNoTracking().ToArrayAsync();
        if (action == "start") Assert.Empty(sessions);
        else { var preserved = Assert.Single(sessions); Assert.Equal(id, preserved.Id); Assert.Null(preserved.EndedAtUtc); }
    }

    [Theory]
    [InlineData(false, null)] [InlineData(true, null)]
    [InlineData(false, "application/json")] [InlineData(true, "application/json")]
    [InlineData(false, "application/x-www-form-urlencoded")] [InlineData(true, "application/x-www-form-urlencoded")]
    public async Task Empty_writes_allow_zero_or_missing_length_even_with_content_type(bool withoutLength, string? contentType)
    {
        var probe = new BodyProbe();
        using var factory = Factory(new Clock(), services => services.AddSingleton<IStartupFilter>(probe));
        await factory.InitializeAsync(); using var client = factory.NewClient();
        var f = await Seed(factory, await Login(client, "empty-body@example.invalid"));
        Guid id = Guid.Empty;
        foreach (var action in new[] { "start", "complete", "delete" })
        {
            using var request = Request(action, f.Map.Id, f.First.Id, id);
            request.Content = withoutLength ? new UnknownLengthContent([]) : new ByteArrayContent([]);
            if (contentType is not null) request.Content.Headers.ContentType = new(contentType);
            request.Headers.Add("X-CSRF-TOKEN", await RegistrationTestFactory.CsrfAsync(client));
            using var response = await client.SendAsync(request);
            if (action == "start")
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
                id = (await response.Content.ReadFromJsonAsync<StudySessionResponse>())!.Id;
            }
            else NoContent(response);
            if (withoutLength) Assert.Null(probe.ContentLength); else Assert.Equal(0L, probe.ContentLength);
        }
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().StudySessions.ToArrayAsync());
    }

    [Theory]
    [InlineData("start")] [InlineData("complete")] [InlineData("delete")] [InlineData("active")]
    public async Task Operational_and_native_stamp_failures_are_sanitized_without_replay(string action)
    {
        var clock = new Clock(); var failed = new FailurePort();
        using var factory = Factory(clock, services => { services.AddScoped<IStudySessionStore>(_ => failed); services.AddScoped<IStudySessionReader>(_ => failed); });
        await factory.InitializeAsync(); using var client = factory.NewClient(); await Login(client, "failure@example.invalid");
        using var request = Request(action, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        if (action != "active") request.Headers.Add("X-CSRF-TOKEN", await RegistrationTestFactory.CsrfAsync(client));
        using var response = await client.SendAsync(request); await Problem(response, 500, "unexpected_error"); Assert.Equal(1, failed.Calls);
        // Force the native stamp validator to consult an unavailable database before MVC.
        clock.Now = clock.Now.AddMinutes(31);
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.ExecuteSqlRawAsync("DROP TABLE AspNetUsers");
        using var stamp = await client.GetAsync(Active); await Problem(stamp, 500, "unexpected_error");
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Deleting_last_session_releases_existing_node_or_map_delete_blocker(bool map)
    {
        using var factory = Factory(new Clock()); await factory.InitializeAsync(); using var client = factory.NewClient();
        var f = await Seed(factory, await Login(client, "release@example.invalid"));
        using var start = await Write(client, HttpMethod.Post, Collection(f)); var id = (await start.Content.ReadFromJsonAsync<StudySessionResponse>())!.Id;
        using var complete = await Write(client, HttpMethod.Post, Item(f, id) + "/complete"); NoContent(complete);
        var path = $"/api/maps/{f.Map.Id}" + (map ? "" : $"/nodes/{f.First.Id}");
        using var blocked = await Write(client, HttpMethod.Delete, path); await Problem(blocked, 409, map ? "map_in_use" : "node_in_use");
        using var delete = await Write(client, HttpMethod.Delete, Item(f, id)); NoContent(delete);
        using (var scope = factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().KnowledgeEdges.ExecuteDeleteAsync();
        using var allowed = await Write(client, HttpMethod.Delete, path); NoContent(allowed);
    }

    // TestServer exercises the real MVC/antiforgery pipeline, not Kestrel wire framing.
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }

    private sealed class BodyProbe : IStartupFilter
    {
        public long? ContentLength { get; private set; }
        public bool CanSeek { get; private set; }
        public bool TokenInHeader { get; private set; }
        public bool FormHasToken { get; private set; }
        public int RemainingBytes { get; private set; }
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                var inspect = context.Request.Path.Value?.Contains("/study-sessions", StringComparison.Ordinal) == true
                    && context.Request.Method != "GET";
                if (inspect)
                {
                    ContentLength = context.Request.ContentLength; CanSeek = context.Request.Body.CanSeek;
                    TokenInHeader = context.Request.Headers.ContainsKey("X-CSRF-TOKEN");
                }
                await continuation(context);
                if (inspect)
                {
                    FormHasToken = context.Features.Get<IFormFeature>()?.Form?.ContainsKey("__RequestVerificationToken") == true;
                    RemainingBytes = await context.Request.Body.ReadAsync(new byte[1]);
                }
            });
            next(app);
        };
    }

    internal sealed class Clock : TimeProvider
    { public DateTimeOffset Now = new(2026, 10, 18, 14, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    private static RegistrationTestFactory Factory(Clock clock, Action<IServiceCollection>? extra = null) => new(configureServices: services =>
    {
        services.AddSingleton<TimeProvider>(clock);
        services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = clock);
        services.PostConfigure<SecurityStampValidatorOptions>(options => options.TimeProvider = clock);
        extra?.Invoke(services);
    });
    private static HttpRequestMessage Request(string action, Guid map, Guid node, Guid id)
    {
        var list = $"/api/maps/{map}/nodes/{node}/study-sessions";
        return action switch { "start" => new(HttpMethod.Post, list), "complete" => new(HttpMethod.Post, $"{list}/{id}/complete"),
            "delete" => new(HttpMethod.Delete, $"{list}/{id}"), _ => new(HttpMethod.Get, Active) };
    }
    private static async Task<PersonalWorkspaceResponse> Login(HttpClient client, string email)
    {
        const string password = "synthetic session lifecycle passphrase";
        using var register = await RegistrationTestFactory.RegisterAsync(client, new { email, password }, await RegistrationTestFactory.CsrfAsync(client));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { email, password }) };
        request.Headers.Add("X-CSRF-TOKEN", await RegistrationTestFactory.CsrfAsync(client));
        using var login = await client.SendAsync(request); Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return (await client.GetFromJsonAsync<PersonalWorkspaceResponse>("/api/workspace"))!;
    }
    private static async Task<KnowledgeGraphFixture> Seed(RegistrationTestFactory factory, PersonalWorkspaceResponse owner, PersonalWorkspaceResponse? other = null)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        return await KnowledgeGraphFixture.CreateAsync(db, await db.PersonalWorkspaces.SingleAsync(w => w.Id == owner.Id),
            other is null ? null : await db.PersonalWorkspaces.SingleAsync(w => w.Id == other.Id));
    }
    private static async Task<HttpResponseMessage> Write(HttpClient client, HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, path); request.Headers.Add("X-CSRF-TOKEN", await RegistrationTestFactory.CsrfAsync(client));
        return await client.SendAsync(request);
    }
    private static async Task<JsonElement> Get(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task Problem(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync(); using var json = JsonDocument.Parse(body); Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        foreach (var marker in new[] { "SQLite", "Exception", "Private failure", "AspNetUsers", "OwnerUserId", "WorkspaceId" }) Assert.DoesNotContain(marker, body);
    }
    private static void NoContent(HttpResponseMessage response) { Assert.Equal(HttpStatusCode.NoContent, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore); }
    private static void Properties(JsonElement value, params string[] names) => Assert.Equal(names.Order(), value.EnumerateObject().Select(p => p.Name).Order());
    private sealed class FailurePort : IStudySessionStore, IStudySessionReader
    {
        public int Calls;
        private T Fail<T>() { Calls++; throw new InvalidOperationException("Private failure"); }
        public Task<StartStudySessionResult> StartAsync(Guid w, Guid m, StudySession s, CancellationToken ct) => Fail<Task<StartStudySessionResult>>();
        public Task<StudySessionWriteStatus> CompleteAsync(Guid w, Guid m, Guid n, Guid s, DateTimeOffset now, CancellationToken ct) => Fail<Task<StudySessionWriteStatus>>();
        public Task<StudySessionWriteStatus> DeleteAsync(Guid w, Guid m, Guid n, Guid s, CancellationToken ct) => Fail<Task<StudySessionWriteStatus>>();
        public Task<ActiveStudySession?> GetActiveAsync(Guid w, CancellationToken ct) => Fail<Task<ActiveStudySession?>>();
        public Task<StudySessionPage?> ListCompletedForNodeAsync(Guid w, Guid m, Guid n, int offset, int size, CancellationToken ct) => throw new NotSupportedException();
        public Task<StudySessionReadModel?> GetForNodeAsync(Guid w, Guid m, Guid n, Guid s, CancellationToken ct) => throw new NotSupportedException();
    }
}

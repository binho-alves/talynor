using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class RegistrationTests
{
    // Synthetic test input only; never a seeded or usable development credential.
    private const string Password = "  uma frase longa com ç e espaços  ";

    [Fact]
    public async Task Registration_persists_unconfirmed_account_and_hash_without_authentication()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        var token = await RegistrationTestFactory.CsrfAsync(client);
        using var response = await RegistrationTestFactory.RegisterAsync(client,
            new { email = "  Estudo@example.invalid  ", password = Password }, token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Null(response.Headers.Location);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) &&
            cookies.Any(cookie => cookie.StartsWith("__Host-Talynor.Auth=", StringComparison.Ordinal)));

        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Users.SingleAsync();
        Assert.Equal("Estudo@example.invalid", user.Email);
        Assert.Equal(user.Email, user.UserName);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        Assert.Equal(users.NormalizeEmail(user.Email), user.NormalizedEmail);
        Assert.Equal(users.NormalizeName(user.UserName), user.NormalizedUserName);
        Assert.False(user.EmailConfirmed);
        Assert.False(string.IsNullOrEmpty(user.PasswordHash));
        Assert.True(await users.CheckPasswordAsync(user, Password));
        Assert.False(await users.CheckPasswordAsync(user, Password.Trim()));
    }

    [Theory]
    [InlineData("without uppercase or digits")]
    [InlineData("uma frase longa com espaços")]
    [InlineData("frase com espaços e Unicode çá")]
    public async Task Password_composition_and_spaces_are_not_restricted(string password)
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        using var response = await RegistrationTestFactory.RegisterAsync(client,
            new { email = "usuário@example.invalid", password }, await RegistrationTestFactory.CsrfAsync(client));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("missing-email")]
    [InlineData("blank-email")]
    [InlineData("invalid-email")]
    [InlineData("long-email")]
    [InlineData("missing-password")]
    [InlineData("empty-password")]
    [InlineData("long-password")]
    [InlineData("short-password")]
    [InlineData("blank-password")]
    public async Task Invalid_fields_return_safe_validation_without_creating_an_account(string scenario)
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        var input = new Dictionary<string, string?> { ["email"] = "study@example.invalid", ["password"] = Password };
        switch (scenario)
        {
            case "missing-email": input.Remove("email"); break;
            case "blank-email": input["email"] = " \t "; break;
            case "invalid-email": input["email"] = "not-an-email"; break;
            case "long-email": input["email"] = new string('a', 245) + "@example.invalid"; break;
            case "missing-password": input.Remove("password"); break;
            case "empty-password": input["password"] = ""; break;
            case "long-password": input["password"] = new string('a', 129); break;
            case "short-password": input["password"] = "short"; break;
            case "blank-password": input["password"] = new string(' ', 15); break;
        }
        using var response = await RegistrationTestFactory.RegisterAsync(client, input,
            await RegistrationTestFactory.CsrfAsync(client));
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(0, await CountUsersAsync(factory));
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"email\":{\"submitted-secret\":1},\"password\":false}")]
    public async Task Invalid_json_or_missing_body_does_not_expose_parser_diagnostics(string json)
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
            { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-CSRF-TOKEN", await RegistrationTestFactory.CsrfAsync(client));
        using var response = await client.SendAsync(request);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.DoesNotContain("submitted-secret", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await CountUsersAsync(factory));
    }

    [Fact]
    public async Task Unsupported_content_type_with_valid_csrf_returns_safe_415_without_creating_an_account()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        var token = await RegistrationTestFactory.CsrfAsync(client);
        var email = "unsupported-content@example.invalid";
        var password = "synthetic-payload-password-marker";
        var payload = JsonSerializer.Serialize(new { email, password });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
            { Content = new StringContent(payload, Encoding.UTF8, "text/plain") };
        request.Headers.Add("X-CSRF-TOKEN", token);

        using var response = await client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType, "validation_failed");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("O tipo de conteúdo da solicitação não é suportado.",
            json.RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain(payload, body);
        Assert.DoesNotContain(email, body);
        Assert.DoesNotContain(password, body);
        Assert.DoesNotContain(token, body);
        Assert.False(json.RootElement.TryGetProperty("detail", out _));
        Assert.False(json.RootElement.TryGetProperty("errors", out _));
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(0, await CountUsersAsync(factory));
    }

    [Fact]
    public async Task Limits_are_checked_after_email_trim_and_password_is_accepted_at_128_units()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        var email = new string('a', 240) + "@example.invalid"; // Exactly 256 units.
        using var response = await RegistrationTestFactory.RegisterAsync(client,
            new { email = "  " + email + "  ", password = new string('a', 128) },
            await RegistrationTestFactory.CsrfAsync(client));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("study@example.invalid")]
    [InlineData("STUDY@EXAMPLE.INVALID")]
    [InlineData("  study@example.invalid  ")]
    public async Task Duplicate_accounts_have_generic_response_and_leave_only_one_account(string duplicate)
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        var token = await RegistrationTestFactory.CsrfAsync(client);
        using var first = await RegistrationTestFactory.RegisterAsync(client,
            new { email = "study@example.invalid", password = Password }, token);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var second = await RegistrationTestFactory.RegisterAsync(client,
            new { email = duplicate, password = Password }, token);
        await AssertProblemAsync(second, HttpStatusCode.BadRequest, "registration_failed");
        var body = await second.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Duplicate", body);
        Assert.DoesNotContain("study", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await CountUsersAsync(factory));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("other-client")]
    public async Task Native_antiforgery_rejects_missing_invalid_or_mismatched_token(string scenario)
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await RegistrationTestFactory.CsrfAsync(client);
        string? token = null;
        if (scenario == "invalid") token = "not-a-token";
        if (scenario == "other-client")
        {
            using var other = factory.NewClient();
            token = await RegistrationTestFactory.CsrfAsync(other);
        }
        using var response = await RegistrationTestFactory.RegisterAsync(client,
            new { email = "study@example.invalid", password = Password }, token);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "csrf_invalid");
        Assert.Equal(0, await CountUsersAsync(factory));
    }

    [Fact]
    public async Task Existing_authenticated_session_cannot_register_another_account()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "synthetic-existing-session"),
             new Claim(ClaimTypes.Name, "existing@example.invalid")], IdentityConstants.ApplicationScheme));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8), IsPersistent = false
        }, IdentityConstants.ApplicationScheme);
        client.DefaultRequestHeaders.Add("Cookie", "__Host-Talynor.Auth=" + options.TicketDataFormat.Protect(ticket));
        using var response = await RegistrationTestFactory.RegisterAsync(client,
            new { email = "another@example.invalid", password = Password },
            await RegistrationTestFactory.CsrfAsync(client));
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "already_authenticated");
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(0, await CountUsersAsync(factory));
    }

    [Fact]
    public async Task Concurrent_equivalent_registrations_hit_the_unique_index_and_create_one_account()
    {
        var race = new RegistrationRaceInterceptor();
        using var factory = new RegistrationTestFactory(race);
        await factory.InitializeAsync();
        using var first = factory.NewClient();
        using var second = factory.NewClient();
        var firstToken = await RegistrationTestFactory.CsrfAsync(first);
        var secondToken = await RegistrationTestFactory.CsrfAsync(second);
        var responses = await Task.WhenAll(
            RegistrationTestFactory.RegisterAsync(first, new { email = "race@example.invalid", password = Password }, firstToken),
            RegistrationTestFactory.RegisterAsync(second, new { email = " RACE@EXAMPLE.INVALID ", password = Password }, secondToken))
            .WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            var rejected = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
            await AssertProblemAsync(rejected, HttpStatusCode.BadRequest, "registration_failed");
            Assert.Equal(2, race.Arrivals);
            Assert.Equal(1, race.UniqueFailures);
            Assert.Equal(1, await CountUsersAsync(factory));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Theory]
    [InlineData(5, 5, "database is locked")]
    [InlineData(19, 1555, "UNIQUE constraint failed: AspNetUsers.Id")]
    [InlineData(19, 2067, "UNIQUE constraint failed: AspNetUsers.NormalizedEmail")]
    public async Task Other_persistence_failures_are_operational_errors_not_duplicates(int code, int extended, string diagnostic)
    {
        using var factory = new RegistrationTestFactory(new FailureInterceptor(code, extended, diagnostic));
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        using var response = await RegistrationTestFactory.RegisterAsync(client,
            new { email = "study@example.invalid", password = Password },
            await RegistrationTestFactory.CsrfAsync(client));
        await AssertProblemAsync(response, HttpStatusCode.InternalServerError, "unexpected_error");
        Assert.DoesNotContain(diagnostic, await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await CountUsersAsync(factory));
    }

    [Fact]
    public async Task Five_requests_rejected_by_csrf_consume_missing_ip_quota_and_sixth_is_rate_limited()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            client.DefaultRequestHeaders.Remove("X-Forwarded-For");
            client.DefaultRequestHeaders.Add("X-Forwarded-For", $"192.0.2.{attempt + 1}");
            using var response = await RegistrationTestFactory.RegisterAsync(client, new { }, null);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "csrf_invalid");
        }
        using var limited = await RegistrationTestFactory.RegisterAsync(client, new { }, null);
        await AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.NotNull(limited.Headers.RetryAfter?.Delta);
        using var csrf = await client.GetAsync("/api/auth/csrf");
        using var map = await client.GetAsync("/api/knowledge-maps/software");
        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        Assert.Equal(HttpStatusCode.OK, map.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(0, await CountUsersAsync(factory));
    }

    [Fact]
    public async Task Distinct_ips_have_independent_quotas_and_mapped_ipv4_shares_its_ipv4_quota()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var first = factory.NewClient("192.0.2.1");
        using var mapped = factory.NewClient("::ffff:192.0.2.1");
        using var other = factory.NewClient("192.0.2.2");
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var admitted = await RegistrationTestFactory.RegisterAsync(first, new { }, null);
            Assert.Equal(HttpStatusCode.BadRequest, admitted.StatusCode);
        }
        using var limited = await RegistrationTestFactory.RegisterAsync(mapped, new { }, null);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var independent = await RegistrationTestFactory.RegisterAsync(other, new { }, null);
        Assert.Equal(HttpStatusCode.BadRequest, independent.StatusCode);
        Assert.Equal(0, await CountUsersAsync(factory));
    }

    private static async Task<int> CountUsersAsync(RegistrationTestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Users.CountAsync();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)status, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.False(json.RootElement.TryGetProperty("exception", out _));
    }

    private sealed class RegistrationRaceInterceptor : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        private int uniqueFailures;
        public int Arrivals => arrivals;
        public int UniqueFailures => uniqueFailures;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<IdentityUser>().Any(entry => entry.State == EntityState.Added))
            {
                if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Exception.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
            {
                Interlocked.Increment(ref uniqueFailures);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FailureInterceptor(int code, int extended, string diagnostic) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Synthetic persistence fault.",
                new SqliteException($"SQLite Error {code}: '{diagnostic}'.", code, extended));
    }
}

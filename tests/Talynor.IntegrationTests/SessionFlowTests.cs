using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Talynor.Api.Contracts;
using Talynor.Application.Accounts;

namespace Talynor.IntegrationTests;

public class SessionFlowTests
{
    // Synthetic inputs confined to disposable test databases; no Development account is seeded.
    private const string Email = "session@example.invalid";
    private const string Password = "  uma frase de sessão com ç  ";

    [Fact]
    public async Task Login_emits_real_nonpersistent_cookie_and_me_returns_only_current_id_email()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await RegisterAsync(client);
        using var response = await LoginAsync(client, "  SESSION@EXAMPLE.INVALID  ", Password);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
        var cookie = Assert.Single(Cookies(response));
        Assert.Equal("__Host-Talynor.Auth", cookie.Name.ToString());
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Lax, cookie.SameSite);
        Assert.Equal("/", cookie.Path.ToString());
        Assert.False(cookie.Domain.HasValue);
        Assert.Null(cookie.Expires);
        Assert.Null(cookie.MaxAge);
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var ticket = options.TicketDataFormat.Unprotect(cookie.Value.ToString());
        Assert.NotNull(ticket);
        Assert.False(ticket.Properties.IsPersistent);
        Assert.Equal(TimeSpan.FromHours(8), ticket.Properties.ExpiresUtc - ticket.Properties.IssuedUtc);
        Assert.False(options.SlidingExpiration);
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>().FindByEmailAsync(Email);
        Assert.NotNull(user);
        Assert.False(user.EmailConfirmed);
        await RegistrationTestFactory.CsrfAsync(client); // Refresh across anonymous -> authenticated identity.
        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.True(me.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "email", "id" }, json.RootElement.EnumerateObject().Select(x => x.Name).OrderBy(x => x));
        Assert.Equal(user.Id, json.RootElement.GetProperty("id").GetString());
        Assert.Equal(Email, json.RootElement.GetProperty("email").GetString());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("other-client")]
    public async Task Login_requires_a_matching_csrf_pair_without_issuing_auth_cookie(string scenario)
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await RegisterAsync(client);
        string? token = null;
        if (scenario == "invalid") token = "invalid-token";
        if (scenario == "other-client")
        {
            using var other = factory.NewClient();
            token = await RegistrationTestFactory.CsrfAsync(other);
        }
        using var response = await PostAsync(client, "login", new { email = Email, password = Password }, token);
        await ProblemAsync(response, HttpStatusCode.BadRequest, "csrf_invalid");
        AssertNoAuthIssuance(response);
    }

    [Theory]
    [InlineData("missing-email")]
    [InlineData("invalid-email")]
    [InlineData("blank-email")]
    [InlineData("long-email")]
    [InlineData("missing-password")]
    [InlineData("empty-password")]
    [InlineData("long-password")]
    public async Task Login_validates_structure_and_utf16_limits(string scenario)
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        var input = new Dictionary<string, string?> { ["email"] = Email, ["password"] = Password };
        switch (scenario)
        {
            case "missing-email": input.Remove("email"); break;
            case "invalid-email": input["email"] = "not-an-email"; break;
            case "blank-email": input["email"] = "  "; break;
            case "long-email": input["email"] = new string('a', 241) + "@example.invalid"; break;
            case "missing-password": input.Remove("password"); break;
            case "empty-password": input["password"] = ""; break;
            case "long-password": input["password"] = new string('a', 129); break;
        }
        using var response = await PostAsync(client, "login", input, await RegistrationTestFactory.CsrfAsync(client));
        await ProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        AssertNoAuthIssuance(response);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"email\":{\"private-marker\":1},\"password\":false}")]
    public async Task Login_parser_errors_are_sanitized(string payload)
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-CSRF-TOKEN", await RegistrationTestFactory.CsrfAsync(client));
        using var response = await client.SendAsync(request);
        await ProblemAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.DoesNotContain("private-marker", await response.Content.ReadAsStringAsync());
        AssertNoAuthIssuance(response);
    }

    [Fact]
    public async Task Missing_account_wrong_password_and_not_allowed_have_identical_public_failure()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await RegisterAsync(client);
        using var missing = await LoginAsync(client, "missing@example.invalid", Password);
        using var wrong = await LoginAsync(client, Email, "wrong"); // No registration minimum in login validation.
        using (var scope = factory.Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value.SignIn.RequireConfirmedEmail = true;
        using var notAllowed = await LoginAsync(client);
        foreach (var response in new[] { missing, wrong, notAllowed })
        {
            await ProblemAsync(response, HttpStatusCode.Unauthorized, "invalid_credentials");
            AssertNoAuthIssuance(response);
        }
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await wrong.Content.ReadAsStringAsync());
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await notAllowed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_does_not_trim_password_or_apply_current_creation_policy()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await RegisterAsync(client);
        using var wrong = await LoginAsync(client, Email, Password.Trim());
        await ProblemAsync(wrong, HttpStatusCode.Unauthorized, "invalid_credentials");
        using (var scope = factory.Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value.Password.RequiredLength = 100;
        using var success = await LoginAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
    }

    [Fact]
    public async Task Login_accepts_exact_limits_after_email_trim()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        var email = new string('a', 240) + "@example.invalid";
        var password = new string('ç', 128);
        await RegisterAsync(client, email, password);
        using var response = await LoginAsync(client, "  " + email + "  ", password);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Existing_session_cannot_switch_account_or_issue_another_cookie()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        using var other = factory.NewClient();
        await RegisterAsync(client);
        await RegisterAsync(other, "other@example.invalid");
        using var login = await LoginAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using var conflict = await LoginAsync(client, "other@example.invalid", Password);
        await ProblemAsync(conflict, HttpStatusCode.Conflict, "already_authenticated");
        AssertNoAuthIssuance(conflict);
        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(Email, (await me.Content.ReadFromJsonAsync<CurrentUserResponse>())!.Email);
    }

    [Fact]
    public async Task Existing_session_after_stamp_interval_is_409_without_native_cookie_renewal_on_login_failure()
    {
        var clock = new SessionClock();
        using var factory = new RegistrationTestFactory(configureServices: services =>
        {
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = clock);
            services.PostConfigure<SecurityStampValidatorOptions>(options => options.TimeProvider = clock);
        });
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await RegisterAsync(client);
        using var login = await LoginAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var token = await RegistrationTestFactory.CsrfAsync(client);
        clock.Advance(TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(1));
        using var conflict = await PostAsync(client, "login", new { email = Email, password = Password }, token);
        await ProblemAsync(conflict, HttpStatusCode.Conflict, "already_authenticated");
        AssertNoAuthIssuance(conflict);
        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        // Native validation/renewal is still available on an ordinary successful authenticated request.
        Assert.Contains(Cookies(me), cookie => cookie.Name == "__Host-Talynor.Auth" && cookie.Value.ToString().Length > 0);
    }

    [Fact]
    public async Task Lockout_uses_five_attempts_five_minutes_and_success_resets_counter()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await RegisterAsync(client);
        string? failureBody = null;
        var before = DateTimeOffset.UtcNow;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var failure = await LoginAsync(client, Email, "wrong");
            await ProblemAsync(failure, HttpStatusCode.Unauthorized, "invalid_credentials");
            AssertNoAuthIssuance(failure);
            var body = await failure.Content.ReadAsStringAsync();
            failureBody ??= body;
            Assert.Equal(failureBody, body);
            using var scope = factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = (await users.FindByEmailAsync(Email))!;
            Assert.True(user.LockoutEnabled);
            Assert.Equal(attempt < 5 ? attempt : 0, user.AccessFailedCount);
            Assert.Equal(attempt == 5, await users.IsLockedOutAsync(user));
            if (attempt == 5)
                Assert.InRange(user.LockoutEnd!.Value, before.AddMinutes(5), DateTimeOffset.UtcNow.AddMinutes(5));
        }
        using var locked = await LoginAsync(client);
        await ProblemAsync(locked, HttpStatusCode.Unauthorized, "invalid_credentials");
        Assert.Equal(failureBody, await locked.Content.ReadAsStringAsync());
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = (await users.FindByEmailAsync(Email))!;
            Assert.True((await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(-1))).Succeeded);
        }
        using var extraFailure = await LoginAsync(client, Email, "wrong");
        await ProblemAsync(extraFailure, HttpStatusCode.Unauthorized, "invalid_credentials");
        using var success = await LoginAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
        using var finalScope = factory.Services.CreateScope();
        Assert.Equal(0, (await finalScope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>()
            .FindByEmailAsync(Email))!.AccessFailedCount);
    }

    [Fact]
    public async Task Login_quota_normalizes_ips_isolates_hosts_and_preserves_registration_quota()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient("192.0.2.70");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.1");
        for (var i = 0; i < 10; i++)
        {
            using var admitted = await PostAsync(client, "login", new { }, null);
            await ProblemAsync(admitted, HttpStatusCode.BadRequest, "csrf_invalid");
        }
        using var mapped = factory.NewClient("::ffff:192.0.2.70");
        using var rejected = await PostAsync(mapped, "login", new { }, null);
        await ProblemAsync(rejected, HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.NotNull(rejected.Headers.RetryAfter);
        AssertNoAuthIssuance(rejected);
        using var other = factory.NewClient("192.0.2.71");
        using var independent = await PostAsync(other, "login", new { }, null);
        Assert.Equal(HttpStatusCode.BadRequest, independent.StatusCode);
        using var register = await RegistrationTestFactory.RegisterAsync(client, new { }, null);
        Assert.Equal(HttpStatusCode.BadRequest, register.StatusCode); // Login quota is not registration quota.
        using var freshFactory = new RegistrationTestFactory();
        await freshFactory.InitializeAsync();
        using var fresh = freshFactory.NewClient("192.0.2.70");
        using var newWindow = await PostAsync(fresh, "login", new { }, null);
        Assert.Equal(HttpStatusCode.BadRequest, newWindow.StatusCode);
    }

    [Fact]
    public async Task Missing_ip_uses_one_shared_restricted_bucket()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var first = factory.NewClient();
        using var second = factory.NewClient();
        for (var i = 0; i < 10; i++)
        {
            using var response = await PostAsync(i % 2 == 0 ? first : second, "login", new { }, null);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var rejected = await PostAsync(second, "login", new { }, null);
        await ProblemAsync(rejected, HttpStatusCode.TooManyRequests, "rate_limited");
    }

    [Theory]
    [InlineData("me", "GET")]
    [InlineData("logout", "POST")]
    public async Task Anonymous_protected_endpoints_return_json_401_without_redirect(string endpoint, string method)
    {
        using var factory = new RegistrationTestFactory();
        using var client = factory.NewClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/auth/" + endpoint);
        using var response = await client.SendAsync(request);
        await ProblemAsync(response, HttpStatusCode.Unauthorized, "authentication_required");
        Assert.False(response.Headers.Contains("Location"));
    }

    [Fact]
    public async Task Me_reads_current_email_by_id_and_removed_account_removes_local_cookie()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await RegisterAsync(client);
        using var login = await LoginAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = (await users.FindByEmailAsync(Email))!;
            Assert.True((await users.SetEmailAsync(user, "updated@example.invalid")).Succeeded);
        }
        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("updated@example.invalid", (await me.Content.ReadFromJsonAsync<CurrentUserResponse>())!.Email);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True((await users.DeleteAsync((await users.FindByEmailAsync("updated@example.invalid"))!)).Succeeded);
        }
        using var removed = await client.GetAsync("/api/auth/me");
        await ProblemAsync(removed, HttpStatusCode.Unauthorized, "authentication_required");
        AssertDeletedAuthCookie(removed);
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("missing-id")]
    [InlineData("ambiguous-id")]
    public async Task Me_fails_closed_for_invalid_cookie_or_identifier(string scenario)
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        var cookie = "tampered-ticket";
        if (scenario != "tampered")
        {
            using var scope = factory.Services.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(IdentityConstants.ApplicationScheme);
            var identity = new ClaimsIdentity(IdentityConstants.ApplicationScheme);
            if (scenario == "ambiguous-id")
            {
                identity.AddClaim(new(ClaimTypes.NameIdentifier, "one"));
                identity.AddClaim(new(ClaimTypes.NameIdentifier, "two"));
            }
            cookie = options.TicketDataFormat.Protect(new AuthenticationTicket(new ClaimsPrincipal(identity),
                new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) },
                IdentityConstants.ApplicationScheme));
        }
        client.DefaultRequestHeaders.Add("Cookie", "__Host-Talynor.Auth=" + cookie);
        using var me = await client.GetAsync("/api/auth/me");
        await ProblemAsync(me, HttpStatusCode.Unauthorized, "authentication_required");
    }

    [Theory]
    [InlineData("login")]
    [InlineData("me")]
    [InlineData("me-stamp")]
    [InlineData("me-stamp-slash")]
    public async Task Real_database_failure_is_sanitized_500_not_invalid_credentials_or_missing_user(string endpoint)
    {
        var clock = new SessionClock();
        using var factory = new RegistrationTestFactory(configureServices: services =>
        {
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = clock);
            services.PostConfigure<SecurityStampValidatorOptions>(options => options.TimeProvider = clock);
        });
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await RegisterAsync(client);
        if (endpoint != "login")
        {
            using var login = await LoginAsync(client);
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        }
        // Deliberately remove only the disposable fixture table: a real provider failure, no production database.
        using (var scope = factory.Services.CreateScope())
            await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(
                scope.ServiceProvider.GetRequiredService<Talynor.Infrastructure.Persistence.TalynorDbContext>().Database,
                "DROP TABLE AspNetUsers");
        if (endpoint.StartsWith("me-stamp", StringComparison.Ordinal))
            clock.Advance(TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(1));
        using var response = endpoint != "login"
            ? await client.GetAsync(endpoint == "me-stamp-slash" ? "/API/AUTH/ME/" : "/api/auth/me")
            : await LoginAsync(client);
        await ProblemAsync(response, HttpStatusCode.InternalServerError, "unexpected_error");
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("AspNetUsers", body);
        Assert.DoesNotContain("SQLite", body);
        Assert.DoesNotContain(Email, body);
        AssertNoAuthIssuance(response);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("pre-login")]
    [InlineData("other-client")]
    public async Task Logout_requires_authenticated_csrf_token_without_ending_session_on_failure(string scenario)
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await RegisterAsync(client);
        var anonymousToken = await RegistrationTestFactory.CsrfAsync(client);
        using var login = await PostAsync(client, "login", new { email = Email, password = Password }, anonymousToken);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        string? token = scenario switch { "invalid" => "invalid", "pre-login" => anonymousToken, _ => null };
        if (scenario == "other-client")
        {
            using var other = factory.NewClient();
            using var otherLogin = await LoginAsync(other);
            Assert.Equal(HttpStatusCode.NoContent, otherLogin.StatusCode);
            token = await RegistrationTestFactory.CsrfAsync(other);
        }
        using var response = await PostAsync(client, "logout", null, token);
        await ProblemAsync(response, HttpStatusCode.BadRequest, "csrf_invalid");
        AssertNoAuthIssuance(response);
        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Logout_is_local_other_browser_survives_and_csrf_follows_identity_changes()
    {
        using var factory = new RegistrationTestFactory();
        await factory.InitializeAsync();
        using var first = factory.NewClient();
        using var second = factory.NewClient();
        await RegisterAsync(first);
        using var firstLogin = await LoginAsync(first);
        using var secondLogin = await LoginAsync(second);
        Assert.Equal(HttpStatusCode.NoContent, firstLogin.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondLogin.StatusCode);
        var token = await RegistrationTestFactory.CsrfAsync(first);
        using var logout = await PostAsync(first, "logout", null, token);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.True(logout.Headers.CacheControl?.NoStore);
        AssertDeletedAuthCookie(logout);
        using var me = await first.GetAsync("/api/auth/me");
        await ProblemAsync(me, HttpStatusCode.Unauthorized, "authentication_required");
        using var otherMe = await second.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, otherMe.StatusCode);
        using var stale = await PostAsync(first, "login", new { email = Email, password = Password }, token);
        await ProblemAsync(stale, HttpStatusCode.BadRequest, "csrf_invalid");
        var anonymous = await RegistrationTestFactory.CsrfAsync(first);
        using var relogin = await PostAsync(first, "login", new { email = Email, password = Password }, anonymous);
        Assert.Equal(HttpStatusCode.NoContent, relogin.StatusCode);
    }

    [Fact]
    public async Task Real_login_cookie_is_rejected_after_security_stamp_interval_and_expired_ticket()
    {
        var clock = new SessionClock();
        using var factory = new RegistrationTestFactory(configureServices: services =>
        {
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = clock);
            services.PostConfigure<SecurityStampValidatorOptions>(options => options.TimeProvider = clock);
        });
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await RegisterAsync(client);
        using var login = await LoginAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True((await users.UpdateSecurityStampAsync((await users.FindByEmailAsync(Email))!)).Succeeded);
        }
        clock.Advance(TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(1));
        using var rejected = await client.GetAsync("/api/auth/me");
        await ProblemAsync(rejected, HttpStatusCode.Unauthorized, "authentication_required");
        AssertDeletedAuthCookie(rejected);
        Assert.Contains(Cookies(rejected), cookie => cookie.Name == IdentityConstants.TwoFactorRememberMeScheme &&
            cookie.Value.ToString() == string.Empty);
        using var relogin = await LoginAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, relogin.StatusCode);
        clock.Advance(TimeSpan.FromHours(8) + TimeSpan.FromSeconds(1));
        using var expired = await client.GetAsync("/api/auth/me");
        await ProblemAsync(expired, HttpStatusCode.Unauthorized, "authentication_required");
    }

    [Fact]
    public async Task Valid_security_stamp_refresh_preserves_original_expiration_and_renewed_cookie_expires()
    {
        var clock = new SessionClock();
        using var factory = new RegistrationTestFactory(configureServices: services =>
        {
            services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.TimeProvider = clock);
            services.PostConfigure<SecurityStampValidatorOptions>(options => options.TimeProvider = clock);
        });
        await factory.InitializeAsync();
        using var client = factory.NewClient();
        await RegisterAsync(client);
        using var login = await LoginAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var initialCookie = Assert.Single(Cookies(login), cookie => cookie.Name == "__Host-Talynor.Auth");
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var initialTicket = options.TicketDataFormat.Unprotect(initialCookie.Value.ToString());
        Assert.NotNull(initialTicket);
        // AuthenticationProperties stores ticket timestamps with whole-second precision.
        var loginTime = DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds());
        Assert.Equal(loginTime, initialTicket.Properties.IssuedUtc);
        Assert.Equal(loginTime.AddHours(8), initialTicket.Properties.ExpiresUtc);
        Assert.False(options.SlidingExpiration);

        var validationInterval = factory.Services.GetRequiredService<IOptions<SecurityStampValidatorOptions>>()
            .Value.ValidationInterval;
        clock.Advance(validationInterval + TimeSpan.FromSeconds(1));
        using var refreshed = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var renewedCookie = Assert.Single(Cookies(refreshed), cookie => cookie.Name == "__Host-Talynor.Auth");
        var renewedTicket = options.TicketDataFormat.Unprotect(renewedCookie.Value.ToString());
        Assert.NotNull(renewedTicket);
        Assert.True(renewedTicket.Principal.Identity?.IsAuthenticated);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds()),
            renewedTicket.Properties.IssuedUtc);
        Assert.True(renewedTicket.Properties.IssuedUtc > initialTicket.Properties.IssuedUtc);
        Assert.Equal(initialTicket.Properties.ExpiresUtc, renewedTicket.Properties.ExpiresUtc);
        Assert.False(renewedTicket.Properties.IsPersistent);

        clock.Advance(initialTicket.Properties.ExpiresUtc!.Value - clock.GetUtcNow() + TimeSpan.FromSeconds(1));
        // The real client cookie jar now sends the cookie renewed by the native handler.
        using var expired = await client.GetAsync("/api/auth/me");
        await ProblemAsync(expired, HttpStatusCode.Unauthorized, "authentication_required");
    }

    private static async Task RegisterAsync(HttpClient client, string email = Email, string password = Password)
    {
        using var response = await RegistrationTestFactory.RegisterAsync(client, new { email, password },
            await RegistrationTestFactory.CsrfAsync(client));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email = Email, string password = Password) =>
        await PostAsync(client, "login", new { email, password }, await RegistrationTestFactory.CsrfAsync(client));

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string action, object? body, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/" + action);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static SetCookieHeaderValue[] Cookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(value => SetCookieHeaderValue.Parse(value)).ToArray() : [];

    private static void AssertNoAuthIssuance(HttpResponseMessage response) =>
        Assert.DoesNotContain(Cookies(response), cookie => cookie.Name == "__Host-Talynor.Auth" ||
            cookie.Name == IdentityConstants.TwoFactorRememberMeScheme);

    private static void AssertDeletedAuthCookie(HttpResponseMessage response)
    {
        var cookie = Assert.Single(Cookies(response), cookie => cookie.Name == "__Host-Talynor.Auth");
        Assert.Equal(string.Empty, cookie.Value.ToString());
        Assert.NotNull(cookie.Expires);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Location"));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)status, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.False(json.RootElement.TryGetProperty("detail", out _));
        Assert.False(json.RootElement.TryGetProperty("exception", out _));
    }

    private sealed class SessionClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }
}

using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Talynor.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace Talynor.IntegrationTests;

// Regression derived from the missing-remember-me-handler diagnostic against Identity 10.0.12.
public class SecurityStampDiagnosticTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Runtime_configuration_has_only_application_and_technical_remember_me_with_native_validator()
    {
        using var factory = new DiagnosticFactory(controlTime: false);
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var schemes = services.GetRequiredService<IAuthenticationSchemeProvider>();
        var registered = (await schemes.GetAllSchemesAsync()).ToArray();
        Assert.Equal(new[] { IdentityConstants.ApplicationScheme, IdentityConstants.TwoFactorRememberMeScheme },
            registered.Select(scheme => scheme.Name).OrderBy(name => name).ToArray());
        output.WriteLine("Registered schemes: " + string.Join(", ", registered.Select(scheme => scheme.Name)));
        foreach (var name in new[] { IdentityConstants.TwoFactorUserIdScheme, IdentityConstants.ExternalScheme })
        {
            Assert.Null(await schemes.GetSchemeAsync(name));
            output.WriteLine(name + ": absent");
        }

        var defaults = new[]
        {
            ("Authenticate", await schemes.GetDefaultAuthenticateSchemeAsync()),
            ("Challenge", await schemes.GetDefaultChallengeSchemeAsync()),
            ("Forbid", await schemes.GetDefaultForbidSchemeAsync()),
            ("SignIn", await schemes.GetDefaultSignInSchemeAsync())
        };
        foreach (var (operation, scheme) in defaults)
        {
            Assert.NotNull(scheme);
            Assert.Equal(IdentityConstants.ApplicationScheme, scheme.Name);
            output.WriteLine($"Default {operation}: {scheme.Name}");
        }

        var validator = services.GetRequiredService<ISecurityStampValidator>();
        Assert.IsType<SecurityStampValidator<IdentityUser>>(validator);
        var stampOptions = services.GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value;
        Assert.Equal(TimeSpan.FromMinutes(30), stampOptions.ValidationInterval);
        var cookie = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        Func<CookieValidatePrincipalContext, Task> callback = SecurityStampValidator.ValidatePrincipalAsync;
        Assert.Equal(callback, cookie.Events.OnValidatePrincipal);
        var version = validator.GetType().Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        // The shared framework can roll forward to a servicing patch without changing NuGet references.
        var runtimeVersion = Version.Parse(version.Split('+')[0]);
        Assert.Equal(10, runtimeVersion.Major);
        Assert.Equal(0, runtimeVersion.Minor);
        Assert.True(runtimeVersion.Build >= 12);
        output.WriteLine("Validator: " + validator.GetType().FullName);
        output.WriteLine("ValidationInterval: " + stampOptions.ValidationInterval);
        output.WriteLine("Cookie callback: SecurityStampValidator.ValidatePrincipalAsync");
        output.WriteLine("Identity assembly: " + version);

        var technical = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.TwoFactorRememberMeScheme);
        Assert.Equal(IdentityConstants.TwoFactorRememberMeScheme, technical.Cookie.Name);
        Assert.True(technical.Cookie.HttpOnly);
        Assert.Equal(Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest, technical.Cookie.SecurePolicy);
        Assert.Equal(Microsoft.AspNetCore.Http.SameSiteMode.Lax, technical.Cookie.SameSite);
        Assert.Null(technical.Cookie.Path); // Native builder derives Path from the request's PathBase.
        Assert.Null(technical.Cookie.Domain);
        Assert.Equal(TimeSpan.FromDays(14), technical.ExpireTimeSpan);
        Assert.True(technical.SlidingExpiration);
        Func<CookieValidatePrincipalContext, Task> technicalCallback =
            SecurityStampValidator.ValidateAsync<ITwoFactorSecurityStampValidator>;
        Assert.Equal(technicalCallback, technical.Events.OnValidatePrincipal);
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        Assert.Equal("/", technical.Cookie.Build(context).Path);
        var handler = await services.GetRequiredService<IAuthenticationHandlerProvider>()
            .GetHandlerAsync(context, IdentityConstants.TwoFactorRememberMeScheme);
        Assert.IsAssignableFrom<IAuthenticationSignOutHandler>(handler);
        output.WriteLine("Technical cookie: native name, HttpOnly, Secure SameAsRequest, SameSite Lax, Path /, no Domain;");
        output.WriteLine("14-day ticket, sliding expiration; sign-out handler available, never a default.");

        using var plainFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production"));
        using var plainClient = plainFactory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var absentRoute = await plainClient.GetAsync(SecurityStampDiagnosticController.ProtectedPath);
        Assert.Equal(HttpStatusCode.NotFound, absentRoute.StatusCode);
        using var absentSignIn = await plainClient.PostAsync(SecurityStampDiagnosticController.SignInPath, null);
        Assert.Equal(HttpStatusCode.NotFound, absentSignIn.StatusCode);
        output.WriteLine("Both diagnostic routes absent from the unmodified API host (404).");
    }

    [Fact]
    public async Task Unchanged_stamp_after_interval_is_validated_and_cookie_is_renewed()
    {
        using var factory = new DiagnosticFactory();
        await factory.InitializeAccountAsync();
        using var client = factory.NewClient();
        using var anonymous = await client.GetAsync(SecurityStampDiagnosticController.ProtectedPath);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.False(anonymous.Headers.Contains("Location"));
        await SignInAndConfirmAsync(factory, client);

        var before = factory.Clock.GetUtcNow();
        factory.Clock.Advance(factory.ValidationInterval + TimeSpan.FromSeconds(1));
        using var validated = await client.GetAsync(SecurityStampDiagnosticController.ProtectedPath);

        Assert.Equal(HttpStatusCode.NoContent, validated.StatusCode);
        var renewed = ReadTicket(factory, validated);
        Assert.Equal(factory.Clock.GetUtcNow(), renewed.Properties.IssuedUtc);
        Assert.True(renewed.Properties.IssuedUtc > before);
        output.WriteLine("Control: 401 without cookie; real cookie initially 204; after interval 204.");
        output.WriteLine("Native validator ran: renewed cookie with advanced IssuedUtc, SlidingExpiration=false.");
    }

    [Fact]
    public async Task Changed_stamp_is_accepted_before_interval_then_rejected_with_401_and_cookie_deletion()
    {
        using var factory = new DiagnosticFactory();
        await factory.InitializeAccountAsync();
        using var client = factory.NewClient();
        var originalCookie = await SignInAndConfirmAsync(factory, client);

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = await users.FindByEmailAsync(SecurityStampDiagnosticController.TestEmail);
            Assert.NotNull(user);
            var oldStamp = await users.GetSecurityStampAsync(user);
            Assert.True((await users.UpdateSecurityStampAsync(user)).Succeeded);
            Assert.True(oldStamp != await users.GetSecurityStampAsync(user));
        }
        using var beforeInterval = await client.GetAsync(SecurityStampDiagnosticController.ProtectedPath);
        Assert.Equal(HttpStatusCode.NoContent, beforeInterval.StatusCode);
        Assert.False(beforeInterval.Headers.Contains("Set-Cookie"));

        factory.Clock.Advance(factory.ValidationInterval + TimeSpan.FromSeconds(1));
        using var rejected = await client.GetAsync(SecurityStampDiagnosticController.ProtectedPath);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.False(rejected.Headers.Contains("Location"));
        var deleted = rejected.Headers.GetValues("Set-Cookie")
            .Select(value => SetCookieHeaderValue.Parse(value)).ToArray();
        Assert.Equal(new[] { "__Host-Talynor.Auth", IdentityConstants.TwoFactorRememberMeScheme }.OrderBy(name => name),
            deleted.Select(cookie => cookie.Name.ToString()).OrderBy(name => name));
        foreach (var cookie in deleted)
        {
            Assert.Equal(string.Empty, cookie.Value.ToString());
            Assert.True(cookie.Expires < factory.Clock.GetUtcNow());
            Assert.True(cookie.Secure);
            Assert.True(cookie.HttpOnly);
        }
        using var withoutCookie = await client.GetAsync(SecurityStampDiagnosticController.ProtectedPath);
        Assert.Equal(HttpStatusCode.Unauthorized, withoutCookie.StatusCode);
        // Replaying the original ticket must also fail, independently of the cookie jar's deletion.
        using var replayClient = factory.NewClient();
        replayClient.DefaultRequestHeaders.Add("Cookie", originalCookie);
        using var replay = await replayClient.GetAsync(SecurityStampDiagnosticController.ProtectedPath);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        output.WriteLine("Before interval: changed stamp still authenticates (204).");
        output.WriteLine("After interval: native stamp rejection returns 401 without exception or redirect.");
        output.WriteLine("Application and auxiliary cookies deleted; anonymous follow-up and original-ticket replay return 401.");
    }

    private static async Task<string> SignInAndConfirmAsync(DiagnosticFactory factory, HttpClient client)
    {
        using var issued = await client.PostAsync(SecurityStampDiagnosticController.SignInPath, null);
        Assert.Equal(HttpStatusCode.NoContent, issued.StatusCode);
        var issuedCookie = Assert.Single(issued.Headers.GetValues("Set-Cookie")
            .Select(value => SetCookieHeaderValue.Parse(value)));
        Assert.Equal("__Host-Talynor.Auth", issuedCookie.Name.ToString());
        var ticket = ReadTicket(factory, issued);
        Assert.Equal(IdentityConstants.ApplicationScheme, ticket.AuthenticationScheme);
        Assert.True(ticket.Principal.Identity?.IsAuthenticated);
        Assert.False(ticket.Properties.IsPersistent);
        Assert.Equal(factory.Clock.GetUtcNow(), ticket.Properties.IssuedUtc);
        var identity = factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        Assert.True(!string.IsNullOrEmpty(ticket.Principal.FindFirst(identity.ClaimsIdentity.SecurityStampClaimType)?.Value));
        using var authenticated = await client.GetAsync(SecurityStampDiagnosticController.ProtectedPath);
        Assert.Equal(HttpStatusCode.NoContent, authenticated.StatusCode);
        Assert.False(authenticated.Headers.Contains("Set-Cookie"));
        return $"{issuedCookie.Name}={issuedCookie.Value}";
    }

    private static AuthenticationTicket ReadTicket(DiagnosticFactory factory, HttpResponseMessage response)
    {
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie")
            .Select(value => SetCookieHeaderValue.Parse(value)), value => value.Name == "__Host-Talynor.Auth");
        Assert.DoesNotContain(response.Headers.GetValues("Set-Cookie")
            .Select(value => SetCookieHeaderValue.Parse(value)),
            value => value.Name == IdentityConstants.TwoFactorRememberMeScheme);
        Assert.True(cookie.Secure);
        Assert.True(cookie.HttpOnly);
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        Assert.False(options.SlidingExpiration);
        var ticket = options.TicketDataFormat.Unprotect(cookie.Value.ToString());
        Assert.NotNull(ticket);
        return ticket;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }

    private sealed class DiagnosticFactory(bool controlTime = true) : WebApplicationFactory<Program>
    {
        private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"talynor-stamp-{Guid.NewGuid():N}.sqlite");
        public ManualTimeProvider Clock { get; } = new();
        public TimeSpan ValidationInterval => Services.GetRequiredService<IOptions<SecurityStampValidatorOptions>>()
            .Value.ValidationInterval;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Production leaves failures unhandled by the Development exception page.
            // Identity/cookie registration in Program is identical in both environments.
            builder.UseEnvironment("Production");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Talynor"] = new SqliteConnectionStringBuilder
                        { DataSource = databasePath, Pooling = false }.ConnectionString
                }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                // These routes are discovered only by this factory, never by the production API.
                services.AddControllers().AddApplicationPart(typeof(SecurityStampDiagnosticController).Assembly);
                if (controlTime)
                {
                    services.PostConfigure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme,
                        options => options.TimeProvider = Clock);
                    services.PostConfigure<SecurityStampValidatorOptions>(options => options.TimeProvider = Clock);
                }
            });
        }

        public async Task InitializeAccountAsync()
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.MigrateAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser
                { Email = SecurityStampDiagnosticController.TestEmail, UserName = SecurityStampDiagnosticController.TestEmail };
            // Synthetic input confined to the disposable test database, never logged or seeded in Development.
            Assert.True((await users.CreateAsync(user, "synthetic security stamp diagnostic passphrase")).Succeeded);
            Assert.False(user.EmailConfirmed);
            Assert.Same(Clock, Services.GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value.TimeProvider);
            Assert.Same(Clock, Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(IdentityConstants.ApplicationScheme).TimeProvider);
        }

        public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false
        });

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                {
                    File.Delete(databasePath + suffix);
                }
            }
        }
    }
}

// Test-only HTTP seam to exercise the real middleware, Identity claims factory and cookie handler.
// No user-supplied credentials, direct ClaimsPrincipal construction or production routes are added.
[ApiController]
public sealed class SecurityStampDiagnosticController(
    UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) : ControllerBase
{
    internal const string TestEmail = "stamp-diagnostic@example.invalid";
    internal const string SignInPath = "/_diagnostics/security-stamp/issue-cookie";
    internal const string ProtectedPath = "/_diagnostics/security-stamp/protected";

    [HttpPost(SignInPath)]
    [AllowAnonymous]
    public async Task<IActionResult> IssueCookie()
    {
        var user = await users.FindByEmailAsync(TestEmail)
            ?? throw new InvalidOperationException("Diagnostic account was not initialized.");
        await signIn.SignInAsync(user, isPersistent: false);
        return NoContent();
    }

    [HttpGet(ProtectedPath)]
    [Authorize]
    public IActionResult Protected() => NoContent();
}

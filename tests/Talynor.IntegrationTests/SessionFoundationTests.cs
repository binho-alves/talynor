using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public class SessionFoundationTests
{
    [Fact]
    public async Task Csrf_bootstrap_returns_only_request_token_and_secure_cookie_without_authentication()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        using var response = await client.GetAsync("/api/auth/csrf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("token", Assert.Single(body.RootElement.EnumerateObject()).Name);
        var token = body.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        var cookies = response.Headers.GetValues("Set-Cookie")
            .Select(value => SetCookieHeaderValue.Parse(value)).ToArray();
        var cookie = Assert.Single(cookies, value => value.Name == "__Host-Talynor.Csrf");
        Assert.True(cookie.Secure);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/", cookie.Path.ToString());
        Assert.False(cookie.Domain.HasValue);
        Assert.DoesNotContain(cookies, value => value.Name == "__Host-Talynor.Auth");

        using var scope = factory.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Scheme = "https";
        context.Request.Method = "POST";
        context.Request.Headers.Cookie = $"{cookie.Name}={cookie.Value}";
        context.Request.Headers["X-CSRF-TOKEN"] = token;
        Assert.True(await scope.ServiceProvider.GetRequiredService<IAntiforgery>().IsRequestValidAsync(context));
    }

    [Fact]
    public async Task Application_cookie_and_authorization_are_configured_for_API_without_extra_schemes()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var options = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.Equal("__Host-Talynor.Auth", options.Cookie.Name);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(Microsoft.AspNetCore.Http.SameSiteMode.Lax, options.Cookie.SameSite);
        Assert.Equal("/", options.Cookie.Path);
        Assert.Null(options.Cookie.Domain);
        Assert.Equal(TimeSpan.FromHours(8), options.ExpireTimeSpan);
        Assert.False(options.SlidingExpiration);
        Func<CookieValidatePrincipalContext, Task> validator = SecurityStampValidator.ValidatePrincipalAsync;
        Assert.Equal(validator, options.Events.OnValidatePrincipal);

        var schemes = services.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.Equal(IdentityConstants.ApplicationScheme, Assert.Single(await schemes.GetAllSchemesAsync()).Name);
        Assert.Equal(IdentityConstants.ApplicationScheme, (await schemes.GetDefaultAuthenticateSchemeAsync())!.Name);
        Assert.Equal(IdentityConstants.ApplicationScheme, (await schemes.GetDefaultChallengeSchemeAsync())!.Name);
        Assert.Equal(IdentityConstants.ApplicationScheme, (await schemes.GetDefaultSignInSchemeAsync())!.Name);
        Assert.Null(services.GetRequiredService<IOptions<AuthorizationOptions>>().Value.FallbackPolicy);
        Assert.Equal("X-CSRF-TOKEN", services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.HeaderName);

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        await context.ChallengeAsync();
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
        await context.ForbidAsync();
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public void Identity_services_resolve_without_changing_the_existing_persistence_model()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        Assert.NotNull(services.GetRequiredService<UserManager<IdentityUser>>());
        Assert.NotNull(services.GetRequiredService<SignInManager<IdentityUser>>());
        Assert.NotNull(services.GetRequiredService<ISecurityStampValidator>());
        Assert.IsAssignableFrom<IUserPasswordStore<IdentityUser>>(services.GetRequiredService<IUserStore<IdentityUser>>());
        Assert.False(services.GetRequiredService<TalynorDbContext>().Database.HasPendingModelChanges());
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Talynor"] = "Data Source=:memory:" }));
            builder.ConfigureServices(services =>
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider()));
        });
}

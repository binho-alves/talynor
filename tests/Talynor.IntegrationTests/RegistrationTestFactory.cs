using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talynor.Api.Contracts;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

internal sealed class RegistrationTestFactory(SaveChangesInterceptor? interceptor = null,
    Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"talynor-register-{Guid.NewGuid():N}.sqlite");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
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
            services.AddSingleton<IStartupFilter, TestRemoteIpFilter>();
            if (interceptor is not null)
            {
                services.AddDbContext<TalynorDbContext>(options => options.AddInterceptors(interceptor));
            }
            configureServices?.Invoke(services);
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().Database.MigrateAsync();
    }

    public HttpClient NewClient(string? remoteIp = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false
        });
        if (remoteIp is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-Remote-IP", remoteIp);
        }
        return client;
    }

    public static async Task<string> CsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CsrfTokenResponse>())!.Token;
    }

    public static Task<HttpResponseMessage> RegisterAsync(HttpClient client, object request, string? token)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
            { Content = JsonContent.Create(request) };
        if (token is not null)
        {
            message.Headers.Add("X-CSRF-TOKEN", token);
        }
        return SendAndDisposeAsync(client, message);
    }

    private static async Task<HttpResponseMessage> SendAndDisposeAsync(HttpClient client, HttpRequestMessage message)
    {
        using (message)
        {
            return await client.SendAsync(message);
        }
    }

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

    // These headers exist only in this in-process test host; production never trusts them.
    private sealed class TestRemoteIpFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuation) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.TryParse(
                    context.Request.Headers["X-Test-Remote-IP"].ToString(), out var address) ? address : null;
                return continuation(context);
            });
            next(app);
        };
    }
}

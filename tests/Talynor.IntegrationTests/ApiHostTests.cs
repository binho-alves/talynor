using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Talynor.IntegrationTests;

public class ApiHostTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Health_returns_healthy_when_host_starts(string environment)
    {
        using var factory = CreateFactory(environment);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OpenApi_is_available_in_development()
    {
        using var factory = CreateFactory(Environments.Development);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.StartsWith("3.", document.RootElement.GetProperty("openapi").GetString());
    }

    [Fact]
    public async Task OpenApi_is_not_exposed_in_production()
    {
        using var factory = CreateFactory(Environments.Production);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(string environment) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment(environment));

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
}

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Talynor.IntegrationTests;

public class KnowledgeMapsTests
{
    [Theory]
    [InlineData("software")]
    [InlineData("csharp")]
    public async Task Known_map_returns_expected_demo_content_and_library_independent_contract(string mapId)
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        using var response = await client.GetAsync($"/api/knowledge-maps/{mapId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var map = document.RootElement;
        AssertProperties(map, "id", "title", "description", "nodes", "edges");
        Assert.Equal(mapId, map.GetProperty("id").GetString());

        var expectedNodes = mapId == "software"
            ? new[] { ("software-development", "Software Development", 35, 420), ("csharp", "C#", 60, 180),
                ("aspnet-core", "ASP.NET Core", 25, 90), ("ef-core", "EF Core", 15, 60), ("architecture", "Architecture", 40, 90) }
            : new[] { ("linq", "LINQ", 75, 60), ("generics", "Generics", 50, 35), ("async-await", "Async/Await", 30, 40),
                ("nullable", "Nullable", 80, 20), ("collections", "Collections", 65, 25) };
        var nodes = map.GetProperty("nodes").EnumerateArray().ToArray();
        Assert.Equal(5, nodes.Length);
        foreach (var (id, title, progress, minutes) in expectedNodes)
        {
            var node = Assert.Single(nodes, n => n.GetProperty("id").GetString() == id);
            AssertProperties(node, "id", "title", "description", "progress", "studyMinutes", "childMapId");
            Assert.Equal(title, node.GetProperty("title").GetString());
            Assert.False(string.IsNullOrWhiteSpace(node.GetProperty("description").GetString()));
            Assert.Equal(progress, node.GetProperty("progress").GetInt32());
            Assert.Equal(minutes, node.GetProperty("studyMinutes").GetInt32());
        }

        var expectedEdges = mapId == "software"
            ? new[] { ("software-csharp", "software-development", "csharp"), ("software-aspnet", "software-development", "aspnet-core"),
                ("software-architecture", "software-development", "architecture"), ("csharp-ef", "csharp", "ef-core"), ("aspnet-ef", "aspnet-core", "ef-core") }
            : new[] { ("linq-collections", "linq", "collections"), ("generics-collections", "generics", "collections"),
                ("generics-nullable", "generics", "nullable"), ("generics-async", "generics", "async-await") };
        var edges = map.GetProperty("edges").EnumerateArray().ToArray();
        Assert.Equal(expectedEdges.Length, edges.Length);
        foreach (var (id, source, target) in expectedEdges)
        {
            var edge = Assert.Single(edges, e => e.GetProperty("id").GetString() == id);
            AssertProperties(edge, "id", "sourceNodeId", "targetNodeId");
            Assert.Equal(source, edge.GetProperty("sourceNodeId").GetString());
            Assert.Equal(target, edge.GetProperty("targetNodeId").GetString());
        }
    }

    [Fact]
    public async Task Unknown_map_returns_not_found()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        using var response = await client.GetAsync("/api/knowledge-maps/unknown");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Csharp_child_map_can_be_loaded()
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        using var parentResponse = await client.GetAsync("/api/knowledge-maps/software");
        parentResponse.EnsureSuccessStatusCode();
        using var parent = JsonDocument.Parse(await parentResponse.Content.ReadAsStringAsync());
        var node = parent.RootElement.GetProperty("nodes").EnumerateArray()
            .Single(n => n.GetProperty("id").GetString() == "csharp");
        var childId = node.GetProperty("childMapId").GetString();
        Assert.Equal("csharp", childId);
        using var childResponse = await client.GetAsync($"/api/knowledge-maps/{childId}");
        Assert.Equal(HttpStatusCode.OK, childResponse.StatusCode);
    }

    private static void AssertProperties(JsonElement element, params string[] names) =>
        Assert.Equal(names.Order(), element.EnumerateObject().Select(p => p.Name).Order());

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Development));

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
}

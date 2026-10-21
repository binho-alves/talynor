using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Talynor.Api.Contracts;
using Talynor.Domain.Flashcards;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

public sealed class FlashcardReviewHttpTests
{
    private const string Password = "synthetic flashcard review test passphrase";
    private static string Cards(Guid map, Guid node) => $"/api/maps/{map:D}/nodes/{node:D}/flashcards";
    private static string Card(Guid map, Guid node, Guid card) => Cards(map, node) + $"/{card:D}";
    private static string Next(Guid map, Guid node) => Cards(map, node) + "/study/next";
    private static string Reveal(Guid map, Guid node, Guid card, string query) => Card(map, node, card) + "/study/reveal" + query;
    private static string Review(Guid map, Guid node, Guid card, Guid review) => Card(map, node, card) + $"/reviews/{review:D}";

    [Fact]
    public async Task Study_next_is_authenticated_no_store_and_never_exposes_back_or_empty_deck_shape()
    {
        using var factory = new RegistrationTestFactory();
        using (var anonymous = factory.NewClient())
        {
            using var denied = await anonymous.GetAsync(Next(Guid.NewGuid(), Guid.NewGuid()));
            await Problem(denied, HttpStatusCode.Unauthorized, "authentication_required");
        }

        await factory.InitializeAsync();
        using var client = factory.NewClient();
        var graph = await Prepare(factory, client);
        using (var empty = await client.GetAsync(Next(graph.Map.Id, graph.First.Id)))
        {
            var body = await Ok(empty);
            Assert.Equal(new[] { "item" }, body.EnumerateObject().Select(property => property.Name));
            Assert.Equal(JsonValueKind.Null, body.GetProperty("item").ValueKind);
        }

        var created = await Create(client, graph.Map.Id, graph.Second.Id, "question", "answer");
        var next = await Ok(await client.GetAsync(Next(graph.Map.Id, graph.Second.Id)));
        var item = next.GetProperty("item");
        Assert.Equal(new[] { "id", "front", "contentRevision", "reviewState" }, item.EnumerateObject().Select(p => p.Name));
        Assert.Equal(created.Id, item.GetProperty("id").GetGuid());
        Assert.Equal("question", item.GetProperty("front").GetString());
        Assert.Equal(1, item.GetProperty("contentRevision").GetInt32());
        Assert.Equal((0, 0), (item.GetProperty("reviewState").GetProperty("reviewCount").GetInt32(),
            item.GetProperty("reviewState").GetProperty("correctCount").GetInt32()));
        Assert.DoesNotContain("back", item.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reveal_requires_one_positive_revision_and_never_returns_answer_when_stale()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var graph = await Prepare(factory, client); var created = await Create(client, graph.Map.Id, graph.Second.Id, "front", "private answer");

        foreach (var query in new[] { "", "?contentRevision=0", "?contentRevision=-1", "?contentRevision=1&contentRevision=1", "?contentRevision=1&x=2", "?contentRevision=1x" })
        {
            using var invalid = await client.GetAsync(Reveal(graph.Map.Id, graph.Second.Id, created.Id, query));
            await Problem(invalid, HttpStatusCode.BadRequest, "validation_failed");
        }

        var reveal = await Ok(await client.GetAsync(Reveal(graph.Map.Id, graph.Second.Id, created.Id, "?contentRevision=1")));
        Assert.Equal(new[] { "flashcardId", "back", "contentRevision" }, reveal.EnumerateObject().Select(p => p.Name));
        Assert.Equal("private answer", reveal.GetProperty("back").GetString());

        using (var changed = await Write(client, "PATCH", Card(graph.Map.Id, graph.Second.Id, created.Id), "{\"back\":\"new answer\"}"))
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        using var stale = await client.GetAsync(Reveal(graph.Map.Id, graph.Second.Id, created.Id, "?contentRevision=1"));
        var problem = await Problem(stale, HttpStatusCode.Conflict, "flashcard_changed");
        Assert.DoesNotContain("back", problem, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("new answer", problem, StringComparison.Ordinal);
        var current = await Ok(await client.GetAsync(Reveal(graph.Map.Id, graph.Second.Id, created.Id, "?contentRevision=2")));
        Assert.Equal("new answer", current.GetProperty("back").GetString());
    }

    [Fact]
    public async Task Review_put_enforces_csrf_strict_payload_and_returns_no_store_current_state()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var graph = await Prepare(factory, client); var card = await Create(client, graph.Map.Id, graph.Second.Id, "front", "back");
        var id = Guid.NewGuid(); var path = Review(graph.Map.Id, graph.Second.Id, card.Id, id);

        using (var missingCsrf = await Send(client, "PUT", path, "{\"contentRevision\":1,\"result\":\"correct\"}", null))
            await Problem(missingCsrf, HttpStatusCode.BadRequest, "csrf_invalid");

        foreach (var json in new[] { "{}", "null", "[]", "{\"contentRevision\":1,\"result\":\"Correct\"}",
                     "{\"contentRevision\":1,\"result\":\"maybe\"}", "{\"contentRevision\":null,\"result\":\"correct\"}",
                     "{\"contentRevision\":1,\"result\":\"correct\",\"extra\":true}" })
        {
            using var invalid = await Write(client, "PUT", path, json);
            await Problem(invalid, HttpStatusCode.BadRequest, "validation_failed");
        }

        var accepted = await Ok(await Write(client, "PUT", path, "{\"contentRevision\":1,\"result\":\"correct\"}"));
        Assert.Equal(new[] { "review", "currentReviewState" }, accepted.EnumerateObject().Select(p => p.Name));
        var review = accepted.GetProperty("review");
        Assert.Equal(id, review.GetProperty("reviewId").GetGuid());
        Assert.Equal(card.Id, review.GetProperty("flashcardId").GetGuid());
        Assert.Equal(1, review.GetProperty("contentRevision").GetInt32());
        Assert.Equal("correct", review.GetProperty("result").GetString());
        Assert.True(review.GetProperty("reviewedAtUtc").GetDateTimeOffset().Offset == TimeSpan.Zero);
        Assert.Equal((1, 1), (accepted.GetProperty("currentReviewState").GetProperty("reviewCount").GetInt32(),
            accepted.GetProperty("currentReviewState").GetProperty("correctCount").GetInt32()));
        Assert.Equal(1, accepted.GetProperty("currentReviewState").GetProperty("contentRevision").GetInt32());
        Assert.True((await client.GetAsync(Next(graph.Map.Id, graph.Second.Id))).Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Review_put_enforces_the_phase_seven_body_cap_before_json_binding()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var graph = await Prepare(factory, client); var card = await Create(client, graph.Map.Id, graph.Second.Id, "front", "back");
        var valid = "{\"contentRevision\":1,\"result\":\"correct\"}";
        using var accepted = await Send(client, "PUT", Review(graph.Map.Id, graph.Second.Id, card.Id, Guid.NewGuid()),
            valid.PadRight(65536), await RegistrationTestFactory.CsrfAsync(client));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.True(accepted.Headers.CacheControl?.NoStore);

        using var oversized = await Send(client, "PUT", Review(graph.Map.Id, graph.Second.Id, card.Id, Guid.NewGuid()),
            valid.PadRight(65537), await RegistrationTestFactory.CsrfAsync(client));
        await Problem(oversized, HttpStatusCode.RequestEntityTooLarge, "validation_failed");
        using var scope = factory.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<TalynorDbContext>().FlashcardReviews.ToArrayAsync());
    }

    [Fact]
    public async Task Same_review_replays_original_event_new_payload_conflicts_and_history_is_current_state_envelope()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var client = factory.NewClient();
        var graph = await Prepare(factory, client); var card = await Create(client, graph.Map.Id, graph.Second.Id, "front", "back");
        var id = Guid.NewGuid(); var path = Review(graph.Map.Id, graph.Second.Id, card.Id, id);
        var first = await Ok(await Write(client, "PUT", path, "{\"contentRevision\":1,\"result\":\"incorrect\"}"));
        var replay = await Ok(await Write(client, "PUT", path, "{\"contentRevision\":1,\"result\":\"incorrect\"}"));
        Assert.Equal(first.GetProperty("review").GetRawText(), replay.GetProperty("review").GetRawText());
        Assert.Equal((1, 0), (replay.GetProperty("currentReviewState").GetProperty("reviewCount").GetInt32(),
            replay.GetProperty("currentReviewState").GetProperty("correctCount").GetInt32()));

        using var conflict = await Write(client, "PUT", path, "{\"contentRevision\":1,\"result\":\"correct\"}");
        await Problem(conflict, HttpStatusCode.Conflict, "review_id_conflict");
        var history = await Ok(await client.GetAsync(path));
        Assert.Equal(first.GetRawText(), history.GetRawText());

        using (var edit = await Write(client, "PATCH", Card(graph.Map.Id, graph.Second.Id, card.Id), "{\"front\":\"edited\"}"))
            Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
        var replayAfterEdit = await Ok(await Write(client, "PUT", path, "{\"contentRevision\":1,\"result\":\"incorrect\"}"));
        Assert.Equal(first.GetProperty("review").GetRawText(), replayAfterEdit.GetProperty("review").GetRawText());
        Assert.Equal((2, 0, 0), (replayAfterEdit.GetProperty("currentReviewState").GetProperty("contentRevision").GetInt32(),
            replayAfterEdit.GetProperty("currentReviewState").GetProperty("reviewCount").GetInt32(),
            replayAfterEdit.GetProperty("currentReviewState").GetProperty("correctCount").GetInt32()));
        using var staleNewId = await Write(client, "PUT", Review(graph.Map.Id, graph.Second.Id, card.Id, Guid.NewGuid()),
            "{\"contentRevision\":1,\"result\":\"correct\"}");
        await Problem(staleNewId, HttpStatusCode.Conflict, "flashcard_changed");
    }

    [Fact]
    public async Task Review_history_hides_foreign_and_missing_cards_and_review_ids_and_other_accounts()
    {
        using var factory = new RegistrationTestFactory(); await factory.InitializeAsync(); using var owner = factory.NewClient();
        var graph = await Prepare(factory, owner); var card = await Create(owner, graph.Map.Id, graph.Second.Id, "front", "back");
        var reviewId = Guid.NewGuid(); var ownPath = Review(graph.Map.Id, graph.Second.Id, card.Id, reviewId);
        await Ok(await Write(owner, "PUT", ownPath, "{\"contentRevision\":1,\"result\":\"correct\"}"));

        var paths = new[]
        {
            Review(graph.OtherMap.Id, graph.Second.Id, card.Id, reviewId),
            Review(graph.Map.Id, graph.First.Id, card.Id, reviewId),
            Review(graph.Map.Id, graph.Second.Id, Guid.NewGuid(), reviewId),
            Review(graph.Map.Id, graph.Second.Id, card.Id, Guid.NewGuid())
        };
        string? baseline = null;
        foreach (var path in paths)
        {
            using var response = await owner.GetAsync(path);
            var text = await Problem(response, HttpStatusCode.NotFound, "flashcard_not_found");
            baseline ??= text;
            Assert.Equal(baseline, text);
        }

        using var foreign = factory.NewClient(); await Login(foreign);
        using var hidden = await foreign.GetAsync(ownPath);
        Assert.Equal(baseline, await Problem(hidden, HttpStatusCode.NotFound, "flashcard_not_found"));
    }

    private static async Task<KnowledgeGraphFixture> Prepare(RegistrationTestFactory factory, HttpClient client)
    {
        await Login(client);
        var workspace = (await client.GetFromJsonAsync<PersonalWorkspaceResponse>("/api/workspace"))!;
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TalynorDbContext>();
        return await KnowledgeGraphFixture.CreateAsync(db, await db.PersonalWorkspaces.SingleAsync(value => value.Id == workspace.Id));
    }

    private static async Task Login(HttpClient client)
    {
        var email = $"flashcard-review-{Guid.NewGuid():N}@example.invalid";
        using var register = await RegistrationTestFactory.RegisterAsync(client, new { email, password = Password },
            await RegistrationTestFactory.CsrfAsync(client));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        using var login = await Write(client, "POST", "/api/auth/login", JsonSerializer.Serialize(new { email, password = Password }));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    private static async Task<FlashcardResponse> Create(HttpClient client, Guid map, Guid node, string front, string back)
    {
        using var response = await Write(client, "POST", Cards(map, node), JsonSerializer.Serialize(new { front, back }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<FlashcardResponse>())!;
    }

    private static async Task<JsonElement> Ok(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }
    }

    private static async Task<HttpResponseMessage> Write(HttpClient client, string method, string path, string json) =>
        await Send(client, method, path, json, await RegistrationTestFactory.CsrfAsync(client));

    private static async Task<HttpResponseMessage> Send(HttpClient client, string method, string path, string? json, string? token)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static async Task<string> Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
            var text = await response.Content.ReadAsStringAsync(); using var json = JsonDocument.Parse(text);
            Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain("Private answer", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
            return text;
        }
    }
}

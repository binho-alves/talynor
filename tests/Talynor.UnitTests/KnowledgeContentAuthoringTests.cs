using Talynor.Application.Accounts;
using Talynor.Application.KnowledgeContents;
using Talynor.Application.Workspaces;
using Talynor.Domain.KnowledgeContents;
using Talynor.Domain.Workspaces;

namespace Talynor.UnitTests;

public class KnowledgeContentAuthoringTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Domain_updates_validate_complete_candidate_preserve_identity_and_ignore_material_noops(bool link)
    {
        var content = Content(link); var identity = (content.Id, content.KnowledgeNodeId, content.Type, content.CreatedAtUtc);
        var later = DateTimeOffset.UnixEpoch.AddDays(1).ToOffset(TimeSpan.FromHours(3));
        Assert.True(content.UpdateDetails(" New ", link ? null : "new body", link ? " https://intranet/new " : null, later));
        Assert.Equal("New", content.Title); Assert.Equal(later.ToUniversalTime(), content.UpdatedAtUtc);
        Assert.Equal(TimeSpan.Zero, content.UpdatedAtUtc.Offset);
        var state = State(content);
        Assert.ThrowsAny<ArgumentException>(() => content.UpdateDetails("Candidate", link ? "not allowed" : " ", link ? "https://intranet" : null, later.AddDays(1)));
        Assert.Equal(state, State(content));
        Assert.ThrowsAny<ArgumentException>(() => content.UpdateDetails(new string('x', 201), content.Body, content.Url, later));
        Assert.Equal(state, State(content));
        Assert.False(content.UpdateDetails(" New ", content.Body, content.Url, later.AddDays(2)));
        Assert.Equal(state, State(content));
        Assert.True(content.UpdateDetails(null, content.Body, content.Url, later.AddMinutes(1)));
        Assert.Null(content.Title); Assert.Equal(identity, (content.Id, content.KnowledgeNodeId, content.Type, content.CreatedAtUtc));
    }

    [Theory]
    [InlineData("javascript:alert(1)")] [InlineData("http://user:pass@example.invalid")]
    [InlineData("https://example.invalid\\evil")] [InlineData("https://example.invalid/\n")]
    public void Link_update_reuses_url_policy_without_partial_title_or_timestamp_change(string url)
    {
        var content = Content(true); var state = State(content);
        Assert.ThrowsAny<ArgumentException>(() => content.UpdateDetails("Candidate", null, url, DateTimeOffset.UnixEpoch.AddDays(1)));
        Assert.Equal(state, State(content));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Create_uses_server_workspace_domain_identity_clock_and_one_save(bool link)
    {
        var f = new Fixture();
        var result = await f.Create.ExecuteAsync(f.Map, f.Node, Input(link), CancellationToken.None);
        Assert.Equal(KnowledgeContentWriteStatus.Success, result.Status); Assert.NotNull(result.Content);
        Assert.NotEqual(Guid.Empty, result.Content.Id); Assert.Equal(f.Clock.GetUtcNow(), result.Content.CreatedAtUtc);
        Assert.Equal(result.Content.CreatedAtUtc, result.Content.UpdatedAtUtc); Assert.Equal("Title", result.Content.Title);
        Assert.Equal((f.Workspace.Id, f.Map, f.Node), f.Store.Scope);
        Assert.Equal(f.Node, f.Store.Content!.KnowledgeNodeId); Assert.Equal(1, f.Store.Saves);
        Assert.Equal(KnowledgeContentWriteOperation.Create, f.Store.Attempt!.Operation);
        Assert.Equal(result.Content.Id, f.Store.Attempt.ContentId);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Patch_preserves_omitted_or_null_payload_clears_title_and_rejects_noops_without_save(bool link)
    {
        var f = new Fixture(); f.Store.Content = Content(link, f.Node); var content = f.Store.Content;
        var original = State(content); f.Clock.Advance();
        Assert.Equal(KnowledgeContentWriteStatus.Success, await f.Update.ExecuteAsync(f.Map, f.Node, content.Id,
            new(Title: "Changed", BodySpecified: !link, UrlSpecified: link), CancellationToken.None));
        Assert.Equal(original.Body, content.Body); Assert.Equal(original.Url, content.Url);
        Assert.Equal(f.Clock.GetUtcNow(), content.UpdatedAtUtc); Assert.Equal(1, f.Store.Saves);
        f.Clock.Advance();
        Assert.Equal(KnowledgeContentWriteStatus.Success, await f.Update.ExecuteAsync(f.Map, f.Node, content.Id, new(ClearTitle: true), CancellationToken.None));
        var state = State(content); f.Clock.Advance();
        Assert.Equal(KnowledgeContentWriteStatus.InvalidInput, await f.Update.ExecuteAsync(f.Map, f.Node, content.Id, new(ClearTitle: true), CancellationToken.None));
        Assert.Equal(KnowledgeContentWriteStatus.InvalidInput, await f.Update.ExecuteAsync(f.Map, f.Node, content.Id, new(), CancellationToken.None));
        Assert.Equal(state, State(content)); Assert.Equal(2, f.Store.Saves);
    }

    [Theory]
    [InlineData("anonymous")] [InlineData("missing-account")] [InlineData("wrong-account")]
    public async Task Invalid_identity_never_reaches_content_store(string scenario)
    {
        var f = new Fixture(); f.Current.IsAuthenticated = scenario != "anonymous";
        if (scenario == "missing-account") f.Accounts.Id = null;
        if (scenario == "wrong-account") f.Accounts.Id = "other";
        Assert.Equal(KnowledgeContentWriteStatus.AuthenticationRequired, (await f.Create.ExecuteAsync(f.Map, f.Node, Input(false), CancellationToken.None)).Status);
        Assert.Equal(KnowledgeContentWriteStatus.AuthenticationRequired, await f.Update.ExecuteAsync(f.Map, f.Node, Guid.NewGuid(), new(Title: "Changed"), CancellationToken.None));
        Assert.Equal(KnowledgeContentWriteStatus.AuthenticationRequired, await f.Delete.ExecuteAsync(f.Map, f.Node, Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(0, f.Store.Reads); Assert.Equal(0, f.Store.Saves);
    }

    [Theory]
    [InlineData("map")] [InlineData("node")] [InlineData("content")]
    public async Task Missing_or_mismatched_target_is_not_found_without_mutation(string missing)
    {
        var f = new Fixture(); f.Store.Content = Content(false, f.Node);
        var map = missing == "map" ? Guid.NewGuid() : f.Map;
        var node = missing == "node" ? Guid.NewGuid() : f.Node;
        var id = missing == "content" ? Guid.NewGuid() : f.Store.Content.Id;
        if (missing != "content") Assert.Equal(KnowledgeContentWriteStatus.NotFound, (await f.Create.ExecuteAsync(map, node, Input(false), CancellationToken.None)).Status);
        Assert.Equal(KnowledgeContentWriteStatus.NotFound, await f.Update.ExecuteAsync(map, node, id, new(Title: "Changed"), CancellationToken.None));
        Assert.Equal(KnowledgeContentWriteStatus.NotFound, await f.Delete.ExecuteAsync(map, node, id, CancellationToken.None));
        Assert.Equal(0, f.Store.Saves); Assert.Equal("Title", f.Store.Content.Title);
    }

    public static IEnumerable<object[]> InvalidCreates()
    {
        yield return [new CreateKnowledgeContentInput("Note", null, "body", null)];
        yield return [new CreateKnowledgeContentInput("0", null, "body", null)];
        yield return [new CreateKnowledgeContentInput("video", null, null, "https://intranet")];
        yield return [new CreateKnowledgeContentInput("note", null, null, null)];
        yield return [new CreateKnowledgeContentInput("note", null, " ", null)];
        yield return [new CreateKnowledgeContentInput("note", null, new string('b', 4001), null)];
        yield return [new CreateKnowledgeContentInput("note", null, "body", "http://intranet")];
        yield return [new CreateKnowledgeContentInput("note", null, "body", null, UrlSpecified: true)];
        yield return [new CreateKnowledgeContentInput("link", null, null, null)];
        yield return [new CreateKnowledgeContentInput("link", null, "body", "http://intranet")];
        yield return [new CreateKnowledgeContentInput("link", null, null, "http://intranet", BodySpecified: true)];
        yield return [new CreateKnowledgeContentInput("link", null, null, "javascript:alert(1)")];
        yield return [new CreateKnowledgeContentInput("note", new string('t', 201), "body", null)];
    }

    [Theory]
    [MemberData(nameof(InvalidCreates))]
    public async Task Invalid_create_does_not_add_or_save(CreateKnowledgeContentInput input)
    {
        var f = new Fixture();
        Assert.Equal(KnowledgeContentWriteStatus.InvalidInput, (await f.Create.ExecuteAsync(f.Map, f.Node, input, CancellationToken.None)).Status);
        Assert.Null(f.Store.Content); Assert.Equal(0, f.Store.Saves);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Invalid_patch_never_partially_mutates_tracked_candidate(bool link)
    {
        var f = new Fixture(); f.Store.Content = Content(link, f.Node); var content = f.Store.Content; var state = State(content);
        var inputs = new[] { new UpdateKnowledgeContentInput(Title: "Candidate", ClearTitle: true),
            new(Title: " "), new(Title: new string('t', 201)),
            link ? new UpdateKnowledgeContentInput(Title: "Candidate", Url: "file:///bad") : new(Title: "Candidate", Body: " "),
            link ? new UpdateKnowledgeContentInput(Title: "Candidate", BodySpecified: true) : new(Title: "Candidate", UrlSpecified: true),
            link ? new UpdateKnowledgeContentInput(Body: "invalid") : new(Url: "https://intranet") };
        foreach (var input in inputs)
        {
            Assert.Equal(KnowledgeContentWriteStatus.InvalidInput, await f.Update.ExecuteAsync(f.Map, f.Node, content.Id, input, CancellationToken.None));
            Assert.Equal(state, State(content));
        }
        Assert.Equal(0, f.Store.Saves);
    }

    [Fact]
    public async Task Delete_is_one_save_and_repeated_delete_is_not_found()
    {
        var f = new Fixture(); f.Store.Content = Content(false, f.Node); var id = f.Store.Content.Id;
        Assert.Equal(KnowledgeContentWriteStatus.Success, await f.Delete.ExecuteAsync(f.Map, f.Node, id, CancellationToken.None));
        Assert.Null(f.Store.Content); Assert.Equal(1, f.Store.Saves); Assert.Equal(KnowledgeContentWriteOperation.Delete, f.Store.Attempt!.Operation);
        Assert.Equal(KnowledgeContentWriteStatus.NotFound, await f.Delete.ExecuteAsync(f.Map, f.Node, id, CancellationToken.None));
        Assert.Equal(1, f.Store.Saves);
    }

    [Fact]
    public async Task Cancelled_commands_do_not_access_store_and_failed_save_never_returns_created_detail()
    {
        var f = new Fixture(); var ct = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Create.ExecuteAsync(f.Map, f.Node, Input(false), ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Update.ExecuteAsync(f.Map, f.Node, Guid.NewGuid(), new(Title: "Changed"), ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Delete.ExecuteAsync(f.Map, f.Node, Guid.NewGuid(), ct));
        Assert.Equal(0, f.Store.Reads);
        f.Store.SaveStatus = KnowledgeContentWriteStatus.NotFound;
        var failed = await f.Create.ExecuteAsync(f.Map, f.Node, Input(false), CancellationToken.None);
        Assert.Equal(KnowledgeContentWriteStatus.NotFound, failed.Status); Assert.Null(failed.Content);
    }

    [Fact]
    public async Task Operational_save_failure_propagates_without_becoming_validation_or_not_found()
    {
        var f = new Fixture(); f.Store.Failure = new InvalidOperationException("Synthetic save failure.");
        Assert.Same(f.Store.Failure, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Create.ExecuteAsync(f.Map, f.Node, Input(false), CancellationToken.None)));
        var id = f.Store.Content!.Id;
        Assert.Same(f.Store.Failure, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Update.ExecuteAsync(f.Map, f.Node, id, new(Title: "Changed"), CancellationToken.None)));
        Assert.Same(f.Store.Failure, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Delete.ExecuteAsync(f.Map, f.Node, id, CancellationToken.None)));
    }

    private static CreateKnowledgeContentInput Input(bool link) => new(link ? "link" : "note", " Title ", link ? null : "body", link ? "http://intranet" : null);
    private static KnowledgeContent Content(bool link, Guid? node = null) => KnowledgeContent.Create(node ?? Guid.NewGuid(),
        link ? KnowledgeContentType.Link : KnowledgeContentType.Note, "Title", link ? null : "body", link ? "http://intranet" : null, DateTimeOffset.UnixEpoch);
    private static (string? Title, string? Body, string? Url, DateTimeOffset Created, DateTimeOffset Updated) State(KnowledgeContent c) => (c.Title, c.Body, c.Url, c.CreatedAtUtc, c.UpdatedAtUtc);
    private sealed class Fixture
    {
        public Guid Map { get; } = Guid.NewGuid(); public Guid Node { get; } = Guid.NewGuid();
        public PersonalWorkspace Workspace { get; } = PersonalWorkspace.Create("owner", DateTimeOffset.UnixEpoch);
        public Current Current { get; } = new(); public Accounts Accounts { get; } = new(); public Clock Clock { get; } = new();
        public Store Store { get; }
        public Fixture() { Store = new(Workspace.Id, Map, Node); }
        private GetOrCreatePersonalWorkspace WorkspaceCase => new(Current, Accounts, new WorkspaceStore(Workspace), Clock);
        public CreateKnowledgeContent Create => new(WorkspaceCase, Store, Clock);
        public UpdateKnowledgeContent Update => new(WorkspaceCase, Store, Clock);
        public DeleteKnowledgeContent Delete => new(WorkspaceCase, Store);
    }
    private sealed class Current : ICurrentUser { public bool IsAuthenticated { get; set; } = true; public string? UserId => "owner"; }
    private sealed class Accounts : IIdentityAccountService
    {
        public string? Id = "owner";
        public Task<AccountSummary?> GetByIdAsync(string id, CancellationToken ct) => Task.FromResult(Id is null ? null : new AccountSummary(Id, "synthetic@example.invalid"));
        public Task<RegisterUserResult> CreateAsync(string email, string password, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class WorkspaceStore(PersonalWorkspace workspace) : IPersonalWorkspaceStore
    {
        public Task<PersonalWorkspace?> GetByOwnerUserIdAsync(string id, CancellationToken ct) => Task.FromResult<PersonalWorkspace?>(workspace);
        public Task<PersonalWorkspace> AddOrGetExistingAsync(PersonalWorkspace value, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UnixEpoch.AddHours(1);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance() => now = now.AddHours(1);
    }
    private sealed class Store(Guid workspace, Guid map, Guid node) : IKnowledgeContentStore
    {
        public KnowledgeContent? Content; public int Reads; public int Saves; public Exception? Failure;
        public KnowledgeContentWriteStatus SaveStatus = KnowledgeContentWriteStatus.Success;
        public KnowledgeContentWriteAttempt? Attempt; public (Guid Workspace, Guid Map, Guid Node) Scope;
        public Task<bool> NodeExistsAsync(Guid w, Guid m, Guid n, CancellationToken ct)
        { Reads++; Scope = (w, m, n); return Task.FromResult(w == workspace && m == map && n == node); }
        public Task<KnowledgeContent?> GetForNodeAsync(Guid w, Guid m, Guid n, Guid id, CancellationToken ct)
        { Reads++; Scope = (w, m, n); return Task.FromResult(w == workspace && m == map && n == node && Content?.Id == id ? Content : null); }
        public void Add(KnowledgeContent content) => Content = content;
        public void Remove(KnowledgeContent content) => Content = null;
        public Task<KnowledgeContentWriteStatus> SaveAsync(KnowledgeContentWriteAttempt attempt, CancellationToken ct)
        { Saves++; Attempt = attempt; return Failure is null ? Task.FromResult(SaveStatus) : Task.FromException<KnowledgeContentWriteStatus>(Failure); }
    }
}

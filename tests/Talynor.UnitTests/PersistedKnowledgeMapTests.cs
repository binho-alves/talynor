using Talynor.Application.Accounts;
using Talynor.Application.KnowledgeMaps;
using Talynor.Application.Workspaces;
using Talynor.Domain.KnowledgeMaps;
using Talynor.Domain.Workspaces;

namespace Talynor.UnitTests;

public class PersistedKnowledgeMapTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Domain_creates_minimal_graph_with_guid_ids_and_controlled_utc_time()
    {
        var workspace = Guid.NewGuid();
        var map = KnowledgeMap.Create(workspace, " Map ", null, new Clock().GetUtcNow());
        var child = Guid.NewGuid();
        var node = KnowledgeNode.Create(map.Id, " Node ", "Description", -12.5, 99.25, child, new Clock().GetUtcNow());
        var edge = KnowledgeEdge.Create(map.Id, node.Id, node.Id, new Clock().GetUtcNow());
        Assert.NotEqual(Guid.Empty, map.Id);
        Assert.NotEqual(Guid.Empty, node.Id);
        Assert.NotEqual(Guid.Empty, edge.Id);
        Assert.Equal(workspace, map.WorkspaceId);
        Assert.Equal("Map", map.Title);
        Assert.Equal("Node", node.Title);
        Assert.Equal(map.Id, node.KnowledgeMapId);
        Assert.Equal(map.Id, edge.KnowledgeMapId);
        Assert.Equal(child, node.ChildMapId);
        Assert.Equal(-12.5, node.PositionX);
        Assert.Equal(99.25, node.PositionY);
        Assert.Equal(Now, map.CreatedAtUtc);
        Assert.Equal(Now, map.UpdatedAtUtc);
        Assert.Equal(Now, node.CreatedAtUtc);
        Assert.Equal(Now, node.UpdatedAtUtc);
        Assert.Equal(Now, edge.CreatedAtUtc);
        // No unapproved cycle/self-loop taxonomy is imposed by this persistence foundation.
        Assert.Equal(edge.SourceNodeId, edge.TargetNodeId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Titles_cannot_be_empty_or_whitespace(string? title)
    {
        Assert.ThrowsAny<ArgumentException>(() => KnowledgeMap.Create(Guid.NewGuid(), title!, null, new Clock().GetUtcNow()));
        Assert.ThrowsAny<ArgumentException>(() => KnowledgeNode.Create(Guid.NewGuid(), title!, null, 0, 0, null, new Clock().GetUtcNow()));
    }

    [Fact]
    public void Ids_and_text_limits_are_validated()
    {
        Assert.Throws<ArgumentException>(() => KnowledgeMap.Create(Guid.Empty, "Map", null, new Clock().GetUtcNow()));
        Assert.Throws<ArgumentException>(() => KnowledgeMap.Create(Guid.NewGuid(), new string('a', 201), null, new Clock().GetUtcNow()));
        Assert.Throws<ArgumentException>(() => KnowledgeMap.Create(Guid.NewGuid(), "Map", new string('a', 4001), new Clock().GetUtcNow()));
        Assert.Throws<ArgumentException>(() => KnowledgeNode.Create(Guid.Empty, "Node", null, 0, 0, null, new Clock().GetUtcNow()));
        Assert.Throws<ArgumentException>(() => KnowledgeNode.Create(Guid.NewGuid(), "Node", null, 0, 0, Guid.Empty, new Clock().GetUtcNow()));
        Assert.Throws<ArgumentException>(() => KnowledgeNode.Create(Guid.NewGuid(), new string('a', 201), null, 0, 0, null, new Clock().GetUtcNow()));
        Assert.Throws<ArgumentException>(() => KnowledgeNode.Create(Guid.NewGuid(), "Node", new string('a', 4001), 0, 0, null, new Clock().GetUtcNow()));
        Assert.Throws<ArgumentException>(() => KnowledgeEdge.Create(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), new Clock().GetUtcNow()));
        Assert.Throws<ArgumentException>(() => KnowledgeEdge.Create(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), new Clock().GetUtcNow()));
        Assert.Throws<ArgumentException>(() => KnowledgeEdge.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, new Clock().GetUtcNow()));
    }

    [Theory]
    [InlineData(double.NaN, 0, "positionX")]
    [InlineData(double.PositiveInfinity, 0, "positionX")]
    [InlineData(double.NegativeInfinity, 0, "positionX")]
    [InlineData(0, double.NaN, "positionY")]
    [InlineData(0, double.PositiveInfinity, "positionY")]
    [InlineData(0, double.NegativeInfinity, "positionY")]
    public void Positions_must_be_finite_and_identify_the_invalid_parameter(double x, double y, string parameter)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            KnowledgeNode.Create(Guid.NewGuid(), "Node", null, x, y, null, new Clock().GetUtcNow()));
        Assert.Equal(parameter, error.ParamName);
    }

    [Theory]
    [InlineData(false, null, "owner")]
    [InlineData(true, null, "owner")]
    [InlineData(true, "owner", null)]
    [InlineData(true, "owner", "another-account")]
    public async Task Invalid_authentication_or_account_never_reads_maps(bool authenticated, string? id, string? accountId)
    {
        var reader = new Reader();
        var useCase = CreateUseCase(reader, new Current(authenticated, id), accountId);
        var result = await useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(KnowledgeMapReadStatus.AuthenticationRequired, result.Status);
        Assert.Null(result.Map);
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task Read_uses_server_resolved_workspace_and_returns_own_projection_with_positions_and_child()
    {
        var map = new PersistedKnowledgeMap(Guid.NewGuid(), "Map", null,
            new[] { new PersistedKnowledgeNode(Guid.NewGuid(), "Node", null, 15.25, -22.5, Guid.NewGuid()) },
            Array.Empty<PersistedKnowledgeEdge>());
        var reader = new Reader { Map = map };
        using var cancellation = new CancellationTokenSource();
        var result = await CreateUseCase(reader).ExecuteAsync(map.Id, cancellation.Token);
        Assert.Equal(KnowledgeMapReadStatus.Success, result.Status);
        Assert.Same(map, result.Map);
        Assert.Equal(map.Id, reader.MapId);
        Assert.Equal(reader.Workspace.Id, reader.WorkspaceId);
        Assert.Equal(cancellation.Token, reader.Token);
        Assert.Equal(15.25, result.Map!.Nodes[0].PositionX);
        Assert.Equal(-22.5, result.Map.Nodes[0].PositionY);
        Assert.NotNull(result.Map.Nodes[0].ChildMapId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_and_other_workspace_map_have_identical_not_found_result(bool otherWorkspace)
    {
        var reader = new Reader { MapWorkspaceId = otherWorkspace ? Guid.NewGuid() : null,
            Map = otherWorkspace ? new(Guid.NewGuid(), "Private", null, [], []) : null };
        var result = await CreateUseCase(reader).ExecuteAsync(reader.Map?.Id ?? Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(new KnowledgeMapReadResult(KnowledgeMapReadStatus.NotFound), result);
    }

    [Fact]
    public async Task Missing_workspace_is_resolved_lazily_without_seeding_maps()
    {
        var reader = new Reader();
        var store = new Workspaces { Workspace = null };
        var useCase = new GetKnowledgeMapForCurrentWorkspace(new(new Current(true, "owner"), new Accounts("owner"), store, new Clock()), reader);
        Assert.Equal(KnowledgeMapReadStatus.NotFound, (await useCase.ExecuteAsync(Guid.NewGuid(), CancellationToken.None)).Status);
        Assert.NotNull(store.Added);
        Assert.Equal("owner", store.Added.OwnerUserId);
        Assert.Equal(store.Added.Id, reader.WorkspaceId);
    }

    [Fact]
    public async Task Operational_error_and_cancellation_are_not_not_found()
    {
        var reader = new Reader { Failure = new InvalidOperationException("Synthetic error.") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateUseCase(reader).ExecuteAsync(Guid.NewGuid(), CancellationToken.None));
        reader = new Reader();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateUseCase(reader).ExecuteAsync(Guid.NewGuid(), new CancellationToken(true)));
        Assert.Equal(0, reader.Calls);
    }

    private static GetKnowledgeMapForCurrentWorkspace CreateUseCase(Reader reader, Current? current = null, string? accountId = "owner") =>
        new(new(current ?? new(true, "owner"), new Accounts(accountId), new Workspaces { Workspace = reader.Workspace }, new Clock()), reader);
    private sealed record Current(bool IsAuthenticated, string? UserId) : ICurrentUser;
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Accounts(string? accountId) : IIdentityAccountService
    {
        public Task<AccountSummary?> GetByIdAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(accountId is null ? null : new AccountSummary(accountId, "synthetic@example.invalid"));
        public Task<RegisterUserResult> CreateAsync(string email, string password, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
    private sealed class Workspaces : IPersonalWorkspaceStore
    {
        public PersonalWorkspace? Workspace { get; init; }
        public PersonalWorkspace? Added { get; private set; }
        public Task<PersonalWorkspace?> GetByOwnerUserIdAsync(string ownerUserId, CancellationToken cancellationToken) => Task.FromResult(Workspace);
        public Task<PersonalWorkspace> AddOrGetExistingAsync(PersonalWorkspace workspace, CancellationToken cancellationToken)
        { Added = workspace; return Task.FromResult(workspace); }
    }
    private sealed class Reader : IPersistedKnowledgeMapReader
    {
        public PersonalWorkspace Workspace { get; } = PersonalWorkspace.Create("owner", Now);
        public PersistedKnowledgeMap? Map { get; init; }
        public Guid? MapWorkspaceId { get; init; }
        public Exception? Failure { get; init; }
        public int Calls { get; private set; }
        public Guid MapId { get; private set; }
        public Guid WorkspaceId { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<PersistedKnowledgeMap?> GetAsync(Guid mapId, Guid workspaceId, CancellationToken cancellationToken)
        {
            Calls++; MapId = mapId; WorkspaceId = workspaceId; Token = cancellationToken;
            return Failure is not null ? Task.FromException<PersistedKnowledgeMap?>(Failure) :
                Task.FromResult(MapWorkspaceId.HasValue && MapWorkspaceId != workspaceId ? null : Map);
        }
    }
}

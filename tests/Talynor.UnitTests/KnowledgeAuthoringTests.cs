using Talynor.Application.Accounts;
using Talynor.Application.KnowledgeMaps;
using Talynor.Application.Workspaces;
using Talynor.Domain.KnowledgeMaps;
using Talynor.Domain.Workspaces;

namespace Talynor.UnitTests;

public class KnowledgeAuthoringTests
{
    [Fact]
    public void Domain_mutations_preserve_creation_and_identity_normalize_utc_and_validate_before_mutating()
    {
        var created = DateTimeOffset.UnixEpoch;
        var map = KnowledgeMap.Create(Guid.NewGuid(), "Map", null, created);
        var node = KnowledgeNode.Create(map.Id, "Node", null, 0, 0, null, created);
        var later = created.AddDays(1).ToOffset(TimeSpan.FromHours(3));
        node.UpdateDetails("Changed", "Description", later);
        Assert.Equal(TimeSpan.Zero, node.UpdatedAtUtc.Offset);
        Assert.Equal(later.ToUniversalTime(), node.UpdatedAtUtc);
        node.Move(-3.5, 12.25, later.AddMinutes(1));
        var child = Guid.NewGuid(); node.SetChildMap(child, later.AddMinutes(2)); Assert.Equal(child, node.ChildMapId);
        node.ClearChildMap(later.AddMinutes(3)); Assert.Null(node.ChildMapId);
        Assert.Throws<ArgumentException>(() => node.SetChildMap(Guid.Empty, later));
        Assert.Throws<ArgumentOutOfRangeException>(() => node.Move(double.NaN, 0, later));
        Assert.Equal(-3.5, node.PositionX); Assert.Equal(12.25, node.PositionY);
        Assert.Equal(created, node.CreatedAtUtc); Assert.Equal(map.Id, node.KnowledgeMapId);
        map.UpdateDetails("Changed", null, later); Assert.Equal(created, map.CreatedAtUtc); Assert.Equal(TimeSpan.Zero, map.UpdatedAtUtc.Offset);
    }

    [Fact]
    public async Task Map_create_update_delete_use_current_workspace_clock_and_preserve_identity_and_creation()
    {
        var fixture = new Fixture();
        var created = await new CreateKnowledgeMap(fixture.WorkspaceCase, fixture.Store, fixture.Clock).ExecuteAsync(new(" Map ", "Description"), CancellationToken.None);
        Assert.Equal(KnowledgeWriteStatus.Success, created.Status);
        Assert.Equal(fixture.Workspace.Id, fixture.Store.Maps.Single().WorkspaceId);
        var map = fixture.Store.Maps.Single();
        var createdAt = map.CreatedAtUtc;
        fixture.Clock.Advance();
        Assert.Equal(KnowledgeWriteStatus.Success, await new UpdateKnowledgeMap(fixture.WorkspaceCase, fixture.Store, fixture.Clock)
            .ExecuteAsync(map.Id, new(" Updated ", null, true), CancellationToken.None));
        Assert.Equal("Updated", map.Title);
        Assert.Null(map.Description);
        Assert.Equal(createdAt, map.CreatedAtUtc);
        Assert.Equal(fixture.Clock.GetUtcNow(), map.UpdatedAtUtc);
        Assert.Equal(KnowledgeWriteStatus.Success, await new DeleteKnowledgeMap(fixture.WorkspaceCase, fixture.Store).ExecuteAsync(map.Id, CancellationToken.None));
        Assert.Empty(fixture.Store.Maps);
    }

    [Fact]
    public async Task Node_create_patch_move_child_set_clear_and_delete_preserve_map_membership()
    {
        var f = new Fixture(); var map = f.AddMap(); var child = f.AddMap();
        var created = await new CreateKnowledgeNode(f.WorkspaceCase, f.Store, f.Clock).ExecuteAsync(map.Id, new("Node", "Details", 1, 2, null), CancellationToken.None);
        Assert.Equal(KnowledgeWriteStatus.Success, created.Status);
        var node = f.Store.Nodes.Single(); var createdAt = node.CreatedAtUtc;
        var update = new UpdateKnowledgeNode(f.WorkspaceCase, f.Store, f.Clock);
        f.Clock.Advance();
        Assert.Equal(KnowledgeWriteStatus.Success, await update.ExecuteAsync(map.Id, node.Id, new("New", null, false, new(-3, 4), child.Id, false), CancellationToken.None));
        Assert.Equal("Details", node.Description);
        Assert.Equal(child.Id, node.ChildMapId);
        Assert.Equal(-3, node.PositionX);
        Assert.Equal(4, node.PositionY);
        Assert.Equal(createdAt, node.CreatedAtUtc);
        Assert.Equal(f.Clock.GetUtcNow(), node.UpdatedAtUtc);
        f.Clock.Advance();
        Assert.Equal(KnowledgeWriteStatus.Success, await update.ExecuteAsync(map.Id, node.Id, new(null, null, true, null, null, true), CancellationToken.None));
        Assert.Null(node.Description); Assert.Null(node.ChildMapId);
        Assert.Equal(map.Id, node.KnowledgeMapId);
        Assert.Equal(f.Clock.GetUtcNow(), node.UpdatedAtUtc);
        Assert.Equal(KnowledgeWriteStatus.Success, await new DeleteKnowledgeNode(f.WorkspaceCase, f.Store).ExecuteAsync(map.Id, node.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData("map")]
    [InlineData("node")]
    public async Task Delete_in_use_is_expected_result_without_removing_or_saving(string resource)
    {
        var f = new Fixture(); var map = f.AddMap(); var node = f.AddNode(map);
        f.Store.InUse = true;
        var result = resource == "map" ? await new DeleteKnowledgeMap(f.WorkspaceCase, f.Store).ExecuteAsync(map.Id, CancellationToken.None)
            : await new DeleteKnowledgeNode(f.WorkspaceCase, f.Store).ExecuteAsync(map.Id, node.Id, CancellationToken.None);
        Assert.Equal(resource == "map" ? KnowledgeWriteStatus.MapInUse : KnowledgeWriteStatus.NodeInUse, result);
        Assert.Equal(0, f.Store.Saves); Assert.Single(f.Store.Maps); Assert.Single(f.Store.Nodes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_foreign_resources_return_not_found_for_every_targeted_use_case(bool foreign)
    {
        var f = new Fixture(); var map = f.AddMap(foreign ? Guid.NewGuid() : null); var node = f.AddNode(map);
        var edge = KnowledgeEdge.Create(map.Id, node.Id, node.Id, f.Clock.GetUtcNow()); f.Store.Edges.Add(edge);
        var id = foreign ? map.Id : Guid.NewGuid();
        Assert.Equal(KnowledgeWriteStatus.NotFound, await new UpdateKnowledgeMap(f.WorkspaceCase, f.Store, f.Clock).ExecuteAsync(id, new("Update", null, false), CancellationToken.None));
        Assert.Equal(KnowledgeWriteStatus.NotFound, await new DeleteKnowledgeMap(f.WorkspaceCase, f.Store).ExecuteAsync(id, CancellationToken.None));
        Assert.Equal(KnowledgeWriteStatus.NotFound, (await new CreateKnowledgeNode(f.WorkspaceCase, f.Store, f.Clock).ExecuteAsync(id, new("N", null, 0, 0, null), CancellationToken.None)).Status);
        Assert.Equal(KnowledgeWriteStatus.NotFound, await new UpdateKnowledgeNode(f.WorkspaceCase, f.Store, f.Clock).ExecuteAsync(id, node.Id, new("New", null, false, null, null, false), CancellationToken.None));
        Assert.Equal(KnowledgeWriteStatus.NotFound, await new DeleteKnowledgeNode(f.WorkspaceCase, f.Store).ExecuteAsync(id, node.Id, CancellationToken.None));
        Assert.Equal(KnowledgeWriteStatus.NotFound, (await new CreateKnowledgeEdge(f.WorkspaceCase, f.Store, f.Clock).ExecuteAsync(id, new(node.Id, node.Id), CancellationToken.None)).Status);
        Assert.Equal(KnowledgeWriteStatus.NotFound, await new DeleteKnowledgeEdge(f.WorkspaceCase, f.Store).ExecuteAsync(id, edge.Id, CancellationToken.None));
        Assert.Equal(0, f.Store.Saves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_child_map_has_same_result_for_missing_and_foreign_without_save(bool foreign)
    {
        var f = new Fixture(); var map = f.AddMap(); var node = f.AddNode(map);
        var childId = foreign ? f.AddMap(Guid.NewGuid()).Id : Guid.NewGuid();
        Assert.Equal(KnowledgeWriteStatus.InvalidChildMap, (await new CreateKnowledgeNode(f.WorkspaceCase, f.Store, f.Clock)
            .ExecuteAsync(map.Id, new("N", null, 0, 0, childId), CancellationToken.None)).Status);
        Assert.Equal(KnowledgeWriteStatus.InvalidChildMap, await new UpdateKnowledgeNode(f.WorkspaceCase, f.Store, f.Clock)
            .ExecuteAsync(map.Id, node.Id, new(null, null, false, null, childId, false), CancellationToken.None));
        Assert.Null(node.ChildMapId); Assert.Equal(0, f.Store.Saves);
    }

    [Fact]
    public async Task Edges_validate_membership_but_preserve_self_loops_and_duplicates()
    {
        var f = new Fixture(); var map = f.AddMap(); var node = f.AddNode(map); var cross = f.AddNode(f.AddMap());
        var create = new CreateKnowledgeEdge(f.WorkspaceCase, f.Store, f.Clock);
        Assert.Equal(KnowledgeWriteStatus.InvalidEdgeNodes, (await create.ExecuteAsync(map.Id, new(node.Id, cross.Id), CancellationToken.None)).Status);
        Assert.Equal(KnowledgeWriteStatus.InvalidInput, (await create.ExecuteAsync(map.Id, new(Guid.Empty, node.Id), CancellationToken.None)).Status);
        for (var i = 0; i < 2; i++) Assert.Equal(KnowledgeWriteStatus.Success, (await create.ExecuteAsync(map.Id, new(node.Id, node.Id), CancellationToken.None)).Status);
        Assert.Equal(2, f.Store.Edges.Count);
        Assert.Equal(KnowledgeWriteStatus.Success, await new DeleteKnowledgeEdge(f.WorkspaceCase, f.Store).ExecuteAsync(map.Id, f.Store.Edges[0].Id, CancellationToken.None));
        Assert.Single(f.Store.Edges);
    }

    [Fact]
    public async Task Domain_and_patch_validation_reject_invalid_input_without_save_or_partial_node_mutation()
    {
        var f = new Fixture(); var map = f.AddMap(); var node = f.AddNode(map);
        Assert.Equal(KnowledgeWriteStatus.InvalidInput, (await new CreateKnowledgeMap(f.WorkspaceCase, f.Store, f.Clock).ExecuteAsync(new(" ", null), CancellationToken.None)).Status);
        Assert.Equal(KnowledgeWriteStatus.InvalidInput, await new UpdateKnowledgeMap(f.WorkspaceCase, f.Store, f.Clock).ExecuteAsync(map.Id, new(null, "Both", true), CancellationToken.None));
        Assert.Equal(KnowledgeWriteStatus.InvalidInput, (await new CreateKnowledgeNode(f.WorkspaceCase, f.Store, f.Clock).ExecuteAsync(map.Id, new("N", null, double.NaN, 0, null), CancellationToken.None)).Status);
        Assert.Equal(KnowledgeWriteStatus.InvalidInput, await new UpdateKnowledgeNode(f.WorkspaceCase, f.Store, f.Clock)
            .ExecuteAsync(map.Id, node.Id, new("Changed", null, false, new(0, double.PositiveInfinity), null, false), CancellationToken.None));
        Assert.Equal("Node", node.Title);
        Assert.Equal(KnowledgeWriteStatus.InvalidInput, await new UpdateKnowledgeNode(f.WorkspaceCase, f.Store, f.Clock)
            .ExecuteAsync(map.Id, node.Id, new(null, null, false, null, map.Id, true), CancellationToken.None));
        Assert.Equal(0, f.Store.Saves);
    }

    [Fact]
    public async Task Authentication_and_operational_failures_do_not_become_not_found_or_validation()
    {
        var f = new Fixture(); f.Current.IsAuthenticated = false;
        Assert.Equal(KnowledgeWriteStatus.AuthenticationRequired, (await new CreateKnowledgeMap(f.WorkspaceCase, f.Store, f.Clock).ExecuteAsync(new("M", null), CancellationToken.None)).Status);
        Assert.Equal(KnowledgeWriteStatus.AuthenticationRequired, await new DeleteKnowledgeMap(f.WorkspaceCase, f.Store).ExecuteAsync(Guid.NewGuid(), CancellationToken.None));
        f.Current.IsAuthenticated = true; f.Store.Failure = new InvalidOperationException("Synthetic operational error.");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CreateKnowledgeMap(f.WorkspaceCase, f.Store, f.Clock).ExecuteAsync(new("M", null), CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CreateKnowledgeMap(f.WorkspaceCase, f.Store, f.Clock).ExecuteAsync(new("M", null), new CancellationToken(true)));
    }

    private sealed class Fixture
    {
        public Clock Clock { get; } = new(); public Current Current { get; } = new();
        public PersonalWorkspace Workspace { get; } = PersonalWorkspace.Create("owner", DateTimeOffset.UnixEpoch);
        public Store Store { get; } = new();
        public GetOrCreatePersonalWorkspace WorkspaceCase => new(Current, new Accounts(), new Workspaces(Workspace), Clock);
        public KnowledgeMap AddMap(Guid? workspaceId = null) { var map = KnowledgeMap.Create(workspaceId ?? Workspace.Id, "Map", "Description", Clock.GetUtcNow()); Store.Maps.Add(map); return map; }
        public KnowledgeNode AddNode(KnowledgeMap map) { var node = KnowledgeNode.Create(map.Id, "Node", null, 0, 0, null, Clock.GetUtcNow()); Store.Nodes.Add(node); return node; }
    }
    private sealed class Clock : TimeProvider { private DateTimeOffset now = DateTimeOffset.UnixEpoch; public override DateTimeOffset GetUtcNow() => now; public void Advance() => now += TimeSpan.FromMinutes(1); }
    private sealed class Current : ICurrentUser { public bool IsAuthenticated { get; set; } = true; public string? UserId => "owner"; }
    private sealed class Accounts : IIdentityAccountService
    {
        public Task<AccountSummary?> GetByIdAsync(string id, CancellationToken ct) => Task.FromResult<AccountSummary?>(new(id, "synthetic@example.invalid"));
        public Task<RegisterUserResult> CreateAsync(string email, string password, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Workspaces(PersonalWorkspace workspace) : IPersonalWorkspaceStore
    {
        public Task<PersonalWorkspace?> GetByOwnerUserIdAsync(string owner, CancellationToken ct) => Task.FromResult<PersonalWorkspace?>(workspace);
        public Task<PersonalWorkspace> AddOrGetExistingAsync(PersonalWorkspace candidate, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Store : IKnowledgeMapStore
    {
        public List<KnowledgeMap> Maps { get; } = []; public List<KnowledgeNode> Nodes { get; } = []; public List<KnowledgeEdge> Edges { get; } = [];
        public bool InUse { get; set; } public int Saves { get; private set; } public Exception? Failure { get; set; }
        public Task<KnowledgeMap?> GetMapAsync(Guid id, Guid workspace, CancellationToken ct) => Task.FromResult(Maps.SingleOrDefault(map => map.Id == id && map.WorkspaceId == workspace));
        public Task<KnowledgeNode?> GetNodeAsync(Guid id, Guid map, Guid workspace, CancellationToken ct) => Task.FromResult(Nodes.SingleOrDefault(node => node.Id == id && node.KnowledgeMapId == map && Maps.Any(m => m.Id == map && m.WorkspaceId == workspace)));
        public Task<KnowledgeEdge?> GetEdgeAsync(Guid id, Guid map, Guid workspace, CancellationToken ct) => Task.FromResult(Edges.SingleOrDefault(edge => edge.Id == id && edge.KnowledgeMapId == map && Maps.Any(m => m.Id == map && m.WorkspaceId == workspace)));
        public Task<bool> MapInUseAsync(Guid map, Guid workspace, CancellationToken ct) => Task.FromResult(InUse);
        public Task<bool> NodeInUseAsync(Guid node, Guid map, Guid workspace, CancellationToken ct) => Task.FromResult(InUse);
        public void AddMap(KnowledgeMap map) => Maps.Add(map); public void AddNode(KnowledgeNode node) => Nodes.Add(node); public void AddEdge(KnowledgeEdge edge) => Edges.Add(edge);
        public void RemoveMap(KnowledgeMap map) => Maps.Remove(map); public void RemoveNode(KnowledgeNode node) => Nodes.Remove(node); public void RemoveEdge(KnowledgeEdge edge) => Edges.Remove(edge);
        public Task<KnowledgeWriteStatus> SaveAsync(KnowledgeWriteAttempt attempt, CancellationToken ct) { Saves++; return Failure is null ? Task.FromResult(KnowledgeWriteStatus.Success) : Task.FromException<KnowledgeWriteStatus>(Failure); }
    }
}

using Talynor.Application.Accounts;
using Talynor.Application.Flashcards;
using Talynor.Application.Workspaces;
using Talynor.Domain.Flashcards;
using Talynor.Domain.Workspaces;

namespace Talynor.UnitTests;

public class FlashcardUseCaseTests
{
    [Theory]
    [InlineData(false, "owner")] [InlineData(true, null)] [InlineData(true, "different-account")]
    public async Task Invalid_current_account_never_reaches_card_storage(bool authenticated, string? account)
    {
        var f = new Fixture(); f.Current.Authenticated = authenticated; f.Accounts.Id = account;
        Assert.Equal(FlashcardStatus.AuthenticationRequired, (await f.List.ExecuteAsync(f.Map, f.Node, 1, 20, default)).Status);
        Assert.Equal(FlashcardStatus.AuthenticationRequired, (await f.Get.ExecuteAsync(f.Map, f.Node, f.Card, default)).Status);
        Assert.Equal(FlashcardStatus.AuthenticationRequired, (await f.Create.ExecuteAsync(f.Map, f.Node, new("f", "b"), default)).Status);
        Assert.Equal(FlashcardStatus.AuthenticationRequired, await f.Update.ExecuteAsync(f.Map, f.Node, f.Card, new("changed", FrontSpecified: true), default));
        Assert.Equal(FlashcardStatus.AuthenticationRequired, await f.Delete.ExecuteAsync(f.Map, f.Node, f.Card, default));
        Assert.Equal(0, f.Storage.Calls);
    }

    [Theory]
    [InlineData(0, 20)] [InlineData(-1, 20)] [InlineData(1, 0)] [InlineData(1, -1)]
    [InlineData(1, 101)] [InlineData(int.MaxValue, 100)]
    public async Task Pagination_rejects_invalid_or_overflowing_offset_without_reader(int page, int size)
    {
        var f = new Fixture();
        Assert.Equal(FlashcardStatus.InvalidInput, (await f.List.ExecuteAsync(f.Map, f.Node, page, size, default)).Status);
        Assert.Equal(0, f.Storage.Calls);
    }

    [Fact]
    public async Task Valid_paging_uses_server_workspace_and_keeps_reader_order_and_hasMore()
    {
        var f = new Fixture();
        var result = await f.List.ExecuteAsync(f.Map, f.Node, 3, 10, default);
        Assert.Equal(FlashcardStatus.Success, result.Status); Assert.Equal(3, result.Collection!.Page);
        Assert.Equal(10, result.Collection.PageSize); Assert.True(result.Collection.HasMore);
        Assert.Equal((f.Workspace.Id, f.Map, f.Node), f.Storage.Scope); Assert.Equal((20, 10), f.Storage.Page);
        Assert.Equal(f.Storage.Details, result.Collection.Flashcards);
    }

    [Fact]
    public async Task Missing_parents_or_cards_produce_generic_not_found_and_never_expose_created_details()
    {
        var f = new Fixture(); f.Storage.Exists = false;
        Assert.Equal(FlashcardStatus.NotFound, (await f.List.ExecuteAsync(f.Map, f.Node, 1, 20, default)).Status);
        Assert.Equal(FlashcardStatus.NotFound, (await f.Get.ExecuteAsync(f.Map, f.Node, f.Card, default)).Status);
        Assert.Equal(FlashcardStatus.NotFound, (await f.Create.ExecuteAsync(f.Map, f.Node, new("f", "b"), default)).Status);
        Assert.Null(f.Storage.Created);
        Assert.Equal(FlashcardStatus.NotFound, await f.Update.ExecuteAsync(f.Map, f.Node, f.Card, new("x", FrontSpecified: true), default));
        Assert.Equal(FlashcardStatus.NotFound, await f.Delete.ExecuteAsync(f.Map, f.Node, f.Card, default));
    }

    [Fact]
    public async Task Create_validates_domain_and_uses_server_identity_time_and_revision()
    {
        var f = new Fixture();
        Assert.Equal(FlashcardStatus.InvalidInput, (await f.Create.ExecuteAsync(f.Map, f.Node, new(" ", "b"), default)).Status);
        Assert.Null(f.Storage.Created);
        var result = await f.Create.ExecuteAsync(f.Map, f.Node, new(" f ", " b\n"), default);
        Assert.Equal(FlashcardStatus.Success, result.Status); Assert.Equal(1, result.Card!.ContentRevision);
        Assert.Equal(f.Clock.GetUtcNow(), result.Card.CreatedAtUtc); Assert.NotEqual(Guid.Empty, result.Card.Id);
        Assert.Equal(" f ", result.Card.Front); Assert.Equal(" b\n", result.Card.Back);
        Assert.Equal(f.Node, f.Storage.Created!.KnowledgeNodeId);
    }

    [Theory]
    [InlineData(FlashcardStatus.InvalidInput)] [InlineData(FlashcardStatus.NotFound)] [InlineData(FlashcardStatus.Success)]
    public async Task Update_and_delete_preserve_writer_outcome_and_scope(FlashcardStatus status)
    {
        var f = new Fixture(); f.Storage.WriteStatus = status;
        var input = new UpdateFlashcardInput(Back: "change", BackSpecified: true);
        Assert.Equal(status, await f.Update.ExecuteAsync(f.Map, f.Node, f.Card, input, default));
        Assert.Same(input, f.Storage.Update); Assert.Equal(f.Clock.GetUtcNow(), f.Storage.Now);
        Assert.Equal((f.Workspace.Id, f.Map, f.Node), f.Storage.Scope);
        Assert.Equal(status, await f.Delete.ExecuteAsync(f.Map, f.Node, f.Card, default));
        Assert.Equal(f.Card, f.Storage.CardId);
    }

    [Fact]
    public async Task Cancellation_stops_before_any_storage_and_operational_failures_propagate()
    {
        var f = new Fixture(); var ct = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.List.ExecuteAsync(f.Map, f.Node, 1, 20, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Get.ExecuteAsync(f.Map, f.Node, f.Card, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Create.ExecuteAsync(f.Map, f.Node, new("f", "b"), ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Update.ExecuteAsync(f.Map, f.Node, f.Card, new(), ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Delete.ExecuteAsync(f.Map, f.Node, f.Card, ct));
        Assert.Equal(0, f.Storage.Calls);
        f.Storage.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Get.ExecuteAsync(f.Map, f.Node, f.Card, default));
    }

    private sealed class Fixture
    {
        public Guid Map = Guid.NewGuid(), Node = Guid.NewGuid(), Card = Guid.NewGuid();
        public PersonalWorkspace Workspace = PersonalWorkspace.Create("owner", DateTimeOffset.UnixEpoch);
        public Current Current = new(); public Accounts Accounts = new(); public Clock Clock = new(); public Storage Storage = new();
        private GetOrCreatePersonalWorkspace Resolver => new(Current, Accounts, new WorkspaceStore(Workspace), Clock);
        public ListFlashcards List => new(Resolver, Storage); public GetFlashcard Get => new(Resolver, Storage);
        public CreateFlashcard Create => new(Resolver, Storage, Clock); public UpdateFlashcard Update => new(Resolver, Storage, Clock);
        public DeleteFlashcard Delete => new(Resolver, Storage);
    }
    private sealed class Current : ICurrentUser { public bool Authenticated = true; public bool IsAuthenticated => Authenticated; public string? UserId => "owner"; }
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
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddDays(1); }
    private sealed class Storage : IFlashcardReader, IFlashcardStore
    {
        public int Calls; public bool Exists = true, Fail; public FlashcardStatus WriteStatus = FlashcardStatus.Success;
        public (Guid Workspace, Guid Map, Guid Node) Scope; public (int Offset, int Size) Page;
        public Flashcard? Created; public UpdateFlashcardInput? Update; public Guid CardId; public DateTimeOffset Now;
        public FlashcardDetail[] Details = [FlashcardDetail.From(Flashcard.Create(Guid.NewGuid(), "second", "back", DateTimeOffset.UnixEpoch)),
            FlashcardDetail.From(Flashcard.Create(Guid.NewGuid(), "first", "back", DateTimeOffset.UnixEpoch))];
        private void Capture(Guid w, Guid m, Guid n) { Calls++; Scope = (w, m, n); if (Fail) throw new InvalidOperationException("Synthetic failure"); }
        public Task<FlashcardPage?> ListAsync(Guid w, Guid m, Guid n, int offset, int size, CancellationToken ct)
        { Capture(w, m, n); Page = (offset, size); return Task.FromResult(Exists ? new FlashcardPage(Details, true) : null); }
        public Task<FlashcardDetail?> GetAsync(Guid w, Guid m, Guid n, Guid id, CancellationToken ct)
        { Capture(w, m, n); return Task.FromResult(Exists ? Details[0] : null); }
        public Task<bool> NodeExistsAsync(Guid w, Guid m, Guid n, CancellationToken ct) { Capture(w, m, n); return Task.FromResult(Exists); }
        public Task<FlashcardStatus> CreateAsync(Guid w, Guid m, Flashcard card, CancellationToken ct)
        { Capture(w, m, card.KnowledgeNodeId); Created = card; return Task.FromResult(WriteStatus); }
        public Task<FlashcardStatus> UpdateAsync(Guid w, Guid m, Guid n, Guid id, UpdateFlashcardInput input, DateTimeOffset now, CancellationToken ct)
        { Capture(w, m, n); CardId = id; Update = input; Now = now; return Task.FromResult(Exists ? WriteStatus : FlashcardStatus.NotFound); }
        public Task<FlashcardStatus> DeleteAsync(Guid w, Guid m, Guid n, Guid id, CancellationToken ct)
        { Capture(w, m, n); CardId = id; return Task.FromResult(Exists ? WriteStatus : FlashcardStatus.NotFound); }
    }
}

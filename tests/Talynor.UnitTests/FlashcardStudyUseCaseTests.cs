using Talynor.Application.Accounts;
using Talynor.Application.Flashcards;
using Talynor.Application.Workspaces;
using Talynor.Domain.Flashcards;
using Talynor.Domain.Workspaces;

namespace Talynor.UnitTests;

public class FlashcardStudyUseCaseTests
{
    [Fact]
    public async Task Next_returns_empty_success_for_owned_empty_node_and_not_found_for_missing_node()
    {
        var f = new Fixture(); f.Reader.Next = new(true, null);
        var empty = await f.Next.ExecuteAsync(f.Map, f.Node, default);
        Assert.Equal(FlashcardStatus.Success, empty.Status); Assert.Null(empty.Item);
        f.Reader.Next = new(false, null);
        Assert.Equal(FlashcardStatus.NotFound, (await f.Next.ExecuteAsync(f.Map, f.Node, default)).Status);
    }

    [Fact]
    public async Task Reveal_returns_only_matching_revision_and_reports_stale_card()
    {
        var f = new Fixture(); f.Reader.Reveal = new(2, "answer");
        Assert.Equal(FlashcardStatus.ContentChanged,
            (await f.Reveal.ExecuteAsync(f.Map, f.Node, f.Card, 1, default)).Status);
        f.Reader.Reveal = new(2, "answer");
        var result = await f.Reveal.ExecuteAsync(f.Map, f.Node, f.Card, 2, default);
        Assert.Equal(FlashcardStatus.Success, result.Status); Assert.Equal("answer", result.Reveal!.Back);
        Assert.Equal(FlashcardStatus.InvalidInput,
            (await f.Reveal.ExecuteAsync(f.Map, f.Node, f.Card, 0, default)).Status);
    }

    [Theory]
    [InlineData("correct", FlashcardAnswerResult.Correct)]
    [InlineData("incorrect", FlashcardAnswerResult.Incorrect)]
    public async Task Review_accepts_only_stable_lowercase_results(string value, FlashcardAnswerResult expected)
    {
        var f = new Fixture();
        var result = await f.Review.ExecuteAsync(f.Map, f.Node, f.Card, f.ReviewId, 1, value, default);
        Assert.Equal(FlashcardStatus.Success, result.Status);
        Assert.Equal(expected, f.Store.Result);
        Assert.Equal(f.ReviewId, f.Store.ReviewId);
        Assert.Equal(FlashcardStatus.InvalidInput,
            (await f.Review.ExecuteAsync(f.Map, f.Node, f.Card, Guid.NewGuid(), 1, "Correct", default)).Status);
    }

    [Fact]
    public async Task Review_and_lookup_require_nonempty_ids_and_use_current_workspace()
    {
        var f = new Fixture();
        Assert.Equal(FlashcardStatus.InvalidInput,
            (await f.Review.ExecuteAsync(f.Map, f.Node, f.Card, Guid.Empty, 1, "correct", default)).Status);
        Assert.Equal(FlashcardStatus.InvalidInput,
            (await f.GetReview.ExecuteAsync(f.Map, f.Node, f.Card, Guid.Empty, default)).Status);
        var read = await f.GetReview.ExecuteAsync(f.Map, f.Node, f.Card, f.ReviewId, default);
        Assert.Equal(FlashcardStatus.Success, read.Status);
        Assert.Equal(f.Workspace.Id, f.Reader.WorkspaceId);
    }

    private sealed class Fixture
    {
        public Guid Map = Guid.NewGuid(), Node = Guid.NewGuid(), Card = Guid.NewGuid(), ReviewId = Guid.NewGuid();
        public PersonalWorkspace Workspace = PersonalWorkspace.Create("owner", DateTimeOffset.UnixEpoch);
        public Current Current = new(); public Accounts Accounts = new(); public Reader Reader = new(); public Store Store = new();
        private GetOrCreatePersonalWorkspace Resolver => new(Current, Accounts, new WorkspaceStore(Workspace), TimeProvider.System);
        public GetNextFlashcardForStudy Next => new(Resolver, Reader);
        public RevealFlashcard Reveal => new(Resolver, Reader);
        public ReviewFlashcard Review => new(Resolver, Store);
        public GetFlashcardReview GetReview => new(Resolver, Reader);
    }

    private sealed class Current : ICurrentUser { public bool IsAuthenticated => true; public string? UserId => "owner"; }
    private sealed class Accounts : IIdentityAccountService
    {
        public Task<AccountSummary?> GetByIdAsync(string id, CancellationToken ct) =>
            Task.FromResult<AccountSummary?>(new(id, "owner@example.invalid"));
        public Task<RegisterUserResult> CreateAsync(string email, string password, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class WorkspaceStore(PersonalWorkspace workspace) : IPersonalWorkspaceStore
    {
        public Task<PersonalWorkspace?> GetByOwnerUserIdAsync(string id, CancellationToken ct) => Task.FromResult<PersonalWorkspace?>(workspace);
        public Task<PersonalWorkspace> AddOrGetExistingAsync(PersonalWorkspace value, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Reader : IFlashcardStudyReader
    {
        public FlashcardStudyNextRead Next = new(true, null);
        public FlashcardRevealRead? Reveal;
        public FlashcardReviewView? ReviewView = new(new(Guid.NewGuid(), Guid.NewGuid(), 1,
            FlashcardAnswerResult.Correct, DateTimeOffset.UnixEpoch), new(1, 1, 1, DateTimeOffset.UnixEpoch));
        public Guid WorkspaceId;
        public Task<FlashcardStudyNextRead> GetNextForStudyAsync(Guid workspaceId, Guid mapId, Guid nodeId, CancellationToken ct)
        { WorkspaceId = workspaceId; return Task.FromResult(Next); }
        public Task<FlashcardRevealRead?> GetRevealAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid cardId, int revision, CancellationToken ct)
        { WorkspaceId = workspaceId; return Task.FromResult(Reveal); }
        public Task<FlashcardReviewView?> GetReviewAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid cardId, Guid reviewId, CancellationToken ct)
        { WorkspaceId = workspaceId; return Task.FromResult(ReviewView); }
    }
    private sealed class Store : IFlashcardStudyStore
    {
        public FlashcardAnswerResult Result; public Guid ReviewId;
        public Task<FlashcardReviewOperationResult> ReviewAsync(Guid workspaceId, Guid mapId, Guid nodeId, Guid cardId,
            Guid reviewId, int revision, FlashcardAnswerResult result, CancellationToken ct)
        { Result = result; ReviewId = reviewId; return Task.FromResult(new FlashcardReviewOperationResult(FlashcardStatus.Success)); }
    }
}

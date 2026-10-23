using Talynor.Application.Accounts;
using Talynor.Application.Progress;
using Talynor.Application.Workspaces;
using Talynor.Domain.Workspaces;

namespace Talynor.UnitTests;

public class NodeProgressTests
{
    [Theory]
    [InlineData(7)] [InlineData(30)] [InlineData(90)]
    public async Task One_UTC_reference_and_exact_elapsed_days_are_forwarded_with_server_workspace(int days)
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        var result = await f.UseCase.ExecuteAsync(f.Map, f.Node, days, cancellation.Token);
        Assert.Equal(NodeProgressStatus.Success, result.Status);
        var summary = result.Summary!;
        Assert.Equal(1, f.Clock.Calls); Assert.Equal(TimeSpan.Zero, summary.Period.ToUtc.Offset);
        Assert.Equal(f.Clock.Now.ToUniversalTime(), summary.CalculatedAtUtc);
        Assert.Equal(summary.CalculatedAtUtc, summary.Period.ToUtc);
        Assert.Equal(TimeSpan.FromDays(days), summary.Period.ToUtc - summary.Period.FromUtc);
        Assert.Equal(days, summary.Period.Days);
        Assert.Equal((f.Store.Existing!.Id, f.Map, f.Node, cancellation.Token), f.Reader.Call);
        Assert.Same(summary.Period, f.Reader.Period);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(1)] [InlineData(8)] [InlineData(31)] [InlineData(91)] [InlineData(int.MaxValue)]
    public async Task Unsupported_presets_are_invalid_without_reader_or_clock_and_no_HTTP_parsing(int days)
    {
        var f = new Fixture();
        Assert.Equal(NodeProgressStatus.Invalid, (await f.UseCase.ExecuteAsync(f.Map, f.Node, days, default)).Status);
        Assert.Equal(0, f.Reader.Calls); Assert.Equal(0, f.Clock.Calls);
        Assert.Throws<ArgumentOutOfRangeException>(() => NodeProgressPeriod.Create(days, f.Clock.Now));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Empty_ids_are_invalid(bool map)
    {
        var f = new Fixture();
        Assert.Equal(NodeProgressStatus.Invalid, (await f.UseCase.ExecuteAsync(map ? Guid.Empty : f.Map, map ? f.Node : Guid.Empty, 30, default)).Status);
        Assert.Equal(0, f.Reader.Calls);
    }

    [Theory]
    [InlineData("anonymous")] [InlineData("missing-id")] [InlineData("missing-account")] [InlineData("wrong-account")]
    public async Task Account_validation_precedes_reader_and_cannot_disclose_activity(string scenario)
    {
        var f = new Fixture();
        f.Current.IsAuthenticated = scenario != "anonymous";
        if (scenario == "missing-id") f.Current.UserId = null;
        if (scenario == "missing-account") f.Accounts.Id = null;
        if (scenario == "wrong-account") f.Accounts.Id = "other";
        Assert.Equal(NodeProgressStatus.AuthenticationRequired, (await f.UseCase.ExecuteAsync(f.Map, f.Node, 30, default)).Status);
        Assert.Equal(0, f.Reader.Calls); Assert.Equal(0, f.Store.Reads); Assert.Equal(0, f.Clock.Calls);
    }

    [Fact]
    public async Task Existing_lazy_workspace_resolution_remains_the_only_possible_initialization()
    {
        var f = new Fixture(); f.Store.Existing = null;
        var result = await f.UseCase.ExecuteAsync(f.Map, f.Node, 30, default);
        Assert.Equal(NodeProgressStatus.Success, result.Status);
        Assert.Equal("owner", f.Store.Added!.OwnerUserId); Assert.Equal(f.Store.Added.Id, f.Reader.Call.Workspace);
        Assert.Equal(1, f.Clock.Calls);
    }

    [Fact]
    public async Task Missing_parent_is_distinct_from_an_owned_empty_node()
    {
        var f = new Fixture(); f.Reader.Missing = true;
        var absent = await f.UseCase.ExecuteAsync(f.Map, f.Node, 30, default);
        Assert.Equal(NodeProgressStatus.NotFound, absent.Status); Assert.Null(absent.Summary);
        f.Reader.Missing = false;
        var empty = (await f.UseCase.ExecuteAsync(f.Map, f.Node, 30, default)).Summary!;
        Assert.Equal(new StudyProgress(0, 0), empty.Study);
        Assert.Equal(new FlashcardProgress(0, 0, null), empty.Flashcards);
        Assert.Equal(new QuizProgress(0, 0, 0, null), empty.Quizzes);
    }

    [Fact]
    public async Task Clock_range_underflow_is_operational_not_an_invalid_preset_or_empty_summary()
    {
        var f = new Fixture(); f.Clock.Now = DateTimeOffset.MinValue;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => f.UseCase.ExecuteAsync(f.Map, f.Node, 90, default));
        Assert.Equal(0, f.Reader.Calls);
    }

    [Theory]
    [InlineData("account")] [InlineData("workspace")] [InlineData("reader")]
    public async Task Operational_failures_propagate_without_fabricated_zeros(string source)
    {
        var f = new Fixture(); var error = new InvalidOperationException("Synthetic failure.");
        if (source == "account") f.Accounts.Failure = error;
        else if (source == "workspace") f.Store.Failure = error;
        else f.Reader.Failure = error;
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => f.UseCase.ExecuteAsync(f.Map, f.Node, 30, default)));
    }

    [Fact]
    public async Task Cancellation_prevents_all_work()
    {
        var f = new Fixture();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.UseCase.ExecuteAsync(f.Map, f.Node, 30, new(true)));
        Assert.Equal(0, f.Reader.Calls); Assert.Equal(0, f.Store.Reads); Assert.Equal(0, f.Clock.Calls);
    }

    [Theory]
    [InlineData(2, 3, 66.67)] [InlineData(1, 32, 3.13)] [InlineData(0, 3, 0)] [InlineData(3, 3, 100)]
    public void Review_ratios_use_decimal_two_places_and_AwayFromZero(int correct, int total, decimal expected) =>
        Assert.Equal(expected, NodeProgressCalculation.Flashcards(total, correct).CorrectPercentage);

    [Theory]
    [InlineData(2, 11, 6, 54.55)] [InlineData(2, 4, 1, 25)]
    [InlineData(1, 3, 2, 66.67)] [InlineData(4, 32, 1, 3.13)]
    public void Quiz_accuracy_weights_questions_not_already_rounded_attempt_percentages(int attempts, int questions, int correct, decimal expected) =>
        Assert.Equal(expected, NodeProgressCalculation.Quizzes(attempts, questions, correct).AccuracyPercentage);

    [Fact]
    public void Exact_unrounded_ticks_are_floored_once_and_can_exceed_long_tick_capacity()
    {
        Assert.Equal(new StudyProgress(2, 1), NodeProgressCalculation.Study(2, 6_000_000m + 6_000_000m));
        Assert.Equal(new StudyProgress(1, 0), NodeProgressCalculation.Study(1, 1));
        Assert.Equal(new StudyProgress(1, 0), NodeProgressCalculation.Study(1, 0));
        var max = NodeProgressCalculation.MaxSafeInteger;
        Assert.Equal(max, NodeProgressCalculation.Study(max, max * (decimal)TimeSpan.TicksPerSecond + 9_999_999m).TotalDurationSeconds);
        Assert.Throws<InvalidOperationException>(() => NodeProgressCalculation.Study(max, (max + 1m) * TimeSpan.TicksPerSecond));
    }

    [Theory]
    [InlineData(-1)] [InlineData(9_007_199_254_740_992)] [InlineData(long.MaxValue)]
    public void Unsafe_count_values_fail_closed(long bad)
    {
        Assert.Throws<InvalidOperationException>(() => NodeProgressCalculation.Study(bad, 0));
        Assert.Throws<InvalidOperationException>(() => NodeProgressCalculation.Flashcards(bad, 0));
        Assert.Throws<InvalidOperationException>(() => NodeProgressCalculation.Flashcards(0, bad));
        Assert.Throws<InvalidOperationException>(() => NodeProgressCalculation.Quizzes(bad, 0, 0));
        Assert.Throws<InvalidOperationException>(() => NodeProgressCalculation.Quizzes(0, bad, 0));
        Assert.Throws<InvalidOperationException>(() => NodeProgressCalculation.Quizzes(0, 0, bad));
    }

    [Fact]
    public void Corrupt_relationships_negative_fractional_ticks_and_impossible_sums_are_not_clamped()
    {
        Assert.Throws<InvalidOperationException>(() => NodeProgressCalculation.Study(1, -1));
        Assert.Throws<InvalidOperationException>(() => NodeProgressCalculation.Study(1, .1m));
        Assert.Throws<InvalidOperationException>(() => NodeProgressCalculation.Study(0, 1));
        Assert.Throws<InvalidOperationException>(() => NodeProgressCalculation.Study(1, decimal.MaxValue));
        Assert.Throws<InvalidOperationException>(() => NodeProgressCalculation.Flashcards(1, 2));
        Assert.Throws<InvalidOperationException>(() => NodeProgressCalculation.Quizzes(1, 0, 0));
        Assert.Throws<InvalidOperationException>(() => NodeProgressCalculation.Quizzes(0, 1, 0));
        Assert.Throws<InvalidOperationException>(() => NodeProgressCalculation.Quizzes(1, 11, 0));
        Assert.Throws<InvalidOperationException>(() => NodeProgressCalculation.Quizzes(1, 1, 2));
    }

    [Fact]
    public void Maximum_safe_counts_do_not_overflow_decimal_percentage_arithmetic()
    {
        var max = NodeProgressCalculation.MaxSafeInteger;
        Assert.Equal(100m, NodeProgressCalculation.Flashcards(max, max).CorrectPercentage);
        Assert.Equal(100m, NodeProgressCalculation.Quizzes(max, max, max).AccuracyPercentage);
    }

    private sealed class Fixture
    {
        public Guid Map { get; } = Guid.NewGuid(); public Guid Node { get; } = Guid.NewGuid();
        public Current Current = new(); public Accounts Accounts = new(); public Store Store = new();
        public Clock Clock = new(); public Reader Reader = new();
        public GetNodeProgress UseCase => new(new(Current, Accounts, Store, TimeProvider.System), Reader, Clock);
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.FromHours(-3)); public int Calls;
        public override DateTimeOffset GetUtcNow() => Now.AddDays(Calls++);
    }
    private sealed class Current : ICurrentUser
    { public bool IsAuthenticated { get; set; } = true; public string? UserId { get; set; } = "owner"; }
    private sealed class Accounts : IIdentityAccountService
    {
        public string? Id = "owner"; public Exception? Failure;
        public Task<AccountSummary?> GetByIdAsync(string id, CancellationToken ct) => Failure is not null
            ? Task.FromException<AccountSummary?>(Failure) : Task.FromResult(Id is null ? null : new AccountSummary(Id, "test@example.invalid"));
        public Task<RegisterUserResult> CreateAsync(string email, string password, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Store : IPersonalWorkspaceStore
    {
        public PersonalWorkspace? Existing = PersonalWorkspace.Create("owner", DateTimeOffset.UnixEpoch);
        public PersonalWorkspace? Added; public Exception? Failure; public int Reads;
        public Task<PersonalWorkspace?> GetByOwnerUserIdAsync(string id, CancellationToken ct)
        { Reads++; return Failure is not null ? Task.FromException<PersonalWorkspace?>(Failure) : Task.FromResult(Existing); }
        public Task<PersonalWorkspace> AddOrGetExistingAsync(PersonalWorkspace workspace, CancellationToken ct)
        { Added = workspace; return Task.FromResult(workspace); }
    }
    private sealed class Reader : INodeProgressReader
    {
        public bool Missing; public Exception? Failure; public int Calls; public NodeProgressPeriod? Period;
        public (Guid Workspace, Guid Map, Guid Node, CancellationToken Token) Call;
        public Task<NodeProgressSummary?> ReadAsync(Guid workspace, Guid map, Guid node, NodeProgressPeriod period, CancellationToken ct)
        {
            Calls++; Call = (workspace, map, node, ct); Period = period;
            return Failure is not null ? Task.FromException<NodeProgressSummary?>(Failure) : Task.FromResult(Missing ? null :
                new NodeProgressSummary(period, NodeProgressCalculation.Study(0, 0), NodeProgressCalculation.Flashcards(0, 0), NodeProgressCalculation.Quizzes(0, 0, 0)));
        }
    }
}

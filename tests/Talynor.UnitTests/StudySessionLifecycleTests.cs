using Talynor.Application.Accounts;
using Talynor.Application.StudySessions;
using Talynor.Application.Workspaces;
using Talynor.Domain.StudySessions;
using Talynor.Domain.Workspaces;

namespace Talynor.UnitTests;

public class StudySessionLifecycleTests
{
    [Theory]
    [InlineData("start")] [InlineData("complete")] [InlineData("delete")] [InlineData("active")]
    public async Task Commands_forward_server_workspace_clock_and_cancellation(string operation)
    {
        var f = new Fixture(); using var cancel = new CancellationTokenSource();
        await f.Run(operation, cancel.Token);
        Assert.Equal(f.Workspace.Id, f.Port.Workspace); Assert.Equal(cancel.Token, f.Port.Token);
        if (operation == "start")
        {
            Assert.NotEqual(Guid.Empty, f.Port.Session!.Id); Assert.Equal(f.Node, f.Port.Session.KnowledgeNodeId);
            Assert.Equal(f.Clock.GetUtcNow(), f.Port.Session.StartedAtUtc); Assert.Null(f.Port.Session.EndedAtUtc);
        }
        if (operation == "complete") Assert.Equal(f.Clock.GetUtcNow(), f.Port.Now);
        if (operation == "active")
        {
            Assert.Null(f.Active!.Active); Assert.Equal(f.Clock.GetUtcNow(), f.Active.ServerTimeUtc);
        }
        else Assert.Equal(f.Map, f.Port.Map);
    }

    [Theory]
    [InlineData("start")] [InlineData("complete")] [InlineData("delete")] [InlineData("active")]
    public async Task Invalid_or_removed_account_never_reaches_session_port(string operation)
    {
        var f = new Fixture(); f.Account.Exists = false;
        await f.Run(operation, CancellationToken.None);
        Assert.Equal(0, f.Port.Calls);
        if (operation == "active") Assert.Equal(StudySessionReadStatus.AuthenticationRequired, f.Active!.Status);
        else Assert.Equal(StudySessionWriteStatus.AuthenticationRequired, f.Status);
    }

    [Theory]
    [InlineData("start")] [InlineData("complete")] [InlineData("delete")] [InlineData("active")]
    public async Task Cancellation_stops_before_account_or_store_and_failures_are_not_business_conflicts(string operation)
    {
        var f = new Fixture();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Run(operation, new CancellationToken(true)));
        Assert.Equal(0, f.Account.Calls); Assert.Equal(0, f.Port.Calls);
        f.Port.Failure = new InvalidOperationException("Synthetic failure.");
        Assert.Same(f.Port.Failure, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Run(operation, CancellationToken.None)));
    }

    [Theory]
    [InlineData(StudySessionWriteStatus.NotFound)] [InlineData(StudySessionWriteStatus.ActiveSessionExists)]
    public async Task Start_preserves_store_result_without_replay(StudySessionWriteStatus status)
    {
        var f = new Fixture(); f.Port.Status = status;
        await f.Run("start", CancellationToken.None); Assert.Equal(status, f.Status); Assert.Equal(1, f.Port.Calls);
    }

    private sealed class Fixture
    {
        public Guid Map = Guid.NewGuid(), Node = Guid.NewGuid(), Id = Guid.NewGuid();
        public Clock Clock = new(); public Account Account = new(); public Port Port = new();
        public PersonalWorkspace Workspace = PersonalWorkspace.Create("owner", DateTimeOffset.UnixEpoch);
        public StudySessionWriteStatus Status; public ActiveStudySessionResult? Active;
        public async Task Run(string operation, CancellationToken ct)
        {
            var resolver = new GetOrCreatePersonalWorkspace(new Current(), Account, new WorkspacePort(Workspace), Clock);
            switch (operation)
            {
                case "start": Status = (await new StartStudySession(resolver, Port, Clock).ExecuteAsync(Map, Node, ct)).Status; break;
                case "complete": Status = await new CompleteStudySession(resolver, Port, Clock).ExecuteAsync(Map, Node, Id, ct); break;
                case "delete": Status = await new DeleteStudySession(resolver, Port).ExecuteAsync(Map, Node, Id, ct); break;
                case "active": Active = await new GetActiveStudySession(resolver, Port, Clock).ExecuteAsync(ct); break;
            }
        }
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(1234567); }
    private sealed class Current : ICurrentUser { public bool IsAuthenticated => true; public string UserId => "owner"; }
    private sealed class Account : IIdentityAccountService
    {
        public bool Exists = true; public int Calls;
        public Task<AccountSummary?> GetByIdAsync(string id, CancellationToken ct)
        { Calls++; return Task.FromResult(Exists ? new AccountSummary(id, "test@example.invalid") : null); }
        public Task<RegisterUserResult> CreateAsync(string email, string password, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class WorkspacePort(PersonalWorkspace workspace) : IPersonalWorkspaceStore
    {
        public Task<PersonalWorkspace?> GetByOwnerUserIdAsync(string id, CancellationToken ct) => Task.FromResult<PersonalWorkspace?>(workspace);
        public Task<PersonalWorkspace> AddOrGetExistingAsync(PersonalWorkspace value, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Port : IStudySessionStore, IStudySessionReader
    {
        public Guid Workspace, Map; public DateTimeOffset Now; public CancellationToken Token; public int Calls;
        public StudySession? Session; public Exception? Failure; public StudySessionWriteStatus Status = StudySessionWriteStatus.Success;
        private void Called(Guid workspace, CancellationToken ct) { Calls++; Workspace = workspace; Token = ct; if (Failure is not null) throw Failure; }
        public Task<StartStudySessionResult> StartAsync(Guid workspace, Guid map, StudySession session, CancellationToken ct)
        { Called(workspace, ct); Map = map; Session = session; return Task.FromResult(new StartStudySessionResult(Status)); }
        public Task<StudySessionWriteStatus> CompleteAsync(Guid workspace, Guid map, Guid node, Guid id, DateTimeOffset now, CancellationToken ct)
        { Called(workspace, ct); Map = map; Now = now; return Task.FromResult(Status); }
        public Task<StudySessionWriteStatus> DeleteAsync(Guid workspace, Guid map, Guid node, Guid id, CancellationToken ct)
        { Called(workspace, ct); Map = map; return Task.FromResult(Status); }
        public Task<ActiveStudySession?> GetActiveAsync(Guid workspace, CancellationToken ct)
        { Called(workspace, ct); return Task.FromResult<ActiveStudySession?>(null); }
        public Task<StudySessionPage?> ListCompletedForNodeAsync(Guid workspace, Guid map, Guid node, int offset, int size, CancellationToken ct) => throw new NotSupportedException();
        public Task<StudySessionReadModel?> GetForNodeAsync(Guid workspace, Guid map, Guid node, Guid id, CancellationToken ct) => throw new NotSupportedException();
    }
}

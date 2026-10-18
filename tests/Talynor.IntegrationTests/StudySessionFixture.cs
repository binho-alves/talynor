using Talynor.Domain.StudySessions;
using Talynor.Domain.Workspaces;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

// Explicit fixtures only: no runtime endpoint, seed or writer creates StudySessions in this checkpoint.
internal sealed record StudySessionFixture(KnowledgeContentFixture Content, StudySession Early, StudySession High,
    StudySession Low, StudySession Active, StudySession Foreign)
{
    public KnowledgeGraphFixture Graph => Content.Graph;
    public static readonly DateTimeOffset Instant = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567);

    public static async Task<StudySessionFixture> CreateAsync(TalynorDbContext db,
        PersonalWorkspace? workspace = null, PersonalWorkspace? other = null)
    {
        var content = await KnowledgeContentFixture.CreateAsync(db, workspace, other);
        var node = content.Graph.First.Id;
        var early = new StudySession(Guid.NewGuid(), node, Instant.AddMinutes(-1), Instant);
        var low = new StudySession(Guid.Parse("00000000-0000-0000-0000-000000000001"), node,
            Instant.ToOffset(TimeSpan.FromHours(-3)), Instant.AddTicks(1234567).ToOffset(TimeSpan.FromHours(5)));
        var high = new StudySession(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), node, Instant, Instant);
        var active = new StudySession(Guid.NewGuid(), node, Instant.AddDays(1), null);
        var foreign = new StudySession(Guid.NewGuid(), content.ForeignNode.Id, Instant, Instant.AddMinutes(1));
        // Low-before-high insertion with equal starts makes the descending ID tie-break observable.
        db.StudySessions.AddRange(low, early, high, active, foreign);
        await db.SaveChangesAsync();
        return new(content, early, high, low, active, foreign);
    }
}

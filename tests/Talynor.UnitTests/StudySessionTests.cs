using Talynor.Domain.StudySessions;

namespace Talynor.UnitTests;

public class StudySessionTests
{
    [Fact]
    public void Active_record_has_normalized_start_and_no_duration()
    {
        var id = Guid.NewGuid(); var node = Guid.NewGuid();
        var start = new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.FromHours(-3));
        var session = new StudySession(id, node, start, null);
        Assert.Equal(id, session.Id); Assert.Equal(node, session.KnowledgeNodeId);
        Assert.Equal(start.UtcDateTime, session.StartedAtUtc.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, session.StartedAtUtc.Offset);
        Assert.Null(session.EndedAtUtc); Assert.Null(session.Duration);
        Assert.Equal(StudySessionStatus.Active, session.Status);
    }

    [Theory]
    [InlineData(0L)] [InlineData(1L)] [InlineData(123456789L)]
    public void Completed_means_ended_record_with_exact_derived_duration_including_zero(long ticks)
    {
        var start = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(-3));
        var end = start.AddTicks(ticks).ToOffset(TimeSpan.FromHours(5));
        var session = new StudySession(Guid.NewGuid(), Guid.NewGuid(), start, end);
        Assert.Equal(TimeSpan.FromTicks(ticks), session.Duration);
        Assert.Equal(TimeSpan.Zero, session.StartedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, session.EndedAtUtc!.Value.Offset);
        Assert.Equal(StudySessionStatus.Completed, session.Status);
    }

    [Fact]
    public void Negative_duration_is_rejected_by_instant_even_with_different_offsets()
    {
        var start = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(-3));
        Assert.Throws<ArgumentException>(() => new StudySession(Guid.NewGuid(), Guid.NewGuid(), start,
            start.AddTicks(-1).ToOffset(TimeSpan.FromHours(5))));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Empty_identity_or_node_is_rejected(bool emptyId) =>
        Assert.Throws<ArgumentException>(() => new StudySession(emptyId ? Guid.Empty : Guid.NewGuid(),
            emptyId ? Guid.NewGuid() : Guid.Empty, DateTimeOffset.UnixEpoch, null));

    [Fact]
    public void Persisted_properties_remain_encapsulated_without_public_constructor()
    {
        Assert.Empty(typeof(StudySession).GetConstructors());
        foreach (var name in new[] { "Id", "KnowledgeNodeId", "StartedAtUtc", "EndedAtUtc" })
            Assert.False(typeof(StudySession).GetProperty(name)!.SetMethod!.IsPublic);
        Assert.Null(typeof(StudySession).GetProperty("Duration")!.SetMethod);
        Assert.Null(typeof(StudySession).GetProperty("Status")!.SetMethod);
    }

    [Fact]
    public void Start_generates_identity_and_complete_preserves_first_utc_end()
    {
        var session = StudySession.Start(Guid.NewGuid(), DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(-3)));
        Assert.NotEqual(Guid.Empty, session.Id); Assert.Null(session.EndedAtUtc);
        Assert.False(session.TryComplete(DateTimeOffset.UnixEpoch.AddTicks(-1)));
        Assert.Null(session.EndedAtUtc);
        Assert.True(session.TryComplete(DateTimeOffset.UnixEpoch.AddTicks(1).ToOffset(TimeSpan.FromHours(5))));
        Assert.Equal(TimeSpan.FromTicks(1), session.Duration);
        Assert.True(session.TryComplete(DateTimeOffset.UnixEpoch.AddDays(1)));
        Assert.True(session.TryComplete(DateTimeOffset.UnixEpoch.AddDays(-1)));
        Assert.Equal(DateTimeOffset.UnixEpoch.AddTicks(1), session.EndedAtUtc);
    }
}

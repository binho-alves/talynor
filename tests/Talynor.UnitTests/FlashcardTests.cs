using Talynor.Domain.Flashcards;

namespace Talynor.UnitTests;

public class FlashcardTests
{
    private static readonly DateTimeOffset Instant = new(2026, 10, 20, 9, 1, 2, TimeSpan.FromHours(-3));

    [Fact]
    public void Creation_generates_identity_revision_one_and_normalized_utc_without_normalizing_text()
    {
        var node = Guid.NewGuid();
        var card = Flashcard.Create(node, "  # <b>Question</b>\r\n ", " \nAnswer\t ", Instant);
        Assert.NotEqual(Guid.Empty, card.Id); Assert.Equal(node, card.KnowledgeNodeId);
        Assert.Equal(1, card.ContentRevision); Assert.Equal("  # <b>Question</b>\r\n ", card.Front);
        Assert.Equal(" \nAnswer\t ", card.Back); Assert.Equal(Instant.ToUniversalTime(), card.CreatedAtUtc);
        Assert.Equal(card.CreatedAtUtc, card.UpdatedAtUtc); Assert.Equal(TimeSpan.Zero, card.CreatedAtUtc.Offset);
        Assert.NotEqual(card.Id, Flashcard.Create(node, "front", "back", Instant).Id);
    }

    [Theory]
    [InlineData(null, "back")] [InlineData("", "back")] [InlineData(" \r\n\t", "back")]
    [InlineData("front", null)] [InlineData("front", "")] [InlineData("front", " \r\n\t")]
    public void Both_sides_require_nonblank_text(string? front, string? back) =>
        Assert.Throws<ArgumentException>(() => Flashcard.Create(Guid.NewGuid(), front!, back!, Instant));

    [Theory]
    [InlineData(2000, 4000, true)] [InlineData(2001, 4000, false)] [InlineData(2000, 4001, false)]
    public void Limits_are_utf16_code_units(int front, int back, bool valid)
    {
        if (valid) Assert.Equal(front, Flashcard.Create(Guid.NewGuid(), new('f', front), new('b', back), Instant).Front.Length);
        else Assert.Throws<ArgumentException>(() => Flashcard.Create(Guid.NewGuid(), new('f', front), new('b', back), Instant));
        // A supplementary character uses two UTF-16 units.
        Assert.Equal(2000, Flashcard.Create(Guid.NewGuid(), string.Concat(Enumerable.Repeat("😀", 1000)), "b", Instant).Front.Length);
        Assert.Throws<ArgumentException>(() => Flashcard.Create(Guid.NewGuid(), string.Concat(Enumerable.Repeat("😀", 1001)), "b", Instant));
    }

    [Fact]
    public void Empty_node_id_is_invalid() => Assert.Throws<ArgumentException>(() => Flashcard.Create(Guid.Empty, "f", "b", Instant));

    [Theory]
    [InlineData("Front", "back")] [InlineData("front", "Back")] [InlineData("Front", "Back")]
    [InlineData(" front", "back")] [InlineData("front\r\n", "back")]
    public void Any_ordinal_text_change_increments_once_and_preserves_identity_and_creation(string front, string back)
    {
        var card = Flashcard.Create(Guid.NewGuid(), "front", "back", Instant);
        var identity = (card.Id, card.KnowledgeNodeId, card.CreatedAtUtc);
        Assert.True(card.Update(front, back, Instant.AddMinutes(1)));
        Assert.Equal(2, card.ContentRevision); Assert.Equal(front, card.Front); Assert.Equal(back, card.Back);
        Assert.Equal(identity, (card.Id, card.KnowledgeNodeId, card.CreatedAtUtc));
        Assert.Equal(Instant.AddMinutes(1).ToUniversalTime(), card.UpdatedAtUtc); Assert.Equal(TimeSpan.Zero, card.UpdatedAtUtc.Offset);
        Assert.False(card.Update(front, back, Instant.AddMinutes(2)));
        Assert.Equal(2, card.ContentRevision); Assert.Equal(Instant.AddMinutes(1).ToUniversalTime(), card.UpdatedAtUtc);
    }

    [Fact]
    public void Unicode_normalization_is_not_applied_and_invalid_candidate_never_partially_mutates()
    {
        var card = Flashcard.Create(Guid.NewGuid(), "é", "back", Instant);
        Assert.True(card.Update("e\u0301", "back", Instant.AddMinutes(1)));
        var state = State(card);
        Assert.Throws<ArgumentException>(() => card.Update("candidate", " ", Instant.AddMinutes(2)));
        Assert.Equal(state, State(card));
        Assert.Throws<ArgumentException>(() => card.Update(new('f', 2001), "candidate", Instant.AddMinutes(2)));
        Assert.Equal(state, State(card));
    }

    [Fact]
    public void Revision_overflow_is_checked_before_any_mutation_and_max_revision_noop_is_still_a_noop()
    {
        var card = Flashcard.Create(Guid.NewGuid(), "front", "back", Instant);
        typeof(Flashcard).GetProperty(nameof(Flashcard.ContentRevision))!.SetValue(card, int.MaxValue);
        var state = State(card);
        Assert.False(card.Update("front", "back", Instant.AddDays(1)));
        Assert.Throws<OverflowException>(() => card.Update("changed", "changed", Instant.AddDays(1)));
        Assert.Equal(state, State(card));
    }

    private static object State(Flashcard card) => (card.Front, card.Back, card.ContentRevision, card.CreatedAtUtc, card.UpdatedAtUtc);
}

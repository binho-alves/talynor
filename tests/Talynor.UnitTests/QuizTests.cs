using Talynor.Domain.Quizzes;

namespace Talynor.UnitTests;

public class QuizTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(-3));
    private static QuizQuestionInput Question(string text = "question", int correct = 0, params string[] options) =>
        new(text, options.Length == 0 ? [" first\n", "second"] : options, correct);
    private static Quiz Create(string title = "title", params QuizQuestionInput[] questions) =>
        Quiz.Create(Guid.NewGuid(), title, questions.Length == 0 ? [Question()] : questions, Now);

    [Fact]
    public void Valid_definition_has_server_identity_utc_revision_positions_and_literal_text()
    {
        var q = Create(" \tTitle \n", Question("  e\u0301 <b>\r\n", 1, " A\n", "a", "é", "e\u0301"));
        Assert.NotEqual(Guid.Empty, q.Id); Assert.NotEqual(Guid.Empty, q.KnowledgeNodeId);
        Assert.Equal("Title", q.Title); Assert.Equal(1, q.ContentRevision);
        Assert.Equal(Now.ToUniversalTime(), q.CreatedAtUtc); Assert.Equal(q.CreatedAtUtc, q.UpdatedAtUtc);
        Assert.Equal(TimeSpan.Zero, q.CreatedAtUtc.Offset);
        var question = Assert.Single(q.Questions); Assert.Equal(q.Id, question.QuizId);
        Assert.Equal("  e\u0301 <b>\r\n", question.Text); Assert.Equal(1, question.Position);
        Assert.Equal(new[] { 1, 2, 3, 4 }, question.Options.Select(o => o.Position));
        Assert.All(question.Options, o => { Assert.Equal(question.Id, o.QuestionId); Assert.NotEqual(Guid.Empty, o.Id); });
        Assert.Equal(" A\n", question.Options[0].Text); Assert.True(question.Options[1].IsCorrect);
        Assert.Single(question.Options, o => o.IsCorrect);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" \t\r\n")] [InlineData("\u0085\u2000")]
    public void Blank_titles_questions_and_options_are_rejected(string? value)
    {
        Assert.Throws<ArgumentException>(() => Create(value!));
        Assert.Throws<ArgumentException>(() => Create("title", Question(value!)));
        Assert.Throws<ArgumentException>(() => Create("title", Question(options: [value!, "other"])));
    }

    [Theory]
    [InlineData(200, 1000, 500, true)] [InlineData(201, 1000, 500, false)]
    [InlineData(200, 1001, 500, false)] [InlineData(200, 1000, 501, false)]
    public void Utf16_boundaries_are_enforced(int title, int question, int option, bool valid)
    {
        var action = () => Create(new string('t', title), Question(new string('q', question), 0, new string('a', option), "b"));
        if (valid) Assert.NotNull(action()); else Assert.Throws<ArgumentException>(action);
    }

    [Theory]
    [InlineData(100, 500, 250, true)] [InlineData(101, 500, 250, false)]
    [InlineData(100, 501, 250, false)] [InlineData(100, 500, 251, false)]
    public void Surrogate_pairs_count_as_two_utf16_units(int title, int question, int option, bool valid)
    {
        static string Pairs(int count) => string.Concat(Enumerable.Repeat("😀", count));
        var action = () => Create(Pairs(title), Question(Pairs(question), 0, Pairs(option), "other"));
        if (valid) Assert.NotNull(action()); else Assert.Throws<ArgumentException>(action);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, true)] [InlineData(10, true)] [InlineData(11, false)]
    public void Question_cardinality_is_bounded(int count, bool valid)
    {
        var action = () => Quiz.Create(Guid.NewGuid(), "t", Enumerable.Range(0, count).Select(i => Question("q" + i)).ToArray(), Now);
        if (valid) Assert.Equal(count, action().Questions.Count); else Assert.Throws<ArgumentException>(action);
    }

    [Theory]
    [InlineData(1, false)] [InlineData(2, true)] [InlineData(4, true)] [InlineData(5, false)]
    public void Options_cardinality_is_bounded(int count, bool valid)
    {
        var action = () => Create("t", new QuizQuestionInput("q", Enumerable.Range(0, count).Select(i => "o" + i).ToArray(), 0));
        if (valid) Assert.Single(action().Questions[0].Options, o => o.IsCorrect); else Assert.Throws<ArgumentException>(action);
    }

    [Theory]
    [InlineData(-1)] [InlineData(2)] [InlineData(int.MaxValue)]
    public void Exactly_one_in_range_correct_choice_is_required(int index) =>
        Assert.Throws<ArgumentException>(() => Create("t", Question(correct: index)));

    [Theory]
    [InlineData("same", "same")] [InlineData(" same\n", "same\t")] [InlineData("\u2000same", "same\u0085")]
    public void Ordinal_outer_trimmed_duplicate_options_are_rejected(string first, string second) =>
        Assert.Throws<ArgumentException>(() => Create("t", Question(options: [first, second])));

    [Fact]
    public void Null_definition_children_options_and_empty_node_are_invalid()
    {
        Assert.Throws<ArgumentException>(() => Quiz.Create(Guid.Empty, "t", [Question()], Now));
        Assert.Throws<ArgumentException>(() => Quiz.Create(Guid.NewGuid(), "t", null!, Now));
        Assert.Throws<ArgumentException>(() => Create("t", [null!]));
        Assert.Throws<ArgumentException>(() => Create("t", new QuizQuestionInput("q", null!, 0)));
        Assert.Equal(200, Create(" " + new string('t', 200) + " ").Title.Length);
    }

    [Theory]
    [InlineData("title")] [InlineData("question")] [InlineData("option")] [InlineData("choice")]
    [InlineData("question-order")] [InlineData("option-order")] [InlineData("literal-space")]
    public void Every_material_change_increments_once_and_regenerates_children(string kind)
    {
        QuizQuestionInput[] original = [Question("q1"), Question("q2")];
        var q = Create("title", original); var oldIds = q.Questions.Select(x => x.Id).ToArray();
        var definition = original.ToArray(); var title = "title";
        switch (kind)
        {
            case "title": title = "new"; break;
            case "question": definition[0] = Question("changed"); break;
            case "option": definition[0] = Question("q1", 0, "changed", "second"); break;
            case "choice": definition[0] = Question("q1", 1); break;
            case "question-order": Array.Reverse(definition); break;
            case "option-order": definition[0] = Question("q1", 1, "second", " first\n"); break;
            case "literal-space": definition[0] = Question("q1 "); break;
        }
        Assert.True(q.Replace(title, definition, Now.AddMinutes(1))); Assert.Equal(2, q.ContentRevision);
        Assert.Equal(Now.ToUniversalTime(), q.CreatedAtUtc); Assert.Equal(Now.AddMinutes(1).ToUniversalTime(), q.UpdatedAtUtc);
        Assert.DoesNotContain(q.Questions[0].Id, oldIds); Assert.Equal(new[] { 1, 2 }, q.Questions.Select(x => x.Position));
    }

    [Fact]
    public void Noop_invalid_candidate_and_overflow_leave_all_fields_and_children_untouched()
    {
        var q = Create(); var child = q.Questions[0]; var options = child.Options.ToArray();
        Assert.False(q.Replace(" \ttitle\n", [Question()], Now.AddDays(1)));
        Assert.Throws<ArgumentException>(() => q.Replace("candidate", [Question("valid"), Question(options: ["same", "same"])], Now.AddDays(1)));
        typeof(Quiz).GetProperty(nameof(Quiz.ContentRevision))!.SetValue(q, int.MaxValue);
        Assert.Throws<OverflowException>(() => q.Replace("candidate", [Question()], Now.AddDays(1)));
        Assert.Equal("title", q.Title); Assert.Equal(int.MaxValue, q.ContentRevision); Assert.Equal(q.CreatedAtUtc, q.UpdatedAtUtc);
        Assert.Same(child, Assert.Single(q.Questions)); Assert.Equal(options, child.Options);
    }
}

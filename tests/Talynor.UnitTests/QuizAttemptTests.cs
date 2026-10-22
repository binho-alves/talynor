using Talynor.Application.Quizzes;
using Talynor.Domain.Quizzes;

namespace Talynor.UnitTests;

public class QuizAttemptTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(-3));
    private static Quiz Definition() => Quiz.Create(Guid.NewGuid(), " historical title ",
        Enumerable.Range(0, 3).Select(i => new QuizQuestionInput($" question {i}\n", [" a ", "b", "c"], i)).ToArray(), Now);
    private static QuizAnswerInput[] Select(Quiz quiz, int correct = 3) => quiz.Questions.Select((q, i) =>
        new QuizAnswerInput(q.Id, q.Options.Single(o => o.IsCorrect == (i < correct) && (i < correct || o.Position == (i + 1) % 3 + 1)).Id)).ToArray();

    [Theory]
    [InlineData(0, 0)] [InlineData(1, 33.33)] [InlineData(2, 66.67)] [InlineData(3, 100)]
    public void Grading_counts_and_rounds_decimal_without_persisting_derived_values(int correct, double percent)
    {
        var quiz = Definition(); var id = Guid.NewGuid(); var attempt = QuizAttempt.Grade(id, quiz, 1, Select(quiz, correct), Now);
        Assert.Equal(id, attempt.AttemptId); Assert.Equal(quiz.Id, attempt.QuizId); Assert.Equal("historical title", attempt.QuizTitle);
        Assert.Equal(1, attempt.ContentRevision); Assert.Equal(3, attempt.QuestionCount); Assert.Equal(correct, attempt.CorrectCount);
        Assert.Equal((decimal)percent, attempt.AccuracyPercentage); Assert.Equal(TimeSpan.Zero, attempt.SubmittedAtUtc.Offset);
        Assert.Equal(Now.ToUniversalTime(), attempt.SubmittedAtUtc); Assert.Equal(correct, attempt.Answers.Count(a => a.IsCorrect));
    }

    [Theory]
    [InlineData("empty-id")] [InlineData("zero-revision")] [InlineData("negative-revision")] [InlineData("stale")]
    [InlineData("null")] [InlineData("empty")] [InlineData("null-answer")] [InlineData("missing")]
    [InlineData("extra")] [InlineData("duplicate")] [InlineData("empty-question")] [InlineData("empty-option")]
    [InlineData("foreign-question")] [InlineData("foreign-option")] [InlineData("other-question-option")]
    public void Invalid_complete_answer_sheet_is_rejected_without_touching_definition(string kind)
    {
        var quiz = Definition(); var id = Guid.NewGuid(); var revision = 1; var answers = Select(quiz);
        switch (kind)
        {
            case "empty-id": id = Guid.Empty; break;
            case "zero-revision": revision = 0; break;
            case "negative-revision": revision = -1; break;
            case "stale": revision = 2; break;
            case "null": answers = null!; break;
            case "empty": answers = []; break;
            case "null-answer": answers[0] = null!; break;
            case "missing": answers = answers[..2]; break;
            case "extra": answers = [..answers, new(Guid.NewGuid(), Guid.NewGuid())]; break;
            case "duplicate": answers[1] = answers[0]; break;
            case "empty-question": answers[0] = answers[0] with { QuestionId = Guid.Empty }; break;
            case "empty-option": answers[0] = answers[0] with { SelectedOptionId = Guid.Empty }; break;
            case "foreign-question": answers[0] = answers[0] with { QuestionId = Guid.NewGuid() }; break;
            case "foreign-option": answers[0] = answers[0] with { SelectedOptionId = Guid.NewGuid() }; break;
            case "other-question-option": answers[0] = answers[0] with { SelectedOptionId = quiz.Questions[1].Options[0].Id }; break;
        }
        Assert.Throws<ArgumentException>(() => QuizAttempt.Grade(id, quiz, revision, answers, Now));
        Assert.Equal(1, quiz.ContentRevision); Assert.Equal(3, quiz.Questions.Count);
    }

    [Fact]
    public void Snapshot_copies_every_literal_option_and_survives_full_child_replacement()
    {
        var quiz = Definition(); var answers = Select(quiz, 2); var attempt = QuizAttempt.Grade(Guid.NewGuid(), quiz, 1, answers, Now);
        var before = QuizAttemptDetail.From(attempt);
        foreach (var pair in quiz.Questions.Zip(attempt.Answers))
        {
            Assert.Equal(pair.First.Id, pair.Second.QuestionId); Assert.Equal(pair.First.Text, pair.Second.Text);
            Assert.Equal(pair.First.Position, pair.Second.Position);
            Assert.Equal(pair.First.Options.Select(o => (o.Id, o.Position, o.Text)), pair.Second.Options.Select(o => (o.OptionId, o.Position, o.Text)));
            Assert.Equal(pair.First.Options.Single(o => o.IsCorrect).Id, pair.Second.CorrectOptionId);
        }
        Assert.True(quiz.Replace("changed", [new("new Q", ["new A", "new B"], 1)], Now.AddDays(1)));
        Assert.Equal("historical title", attempt.QuizTitle); Assert.Equal(1, attempt.ContentRevision);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(QuizAttemptDetail.From(attempt)));
        Assert.Equal(before.Answers.Select(a => a.QuestionId), attempt.Answers.Select(a => a.QuestionId));
        Assert.Equal(9, attempt.Answers.Sum(a => a.Options.Count));
        Assert.All(typeof(QuizAttempt).GetProperties().Where(p => p.SetMethod is not null), p => Assert.True(p.SetMethod!.IsPrivate));
        Assert.Throws<NotSupportedException>(() => ((IList<QuizAttemptAnswer>)attempt.Answers).Clear());
    }

    [Fact]
    public void Replay_equality_is_quiz_revision_and_complete_set_independent_of_json_order()
    {
        var quiz = Definition(); var answers = Select(quiz); var attempt = QuizAttempt.Grade(Guid.NewGuid(), quiz, 1, answers, Now);
        Assert.True(attempt.Matches(quiz.Id, 1, answers.Reverse().ToArray()));
        Assert.False(attempt.Matches(Guid.NewGuid(), 1, answers)); Assert.False(attempt.Matches(quiz.Id, 2, answers));
        Assert.False(attempt.Matches(quiz.Id, 1, answers[..2])); Assert.False(attempt.Matches(quiz.Id, 1, [answers[0], answers[0], answers[2]]));
        Assert.False(attempt.Matches(quiz.Id, 1, [answers[0] with { SelectedOptionId = quiz.Questions[0].Options[1].Id }, ..answers[1..]]));
    }
}

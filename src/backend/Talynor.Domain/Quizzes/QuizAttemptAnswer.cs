namespace Talynor.Domain.Quizzes;

public sealed class QuizAttemptAnswer
{
    private QuizAttemptAnswer() { }
    private readonly List<QuizAttemptAnswerOption> options = [];
    public Guid AttemptId { get; private set; }
    public Guid QuestionId { get; private set; }
    public int Position { get; private set; }
    public string Text { get; private set; } = null!;
    public Guid SelectedOptionId { get; private set; }
    public Guid CorrectOptionId { get; private set; }
    public bool IsCorrect => SelectedOptionId == CorrectOptionId;
    public IReadOnlyList<QuizAttemptAnswerOption> Options => options.AsReadOnly();

    internal static QuizAttemptAnswer Snapshot(Guid attemptId, QuizQuestion question, Guid selectedOptionId)
    {
        var answer = new QuizAttemptAnswer { AttemptId = attemptId, QuestionId = question.Id, Position = question.Position,
            Text = question.Text, SelectedOptionId = selectedOptionId, CorrectOptionId = question.Options.Single(o => o.IsCorrect).Id };
        answer.options.AddRange(question.Options.OrderBy(o => o.Position).Select(o => QuizAttemptAnswerOption.Snapshot(attemptId, question.Id, o)));
        return answer;
    }
}

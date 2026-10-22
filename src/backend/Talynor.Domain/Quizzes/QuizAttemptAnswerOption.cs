namespace Talynor.Domain.Quizzes;

public sealed class QuizAttemptAnswerOption
{
    private QuizAttemptAnswerOption() { }
    public Guid AttemptId { get; private set; }
    public Guid QuestionId { get; private set; }
    public Guid OptionId { get; private set; }
    public int Position { get; private set; }
    public string Text { get; private set; } = null!;

    internal static QuizAttemptAnswerOption Snapshot(Guid attemptId, Guid questionId, QuizOption option) =>
        new() { AttemptId = attemptId, QuestionId = questionId, OptionId = option.Id, Position = option.Position, Text = option.Text };
}

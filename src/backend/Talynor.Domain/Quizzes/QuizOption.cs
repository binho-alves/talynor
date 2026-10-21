namespace Talynor.Domain.Quizzes;

public sealed class QuizOption
{
    private QuizOption() { }
    public Guid Id { get; private set; }
    public Guid QuestionId { get; private set; }
    public string Text { get; private set; } = null!;
    public int Position { get; private set; }
    public bool IsCorrect { get; private set; }

    internal static QuizOption Create(Guid questionId, string text, int position, bool isCorrect)
    {
        // String.Length counts UTF-16 units; only duplicate comparison trims the text.
        if (string.IsNullOrWhiteSpace(text) || text.Length > 500 || position is < 1 or > 4)
            throw new ArgumentException("A bounded nonblank option is required.", nameof(text));
        return new() { Id = Guid.NewGuid(), QuestionId = questionId, Text = text, Position = position, IsCorrect = isCorrect };
    }
}

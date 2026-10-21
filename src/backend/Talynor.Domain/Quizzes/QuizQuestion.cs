namespace Talynor.Domain.Quizzes;

public sealed class QuizQuestion
{
    private QuizQuestion() { }
    private readonly List<QuizOption> options = [];
    public Guid Id { get; private set; }
    public Guid QuizId { get; private set; }
    public string Text { get; private set; } = null!;
    public int Position { get; private set; }
    public IReadOnlyList<QuizOption> Options => options.AsReadOnly();

    internal static QuizQuestion Create(Guid quizId, QuizQuestionInput input, int position)
    {
        if (input is null || string.IsNullOrWhiteSpace(input.Text) || input.Text.Length > 1000 || position is < 1 or > 10)
            throw new ArgumentException("A bounded nonblank question is required.", nameof(input));
        if (input.Options is null || input.Options.Count is < 2 or > 4 || input.CorrectOptionIndex < 0 ||
            input.CorrectOptionIndex >= input.Options.Count)
            throw new ArgumentException("Two to four options and one correct index are required.", nameof(input));
        var question = new QuizQuestion { Id = Guid.NewGuid(), QuizId = quizId, Text = input.Text, Position = position };
        var normalized = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < input.Options.Count; i++)
        {
            var option = QuizOption.Create(question.Id, input.Options[i], i + 1, i == input.CorrectOptionIndex);
            if (!normalized.Add(option.Text.Trim())) throw new ArgumentException("Duplicate options are invalid.", nameof(input));
            question.options.Add(option);
        }
        return question;
    }
}

namespace Talynor.Domain.Quizzes;

public sealed record QuizQuestionInput(string Text, IReadOnlyList<string> Options, int CorrectOptionIndex);

public sealed class Quiz
{
    private Quiz() { }
    private readonly List<QuizQuestion> questions = [];
    public Guid Id { get; private set; }
    public Guid KnowledgeNodeId { get; private set; }
    public string Title { get; private set; } = null!;
    public int ContentRevision { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public IReadOnlyList<QuizQuestion> Questions => questions.AsReadOnly();

    public static Quiz Create(Guid nodeId, string title, IReadOnlyList<QuizQuestionInput> definition, DateTimeOffset now)
    {
        if (nodeId == Guid.Empty) throw new ArgumentException("A node ID is required.", nameof(nodeId));
        var id = Guid.NewGuid();
        var candidate = ValidateAndBuild(id, title, definition);
        var quiz = new Quiz { Id = id, KnowledgeNodeId = nodeId, Title = candidate.Title, ContentRevision = 1,
            CreatedAtUtc = now.ToUniversalTime(), UpdatedAtUtc = now.ToUniversalTime() };
        quiz.questions.AddRange(candidate.Questions);
        return quiz;
    }

    public bool Replace(string title, IReadOnlyList<QuizQuestionInput> definition, DateTimeOffset now)
    {
        // Validate the complete candidate, including every child, before touching the aggregate.
        var candidate = ValidateAndBuild(Id, title, definition);
        if (Title == candidate.Title && questions.OrderBy(q => q.Position).SequenceEqual(candidate.Questions, MaterialComparer.Instance))
            return false;
        var revision = checked(ContentRevision + 1);
        Title = candidate.Title;
        questions.Clear();
        questions.AddRange(candidate.Questions);
        ContentRevision = revision;
        UpdatedAtUtc = now.ToUniversalTime();
        return true;
    }

    private static (string Title, QuizQuestion[] Questions) ValidateAndBuild(Guid quizId, string title,
        IReadOnlyList<QuizQuestionInput> definition)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
            throw new ArgumentException("A bounded nonblank title is required.", nameof(title));
        if (definition is null || definition.Count is < 1 or > 10)
            throw new ArgumentException("Between one and ten questions are required.", nameof(definition));
        return (title.Trim(), definition.Select((input, index) => QuizQuestion.Create(quizId, input, index + 1)).ToArray());
    }

    private sealed class MaterialComparer : IEqualityComparer<QuizQuestion>
    {
        public static readonly MaterialComparer Instance = new();
        public bool Equals(QuizQuestion? left, QuizQuestion? right) => left is not null && right is not null &&
            string.Equals(left.Text, right.Text, StringComparison.Ordinal) &&
            left.Options.OrderBy(o => o.Position).Select(o => (o.Text, o.IsCorrect))
                .SequenceEqual(right.Options.OrderBy(o => o.Position).Select(o => (o.Text, o.IsCorrect)));
        public int GetHashCode(QuizQuestion value) => StringComparer.Ordinal.GetHashCode(value.Text);
    }
}

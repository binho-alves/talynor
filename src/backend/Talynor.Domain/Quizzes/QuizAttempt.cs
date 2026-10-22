namespace Talynor.Domain.Quizzes;

public sealed record QuizAnswerInput(Guid QuestionId, Guid SelectedOptionId);

public sealed class QuizAttempt
{
    private QuizAttempt() { }
    private readonly List<QuizAttemptAnswer> answers = [];
    public Guid AttemptId { get; private set; }
    public Guid QuizId { get; private set; }
    public string QuizTitle { get; private set; } = null!;
    public int ContentRevision { get; private set; }
    public DateTimeOffset SubmittedAtUtc { get; private set; }
    public int QuestionCount { get; private set; }
    public int CorrectCount { get; private set; }
    public decimal AccuracyPercentage => Accuracy(CorrectCount, QuestionCount);
    public IReadOnlyList<QuizAttemptAnswer> Answers => answers.AsReadOnly();

    public static bool ValidSubmission(Guid attemptId, int revision, IReadOnlyList<QuizAnswerInput>? selections) =>
        attemptId != Guid.Empty && revision > 0 && selections is { Count: >= 1 and <= 10 } &&
        selections.All(a => a is not null && a.QuestionId != Guid.Empty && a.SelectedOptionId != Guid.Empty) &&
        selections.Select(a => a.QuestionId).Distinct().Count() == selections.Count;

    public static QuizAttempt Grade(Guid attemptId, Quiz quiz, int revision,
        IReadOnlyList<QuizAnswerInput> selections, DateTimeOffset submittedAtUtc)
    {
        if (quiz is null || !ValidSubmission(attemptId, revision, selections) || revision != quiz.ContentRevision ||
            selections.Count != quiz.Questions.Count)
            throw new ArgumentException("A complete valid submission for the current definition is required.");
        var selected = selections.ToDictionary(a => a.QuestionId, a => a.SelectedOptionId);
        var attempt = new QuizAttempt { AttemptId = attemptId, QuizId = quiz.Id, QuizTitle = quiz.Title,
            ContentRevision = revision, SubmittedAtUtc = submittedAtUtc.ToUniversalTime(), QuestionCount = quiz.Questions.Count };
        foreach (var question in quiz.Questions.OrderBy(q => q.Position))
        {
            if (!selected.TryGetValue(question.Id, out var option) || !question.Options.Any(o => o.Id == option))
                throw new ArgumentException("Every question requires one of its presented options.");
            attempt.answers.Add(QuizAttemptAnswer.Snapshot(attemptId, question, option));
        }
        attempt.CorrectCount = attempt.answers.Count(a => a.IsCorrect);
        return attempt;
    }

    public bool Matches(Guid quizId, int revision, IReadOnlyList<QuizAnswerInput> selections) =>
        ValidSubmission(AttemptId, revision, selections) && QuizId == quizId && ContentRevision == revision &&
        selections.Count == answers.Count && selections.All(s =>
            answers.Any(a => a.QuestionId == s.QuestionId && a.SelectedOptionId == s.SelectedOptionId));

    public static decimal Accuracy(int correctCount, int questionCount) =>
        Math.Round(100m * correctCount / questionCount, 2, MidpointRounding.AwayFromZero);
}

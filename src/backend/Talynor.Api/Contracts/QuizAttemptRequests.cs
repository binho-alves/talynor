using System.Text.Json.Serialization;
using Talynor.Domain.Quizzes;

namespace Talynor.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SubmitQuizAttemptRequest
{
    [JsonRequired, JsonNumberHandling(JsonNumberHandling.Strict)] public int? ContentRevision { get; init; }
    [JsonRequired] public QuizAnswerRequest[] Answers { get; init; } = null!;
    public QuizAnswerInput[] ToAnswers() => Answers?.Select(a => a is null ? null! : new QuizAnswerInput(a.QuestionId, a.SelectedOptionId)).ToArray()!;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class QuizAnswerRequest
{
    [JsonRequired] public Guid QuestionId { get; init; }
    [JsonRequired] public Guid SelectedOptionId { get; init; }
}

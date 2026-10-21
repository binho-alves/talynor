using System.Text.Json.Serialization;
using Talynor.Application.Quizzes;
using Talynor.Domain.Quizzes;

namespace Talynor.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateQuizRequest
{
    [JsonRequired] public string Title { get; init; } = null!;
    [JsonRequired] public QuizQuestionRequest[] Questions { get; init; } = null!;
    public QuizInput ToInput() => new(Title, Questions?.Select(q => q?.ToInput()!).ToArray()!);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReplaceQuizRequest
{
    [JsonRequired, JsonNumberHandling(JsonNumberHandling.Strict)] public int? ExpectedRevision { get; init; }
    [JsonRequired] public string Title { get; init; } = null!;
    [JsonRequired] public QuizQuestionRequest[] Questions { get; init; } = null!;
    public QuizInput ToInput() => new(Title, Questions?.Select(q => q?.ToInput()!).ToArray()!);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class QuizQuestionRequest
{
    [JsonRequired] public string Text { get; init; } = null!;
    [JsonRequired] public string[] Options { get; init; } = null!;
    [JsonRequired, JsonNumberHandling(JsonNumberHandling.Strict)] public int? CorrectOptionIndex { get; init; }
    public QuizQuestionInput ToInput() => new(Text, Options, CorrectOptionIndex ?? -1);
}

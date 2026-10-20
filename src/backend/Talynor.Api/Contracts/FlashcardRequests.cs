using System.Text.Json.Serialization;

namespace Talynor.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateFlashcardRequest
{
    [JsonRequired] public string Front { get; init; } = null!;
    [JsonRequired] public string Back { get; init; } = null!;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateFlashcardRequest
{
    private string? front;
    private string? back;
    public string? Front { get => front; init { front = value; FrontSpecified = true; } }
    public string? Back { get => back; init { back = value; BackSpecified = true; } }
    internal bool FrontSpecified { get; private set; }
    internal bool BackSpecified { get; private set; }
}

using System.Text.Json.Serialization;

namespace Talynor.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateKnowledgeContentRequest
{
    [JsonRequired] public string Type { get; init; } = null!;
    public string? Title { get; init; }
    private string? body;
    private string? url;
    public string? Body { get => body; init { body = value; BodySpecified = true; } }
    public string? Url { get => url; init { url = value; UrlSpecified = true; } }
    internal bool BodySpecified { get; private set; }
    internal bool UrlSpecified { get; private set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateKnowledgeContentRequest
{
    public string? Title { get; init; }
    public bool ClearTitle { get; init; }
    private string? body;
    private string? url;
    public string? Body { get => body; init { body = value; BodySpecified = true; } }
    public string? Url { get => url; init { url = value; UrlSpecified = true; } }
    internal bool BodySpecified { get; private set; }
    internal bool UrlSpecified { get; private set; }
}

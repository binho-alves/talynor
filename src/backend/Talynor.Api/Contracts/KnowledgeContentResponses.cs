using System.Text.Json.Serialization;
using Talynor.Application.KnowledgeContents;
using Talynor.Domain.KnowledgeContents;

namespace Talynor.Api.Contracts;

public sealed record KnowledgeContentListResponse(IReadOnlyList<KnowledgeContentSummaryResponse> Contents);
public sealed record KnowledgeContentSummaryResponse(Guid Id, string Type, string? Title,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url);

internal static class KnowledgeContentResponseMapping
{
    public static KnowledgeContentDetailResponse ToResponse(KnowledgeContentDetail content) => new(content.Id,
        TypeName(content.Type), content.Title, content.CreatedAtUtc, content.UpdatedAtUtc, content.Body, content.Url);
    public static string TypeName(KnowledgeContentType type) => type switch
    {
        KnowledgeContentType.Note => "note", KnowledgeContentType.Link => "link",
        _ => throw new InvalidOperationException("Unknown knowledge content type.")
    };
}
public sealed record KnowledgeContentDetailResponse(Guid Id, string Type, string? Title,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Body,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url);

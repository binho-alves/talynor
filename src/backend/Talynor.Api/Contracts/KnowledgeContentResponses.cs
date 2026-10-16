using System.Text.Json.Serialization;

namespace Talynor.Api.Contracts;

public sealed record KnowledgeContentListResponse(IReadOnlyList<KnowledgeContentSummaryResponse> Contents);
public sealed record KnowledgeContentSummaryResponse(Guid Id, string Type, string? Title,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url);
public sealed record KnowledgeContentDetailResponse(Guid Id, string Type, string? Title,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Body,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url);

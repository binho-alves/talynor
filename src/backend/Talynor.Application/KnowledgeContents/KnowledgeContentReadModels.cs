using Talynor.Domain.KnowledgeContents;

namespace Talynor.Application.KnowledgeContents;

public sealed record KnowledgeContentSummary(Guid Id, KnowledgeContentType Type, string? Title,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, string? Url);
public sealed record KnowledgeContentDetail(Guid Id, KnowledgeContentType Type, string? Title,
    string? Body, string? Url, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public enum KnowledgeContentReadStatus { Success, AuthenticationRequired, NotFound }
public sealed record KnowledgeContentListResult(KnowledgeContentReadStatus Status,
    IReadOnlyList<KnowledgeContentSummary>? Contents = null);
public sealed record KnowledgeContentDetailResult(KnowledgeContentReadStatus Status, KnowledgeContentDetail? Content = null);

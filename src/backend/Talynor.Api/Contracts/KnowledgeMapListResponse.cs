namespace Talynor.Api.Contracts;

public sealed record KnowledgeMapListResponse(IReadOnlyList<KnowledgeMapSummaryResponse> Maps);
public sealed record KnowledgeMapSummaryResponse(Guid Id, string Title);

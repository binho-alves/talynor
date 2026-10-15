namespace Talynor.Application.KnowledgeMaps;

public sealed record KnowledgeMapSummary(Guid Id, string Title);
public enum KnowledgeMapListStatus { Success, AuthenticationRequired }
public sealed record KnowledgeMapListResult(KnowledgeMapListStatus Status, IReadOnlyList<KnowledgeMapSummary>? Maps = null);

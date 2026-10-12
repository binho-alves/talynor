namespace Talynor.Application.KnowledgeMaps;

// Temporary read projections, not domain entities or persistence models.
public sealed record KnowledgeMapReadModel(
    string Id, string Title, string Description,
    IReadOnlyList<KnowledgeNodeReadModel> Nodes,
    IReadOnlyList<KnowledgeEdgeReadModel> Edges);

public sealed record KnowledgeNodeReadModel(
    string Id, string Title, string Description,
    int Progress, int StudyMinutes, string? ChildMapId);

public sealed record KnowledgeEdgeReadModel(string Id, string SourceNodeId, string TargetNodeId);

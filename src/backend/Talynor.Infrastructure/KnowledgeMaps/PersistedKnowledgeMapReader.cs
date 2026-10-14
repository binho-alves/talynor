using Microsoft.EntityFrameworkCore;
using Talynor.Application.KnowledgeMaps;
using Talynor.Infrastructure.Persistence;

namespace Talynor.Infrastructure.KnowledgeMaps;

public sealed class PersistedKnowledgeMapReader(TalynorDbContext context) : IPersistedKnowledgeMapReader
{
    public async Task<PersistedKnowledgeMap?> GetAsync(Guid mapId, Guid workspaceId, CancellationToken cancellationToken)
    {
        // One read snapshot for map, nodes and edges; no mix of separately committed graph states.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var map = await context.KnowledgeMaps.AsNoTracking()
            .SingleOrDefaultAsync(map => map.Id == mapId && map.WorkspaceId == workspaceId, cancellationToken);
        if (map is null) return null;
        var nodes = await context.KnowledgeNodes.AsNoTracking().Where(node => node.KnowledgeMapId == map.Id)
            .OrderBy(node => node.Id).Select(node => new PersistedKnowledgeNode(node.Id, node.Title,
                node.Description, node.PositionX, node.PositionY, node.ChildMapId)).ToArrayAsync(cancellationToken);
        var edges = await context.KnowledgeEdges.AsNoTracking().Where(edge => edge.KnowledgeMapId == map.Id)
            .OrderBy(edge => edge.Id).Select(edge => new PersistedKnowledgeEdge(
                edge.Id, edge.SourceNodeId, edge.TargetNodeId)).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(map.Id, map.Title, map.Description, Array.AsReadOnly(nodes), Array.AsReadOnly(edges));
    }
}

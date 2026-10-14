using Talynor.Domain.KnowledgeMaps;
using Talynor.Domain.Workspaces;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

// Explicit test-only data. Runtime/startup has no map seed or usable default account.
internal sealed record KnowledgeGraphFixture(PersonalWorkspace Workspace, PersonalWorkspace OtherWorkspace,
    KnowledgeMap Map, KnowledgeMap Child, KnowledgeMap OtherMap, KnowledgeNode First, KnowledgeNode Second,
    KnowledgeNode ChildNode, KnowledgeEdge Edge)
{
    public static async Task<KnowledgeGraphFixture> CreateAsync(TalynorDbContext db,
        PersonalWorkspace? workspace = null, PersonalWorkspace? otherWorkspace = null)
    {
        var clock = TimeProvider.System;
        workspace ??= PersonalWorkspace.Create("fixture-owner-a", clock.GetUtcNow());
        otherWorkspace ??= PersonalWorkspace.Create("fixture-owner-b", clock.GetUtcNow());
        if (db.Entry(workspace).State == Microsoft.EntityFrameworkCore.EntityState.Detached)
            db.PersonalWorkspaces.Add(workspace);
        if (db.Entry(otherWorkspace).State == Microsoft.EntityFrameworkCore.EntityState.Detached)
            db.PersonalWorkspaces.Add(otherWorkspace);
        var map = KnowledgeMap.Create(workspace.Id, "Software", "Persistent fixture", clock);
        var child = KnowledgeMap.Create(workspace.Id, "C#", null, clock);
        var other = KnowledgeMap.Create(otherWorkspace.Id, "Private B", null, clock);
        var first = KnowledgeNode.Create(map.Id, "C#", null, 125.5, -32.25, child.Id, clock);
        var second = KnowledgeNode.Create(map.Id, "Architecture", "Design", -8, 50, null, clock);
        var childNode = KnowledgeNode.Create(child.Id, "LINQ", null, 0, 0, null, clock);
        var edge = KnowledgeEdge.Create(map.Id, first.Id, second.Id, clock);
        db.KnowledgeMaps.AddRange(map, child, other);
        db.KnowledgeNodes.AddRange(first, second, childNode);
        db.KnowledgeEdges.Add(edge);
        await db.SaveChangesAsync();
        return new(workspace, otherWorkspace, map, child, other, first, second, childNode, edge);
    }
}

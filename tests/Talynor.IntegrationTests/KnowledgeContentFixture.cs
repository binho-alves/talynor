using Talynor.Domain.KnowledgeContents;
using Talynor.Domain.KnowledgeMaps;
using Talynor.Domain.Workspaces;
using Talynor.Infrastructure.Persistence;

namespace Talynor.IntegrationTests;

internal sealed record KnowledgeContentFixture(KnowledgeGraphFixture Graph, KnowledgeContent Early,
    KnowledgeContent Note, KnowledgeContent Link, KnowledgeContent ChildContent, KnowledgeContent ForeignContent, KnowledgeNode ForeignNode)
{
    public static readonly DateTimeOffset Instant = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567);
    public static async Task<KnowledgeContentFixture> CreateAsync(TalynorDbContext db,
        PersonalWorkspace? workspace = null, PersonalWorkspace? other = null)
    {
        var graph = await KnowledgeGraphFixture.CreateAsync(db, workspace, other);
        var foreignNode = KnowledgeNode.Create(graph.OtherMap.Id, "Foreign concept", null, 0, 0, null, Instant);
        db.KnowledgeNodes.Add(foreignNode);
        var early = KnowledgeContent.Create(graph.First.Id, KnowledgeContentType.Note, null, "Earlier note", null, Instant.AddMinutes(-1));
        var note = KnowledgeContent.Create(graph.First.Id, KnowledgeContentType.Note, " Note ", "<b>private literal</b>\n# not markdown", null, Instant.ToOffset(TimeSpan.FromHours(-3)));
        var link = KnowledgeContent.Create(graph.First.Id, KnowledgeContentType.Link, null, null, "http://intranet/docs?q=1#part", Instant.ToOffset(TimeSpan.FromHours(5)));
        // Deliberate equal-time high-before-low insertion, not random expected GUID order.
        db.Entry(note).Property(value => value.Id).CurrentValue = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        db.Entry(link).Property(value => value.Id).CurrentValue = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var child = KnowledgeContent.Create(graph.ChildNode.Id, KnowledgeContentType.Note, null, "Child content", null, Instant);
        var foreign = KnowledgeContent.Create(foreignNode.Id, KnowledgeContentType.Note, null, "Private B", null, Instant);
        db.KnowledgeContents.AddRange(note, link, early, child, foreign);
        await db.SaveChangesAsync();
        return new(graph, early, note, link, child, foreign, foreignNode);
    }
}

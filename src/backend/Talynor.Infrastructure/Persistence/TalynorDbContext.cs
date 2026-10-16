using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Talynor.Domain.Workspaces;
using Talynor.Domain.KnowledgeMaps;
using Talynor.Domain.KnowledgeContents;

namespace Talynor.Infrastructure.Persistence;

public sealed class TalynorDbContext(DbContextOptions<TalynorDbContext> options)
    : IdentityUserContext<IdentityUser>(options)
{
    public DbSet<PersonalWorkspace> PersonalWorkspaces => Set<PersonalWorkspace>();
    public DbSet<KnowledgeMap> KnowledgeMaps => Set<KnowledgeMap>();
    public DbSet<KnowledgeNode> KnowledgeNodes => Set<KnowledgeNode>();
    public DbSet<KnowledgeEdge> KnowledgeEdges => Set<KnowledgeEdge>();
    public DbSet<KnowledgeContent> KnowledgeContents => Set<KnowledgeContent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<KnowledgeContent>(content =>
        {
            content.ToTable("KnowledgeContents", table =>
            {
                table.HasCheckConstraint("CK_KnowledgeContents_Type", "Type IN ('note', 'link')");
                table.HasCheckConstraint("CK_KnowledgeContents_Payload",
                    "(Type = 'note' AND Body IS NOT NULL AND Url IS NULL) OR (Type = 'link' AND Url IS NOT NULL AND Body IS NULL)");
            });
            content.HasKey(value => value.Id);
            content.Property(value => value.Id).ValueGeneratedNever();
            content.HasOne<KnowledgeNode>().WithMany().HasForeignKey(value => value.KnowledgeNodeId)
                .OnDelete(DeleteBehavior.Cascade);
            content.Property(value => value.Type).IsRequired().HasConversion(
                value => ContentTypeToStorage(value), value => ContentTypeFromStorage(value));
            content.Property(value => value.Title).HasMaxLength(200);
            content.Property(value => value.Body).HasMaxLength(4000);
            content.Property(value => value.Url).HasMaxLength(2048);
            // New content timestamps only: order by UTC DateTime in SQLite, retain UTC DateTimeOffset in Domain/API.
            content.Property(value => value.CreatedAtUtc).HasConversion(
                value => value.UtcDateTime, value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            content.Property(value => value.UpdatedAtUtc).HasConversion(
                value => value.UtcDateTime, value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
            content.HasIndex(value => new { value.KnowledgeNodeId, value.CreatedAtUtc, value.Id });
        });
        builder.Entity<PersonalWorkspace>(workspace =>
        {
            workspace.ToTable("PersonalWorkspaces");
            workspace.HasKey(value => value.Id);
            workspace.Property(value => value.Id).ValueGeneratedNever();
            workspace.Property(value => value.OwnerUserId).IsRequired();
            workspace.HasIndex(value => value.OwnerUserId).IsUnique();
            workspace.Property(value => value.CreatedAtUtc).IsRequired();
            // Logical Identity reference only; account-deletion lifecycle remains outside this slice.
        });
        builder.Entity<KnowledgeMap>(map =>
        {
            map.ToTable("KnowledgeMaps");
            map.HasKey(value => value.Id);
            map.Property(value => value.Id).ValueGeneratedNever();
            map.Property(value => value.WorkspaceId).IsRequired();
            map.HasIndex(value => value.WorkspaceId);
            map.HasOne<PersonalWorkspace>().WithMany().HasForeignKey(value => value.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);
            map.Property(value => value.Title).IsRequired().HasMaxLength(200);
            map.Property(value => value.Description).HasMaxLength(4000);
            map.Property(value => value.CreatedAtUtc).IsRequired();
            map.Property(value => value.UpdatedAtUtc).IsRequired();
        });
        builder.Entity<KnowledgeNode>(node =>
        {
            node.ToTable("KnowledgeNodes", table =>
            {
                table.HasTrigger("TR_KnowledgeNodes_ChildWorkspace_Insert");
                table.HasTrigger("TR_KnowledgeNodes_ChildWorkspace_Update");
                table.HasCheckConstraint("CK_KnowledgeNodes_FinitePositions",
                    "abs(PositionX) <= 1.7976931348623157e308 AND abs(PositionY) <= 1.7976931348623157e308");
            });
            node.HasKey(value => value.Id);
            node.Property(value => value.Id).ValueGeneratedNever();
            // Composite key makes each edge endpoint's map membership a database constraint.
            node.HasAlternateKey(value => new { value.KnowledgeMapId, value.Id });
            node.HasOne<KnowledgeMap>().WithMany().HasForeignKey(value => value.KnowledgeMapId)
                .OnDelete(DeleteBehavior.Cascade);
            node.HasOne<KnowledgeMap>().WithMany().HasForeignKey(value => value.ChildMapId)
                .OnDelete(DeleteBehavior.Restrict);
            node.Property(value => value.Title).IsRequired().HasMaxLength(200);
            node.Property(value => value.Description).HasMaxLength(4000);
            node.Property(value => value.PositionX).IsRequired();
            node.Property(value => value.PositionY).IsRequired();
            node.Property(value => value.CreatedAtUtc).IsRequired();
            node.Property(value => value.UpdatedAtUtc).IsRequired();
        });
        builder.Entity<KnowledgeMap>().ToTable("KnowledgeMaps", table =>
            table.HasTrigger("TR_KnowledgeMaps_ChildWorkspace_Update"));
        builder.Entity<KnowledgeEdge>(edge =>
        {
            edge.ToTable("KnowledgeEdges");
            edge.HasKey(value => value.Id);
            edge.Property(value => value.Id).ValueGeneratedNever();
            edge.HasOne<KnowledgeMap>().WithMany().HasForeignKey(value => value.KnowledgeMapId)
                .OnDelete(DeleteBehavior.Cascade);
            edge.HasOne<KnowledgeNode>().WithMany()
                .HasForeignKey(value => new { value.KnowledgeMapId, value.SourceNodeId })
                .HasPrincipalKey(value => new { value.KnowledgeMapId, value.Id })
                .OnDelete(DeleteBehavior.NoAction);
            edge.HasOne<KnowledgeNode>().WithMany()
                .HasForeignKey(value => new { value.KnowledgeMapId, value.TargetNodeId })
                .HasPrincipalKey(value => new { value.KnowledgeMapId, value.Id })
                .OnDelete(DeleteBehavior.NoAction);
            edge.Property(value => value.CreatedAtUtc).IsRequired();
        });
    }

    private static string ContentTypeToStorage(KnowledgeContentType value) => value switch
    {
        KnowledgeContentType.Note => "note", KnowledgeContentType.Link => "link",
        _ => throw new InvalidOperationException("Unknown knowledge content type.")
    };
    private static KnowledgeContentType ContentTypeFromStorage(string value) => value switch
    {
        "note" => KnowledgeContentType.Note, "link" => KnowledgeContentType.Link,
        _ => throw new InvalidOperationException("Unknown knowledge content type.")
    };
}

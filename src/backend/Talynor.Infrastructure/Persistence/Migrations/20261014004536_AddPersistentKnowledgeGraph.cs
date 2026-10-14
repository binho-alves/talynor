using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Talynor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersistentKnowledgeGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KnowledgeMaps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeMaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeMaps_PersonalWorkspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "PersonalWorkspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    KnowledgeMapId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    PositionX = table.Column<double>(type: "REAL", nullable: false),
                    PositionY = table.Column<double>(type: "REAL", nullable: false),
                    ChildMapId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeNodes", x => x.Id);
                    table.UniqueConstraint("AK_KnowledgeNodes_KnowledgeMapId_Id", x => new { x.KnowledgeMapId, x.Id });
                    table.CheckConstraint("CK_KnowledgeNodes_FinitePositions", "abs(PositionX) <= 1.7976931348623157e308 AND abs(PositionY) <= 1.7976931348623157e308");
                    table.ForeignKey(
                        name: "FK_KnowledgeNodes_KnowledgeMaps_ChildMapId",
                        column: x => x.ChildMapId,
                        principalTable: "KnowledgeMaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KnowledgeNodes_KnowledgeMaps_KnowledgeMapId",
                        column: x => x.KnowledgeMapId,
                        principalTable: "KnowledgeMaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeEdges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    KnowledgeMapId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceNodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetNodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeEdges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeEdges_KnowledgeMaps_KnowledgeMapId",
                        column: x => x.KnowledgeMapId,
                        principalTable: "KnowledgeMaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KnowledgeEdges_KnowledgeNodes_KnowledgeMapId_SourceNodeId",
                        columns: x => new { x.KnowledgeMapId, x.SourceNodeId },
                        principalTable: "KnowledgeNodes",
                        principalColumns: new[] { "KnowledgeMapId", "Id" });
                    table.ForeignKey(
                        name: "FK_KnowledgeEdges_KnowledgeNodes_KnowledgeMapId_TargetNodeId",
                        columns: x => new { x.KnowledgeMapId, x.TargetNodeId },
                        principalTable: "KnowledgeNodes",
                        principalColumns: new[] { "KnowledgeMapId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeEdges_KnowledgeMapId_SourceNodeId",
                table: "KnowledgeEdges",
                columns: new[] { "KnowledgeMapId", "SourceNodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeEdges_KnowledgeMapId_TargetNodeId",
                table: "KnowledgeEdges",
                columns: new[] { "KnowledgeMapId", "TargetNodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeMaps_WorkspaceId",
                table: "KnowledgeMaps",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeNodes_ChildMapId",
                table: "KnowledgeNodes",
                column: "ChildMapId");

            // A simple ChildMapId FK cannot enforce workspace equality across two maps.
            // Keep Node free of duplicated WorkspaceId and enforce the invariant atomically in SQLite.
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_KnowledgeNodes_ChildWorkspace_Insert
                AFTER INSERT ON KnowledgeNodes
                WHEN NEW.ChildMapId IS NOT NULL AND EXISTS (
                    SELECT 1 FROM KnowledgeMaps parent JOIN KnowledgeMaps child ON child.Id = NEW.ChildMapId
                    WHERE parent.Id = NEW.KnowledgeMapId AND parent.WorkspaceId <> child.WorkspaceId)
                BEGIN SELECT RAISE(ABORT, 'Child map must belong to the same workspace.'); END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_KnowledgeNodes_ChildWorkspace_Update
                AFTER UPDATE OF KnowledgeMapId, ChildMapId ON KnowledgeNodes
                WHEN NEW.ChildMapId IS NOT NULL AND EXISTS (
                    SELECT 1 FROM KnowledgeMaps parent JOIN KnowledgeMaps child ON child.Id = NEW.ChildMapId
                    WHERE parent.Id = NEW.KnowledgeMapId AND parent.WorkspaceId <> child.WorkspaceId)
                BEGIN SELECT RAISE(ABORT, 'Child map must belong to the same workspace.'); END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_KnowledgeMaps_ChildWorkspace_Update
                AFTER UPDATE OF WorkspaceId ON KnowledgeMaps
                WHEN EXISTS (
                    SELECT 1 FROM KnowledgeNodes node
                    JOIN KnowledgeMaps parent ON parent.Id = node.KnowledgeMapId
                    JOIN KnowledgeMaps child ON child.Id = node.ChildMapId
                    WHERE (parent.Id = NEW.Id OR child.Id = NEW.Id) AND parent.WorkspaceId <> child.WorkspaceId)
                BEGIN SELECT RAISE(ABORT, 'Child map must belong to the same workspace.'); END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER TR_KnowledgeMaps_ChildWorkspace_Update;");
            migrationBuilder.Sql("DROP TRIGGER TR_KnowledgeNodes_ChildWorkspace_Update;");
            migrationBuilder.Sql("DROP TRIGGER TR_KnowledgeNodes_ChildWorkspace_Insert;");
            migrationBuilder.DropTable(
                name: "KnowledgeEdges");

            migrationBuilder.DropTable(
                name: "KnowledgeNodes");

            migrationBuilder.DropTable(
                name: "KnowledgeMaps");
        }
    }
}

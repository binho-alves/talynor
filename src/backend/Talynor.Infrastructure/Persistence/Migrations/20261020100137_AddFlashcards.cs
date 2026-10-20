using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Talynor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlashcards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Flashcards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    KnowledgeNodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Front = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Back = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ContentRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flashcards", x => x.Id);
                    table.CheckConstraint("CK_Flashcards_ContentRevision", "ContentRevision > 0");
                    table.ForeignKey(
                        name: "FK_Flashcards_KnowledgeNodes_KnowledgeNodeId",
                        column: x => x.KnowledgeNodeId,
                        principalTable: "KnowledgeNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Flashcards_KnowledgeNodeId_CreatedAtUtc_Id",
                table: "Flashcards",
                columns: new[] { "KnowledgeNodeId", "CreatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Flashcards");
        }
    }
}

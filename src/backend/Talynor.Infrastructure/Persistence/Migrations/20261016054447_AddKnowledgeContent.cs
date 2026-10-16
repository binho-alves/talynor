using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Talynor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KnowledgeContents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    KnowledgeNodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Body = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeContents", x => x.Id);
                    table.CheckConstraint("CK_KnowledgeContents_Payload", "(Type = 'note' AND Body IS NOT NULL AND Url IS NULL) OR (Type = 'link' AND Url IS NOT NULL AND Body IS NULL)");
                    table.CheckConstraint("CK_KnowledgeContents_Type", "Type IN ('note', 'link')");
                    table.ForeignKey(
                        name: "FK_KnowledgeContents_KnowledgeNodes_KnowledgeNodeId",
                        column: x => x.KnowledgeNodeId,
                        principalTable: "KnowledgeNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeContents_KnowledgeNodeId_CreatedAtUtc_Id",
                table: "KnowledgeContents",
                columns: new[] { "KnowledgeNodeId", "CreatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KnowledgeContents");
        }
    }
}

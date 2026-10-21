using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Talynor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFlashcardReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FlashcardReviews",
                columns: table => new
                {
                    ReviewId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FlashcardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContentRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    Result = table.Column<string>(type: "TEXT", maxLength: 9, nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlashcardReviews", x => x.ReviewId);
                    table.CheckConstraint("CK_FlashcardReviews_ContentRevision", "ContentRevision > 0");
                    table.CheckConstraint("CK_FlashcardReviews_Result", "Result IN ('correct', 'incorrect')");
                    table.ForeignKey(
                        name: "FK_FlashcardReviews_Flashcards_FlashcardId",
                        column: x => x.FlashcardId,
                        principalTable: "Flashcards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FlashcardReviewStates",
                columns: table => new
                {
                    FlashcardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReviewCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CorrectCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastReviewedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlashcardReviewStates", x => x.FlashcardId);
                    table.CheckConstraint("CK_FlashcardReviewStates_LastReviewedAtUtc", "LastReviewedAtUtc IS NOT NULL");
                    table.CheckConstraint("CK_FlashcardReviewStates_ReviewCounts", "ReviewCount >= 1 AND CorrectCount >= 0 AND CorrectCount <= ReviewCount");
                    table.ForeignKey(
                        name: "FK_FlashcardReviewStates_Flashcards_FlashcardId",
                        column: x => x.FlashcardId,
                        principalTable: "Flashcards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardReviews_FlashcardId_ReviewedAtUtc_ReviewId",
                table: "FlashcardReviews",
                columns: new[] { "FlashcardId", "ReviewedAtUtc", "ReviewId" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_FlashcardReviewStates_LastReviewedAtUtc_FlashcardId",
                table: "FlashcardReviewStates",
                columns: new[] { "LastReviewedAtUtc", "FlashcardId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlashcardReviews");

            migrationBuilder.DropTable(
                name: "FlashcardReviewStates");
        }
    }
}

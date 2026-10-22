using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Talynor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuizAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuizAttempts",
                columns: table => new
                {
                    AttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuizId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuizTitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContentRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    QuestionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CorrectCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizAttempts", x => x.AttemptId);
                    table.CheckConstraint("CK_QuizAttempts_ContentRevision", "ContentRevision > 0");
                    table.CheckConstraint("CK_QuizAttempts_CorrectCount", "CorrectCount >= 0 AND CorrectCount <= QuestionCount");
                    table.CheckConstraint("CK_QuizAttempts_QuestionCount", "QuestionCount BETWEEN 1 AND 10");
                    table.ForeignKey(
                        name: "FK_QuizAttempts_Quizzes_QuizId",
                        column: x => x.QuizId,
                        principalTable: "Quizzes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuizAttemptAnswers",
                columns: table => new
                {
                    AttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    SelectedOptionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CorrectOptionId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizAttemptAnswers", x => new { x.AttemptId, x.QuestionId });
                    table.CheckConstraint("CK_QuizAttemptAnswers_Position", "Position BETWEEN 1 AND 10");
                    table.ForeignKey(
                        name: "FK_QuizAttemptAnswers_QuizAttempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "QuizAttempts",
                        principalColumn: "AttemptId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuizAttemptAnswerOptions",
                columns: table => new
                {
                    AttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OptionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizAttemptAnswerOptions", x => new { x.AttemptId, x.QuestionId, x.OptionId });
                    table.CheckConstraint("CK_QuizAttemptAnswerOptions_Position", "Position BETWEEN 1 AND 4");
                    table.ForeignKey(
                        name: "FK_QuizAttemptAnswerOptions_QuizAttemptAnswers_AttemptId_QuestionId",
                        columns: x => new { x.AttemptId, x.QuestionId },
                        principalTable: "QuizAttemptAnswers",
                        principalColumns: new[] { "AttemptId", "QuestionId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttemptAnswerOptions_AttemptId_QuestionId_Position",
                table: "QuizAttemptAnswerOptions",
                columns: new[] { "AttemptId", "QuestionId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttemptAnswers_AttemptId_Position",
                table: "QuizAttemptAnswers",
                columns: new[] { "AttemptId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_QuizId_SubmittedAtUtc_AttemptId",
                table: "QuizAttempts",
                columns: new[] { "QuizId", "SubmittedAtUtc", "AttemptId" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuizAttemptAnswerOptions");

            migrationBuilder.DropTable(
                name: "QuizAttemptAnswers");

            migrationBuilder.DropTable(
                name: "QuizAttempts");
        }
    }
}

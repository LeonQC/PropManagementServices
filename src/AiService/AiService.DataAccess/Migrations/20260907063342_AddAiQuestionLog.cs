using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiService.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddAiQuestionLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_question_log",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    feature = table.Column<string>(type: "text", nullable: false),
                    model = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<string>(type: "text", nullable: true),
                    entity_id = table.Column<string>(type: "text", nullable: true),
                    total_latency_ms = table.Column<int>(type: "integer", nullable: false),
                    iterations = table.Column<int>(type: "integer", nullable: false),
                    tool_calls = table.Column<int>(type: "integer", nullable: false),
                    truncated = table.Column<bool>(type: "boolean", nullable: false),
                    truncation_reason = table.Column<string>(type: "text", nullable: true),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_question_log", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_question_log_correlation_id",
                table: "ai_question_log",
                column: "correlation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_question_log_feature_created_at",
                table: "ai_question_log",
                columns: new[] { "feature", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_question_log");
        }
    }
}

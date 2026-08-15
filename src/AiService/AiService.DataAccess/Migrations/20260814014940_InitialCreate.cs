using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiService.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_request_log",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    feature = table.Column<string>(type: "text", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: true),
                    entity_id = table.Column<string>(type: "text", nullable: true),
                    input_tokens = table.Column<int>(type: "integer", nullable: false),
                    output_tokens = table.Column<int>(type: "integer", nullable: false),
                    latency_ms = table.Column<int>(type: "integer", nullable: false),
                    cost_usd = table.Column<double>(type: "double precision", nullable: true),
                    chunk_count = table.Column<int>(type: "integer", nullable: true),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_request_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "prompt_templates",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    feature = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    system_prompt = table.Column<string>(type: "text", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prompt_templates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_request_log_entity_id",
                table: "ai_request_log",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_request_log_feature_created_at",
                table: "ai_request_log",
                columns: new[] { "feature", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_prompt_templates_feature_active",
                table: "prompt_templates",
                columns: new[] { "feature", "is_active" },
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_prompt_templates_feature_version",
                table: "prompt_templates",
                columns: new[] { "feature", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_request_log");

            migrationBuilder.DropTable(
                name: "prompt_templates");
        }
    }
}

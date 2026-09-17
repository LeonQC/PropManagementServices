using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiService.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddAiWorkFingerprints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_work_fingerprints",
                columns: table => new
                {
                    feature = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<string>(type: "text", nullable: false),
                    input_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_output_score = table.Column<double>(type: "double precision", nullable: true),
                    computed_at = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_work_fingerprints", x => new { x.feature, x.entity_id });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_work_fingerprints");
        }
    }
}

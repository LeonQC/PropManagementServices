using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiService.DataAccess.Migrations
{
    /// <summary>
    /// The worker stops hashing scoring inputs and starts tracking only what it wrote.
    ///
    /// <para>The fingerprint existed to stop the service rescoring in a loop after writing a
    /// score back. The score is no longer written back at all — it is computed on read in
    /// deals-service — so there is no loop to break and nothing to hash. What remains worth
    /// remembering is the score each rationale was written to explain, which is the baseline
    /// for judging that prose stale.</para>
    ///
    /// <para>Dropped rather than migrated: every fingerprint was derived from inputs that no
    /// longer feed anything here, and no rationale had been generated yet, so the only column
    /// worth keeping was empty in every row.</para>
    /// </summary>
    public partial class ReplaceFingerprintsWithWorkRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_work_fingerprints");

            migrationBuilder.CreateTable(
                name: "ai_work_records",
                columns: table => new
                {
                    feature = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<string>(type: "text", nullable: false),
                    last_output_score = table.Column<double>(type: "double precision", nullable: true),
                    computed_at = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_work_records", x => new { x.feature, x.entity_id });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_work_records");

            migrationBuilder.CreateTable(
                name: "ai_work_fingerprints",
                columns: table => new
                {
                    feature = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<string>(type: "text", nullable: false),
                    computed_at = table.Column<string>(type: "text", nullable: false),
                    input_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_output_score = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_work_fingerprints", x => new { x.feature, x.entity_id });
                });
        }
    }
}

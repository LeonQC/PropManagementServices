using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DealsService.DataAccess.Migrations
{
    /// <summary>
    /// The score stops being stored and becomes derived per read (see DealScore).
    ///
    /// <para>EF warns about data loss, and it is right that data goes away, but nothing is
    /// lost: every value in this column is reproducible from the deal row by a pure function,
    /// and keeping it would have been actively wrong. The stage-momentum component moves as
    /// days pass, so a stored score freezes at the last write and a deal stalling for months
    /// keeps reporting the number it had when someone last touched it.</para>
    /// </summary>
    public partial class DropStoredAiScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_score",
                table: "deals");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ai_score",
                table: "deals",
                type: "double precision",
                nullable: true);
        }
    }
}

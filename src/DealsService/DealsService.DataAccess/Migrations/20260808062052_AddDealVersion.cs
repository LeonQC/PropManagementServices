using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DealsService.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddDealVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "version",
                table: "deals",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Existing rows start at 1, matching CreateAsync, so "version >= 1" holds for
            // every deal and version 0 never reaches OpenSearch's external versioning.
            migrationBuilder.Sql("UPDATE deals SET version = 1 WHERE version = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "version",
                table: "deals");
        }
    }
}

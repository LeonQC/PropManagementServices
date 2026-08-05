using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ListingsService.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertyVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "version",
                table: "properties",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Existing rows start at 1, matching CreateAsync, so "version >= 1" holds for
            // every property and version 0 never reaches OpenSearch's external versioning.
            migrationBuilder.Sql("UPDATE properties SET version = 1 WHERE version = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "version",
                table: "properties");
        }
    }
}
